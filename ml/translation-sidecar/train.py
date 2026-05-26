"""
Fine-tune Helsinki-NLP MarianMT on a Korean legal corpus.

This improves legal-term accuracy beyond what the base model achieves on
general Korean↔English text.  Run this once when you have training data;
the fine-tuned checkpoint replaces the base model (no code changes needed).

Recommended corpus:
  AIHub 법률/특허 번역 말뭉치 (Korean-English Legal Translation Corpus)
  https://aihub.or.kr  (free registration, ~1.6 M sentence pairs, legal domain)

Expected data format — a UTF-8 TSV file with two columns, no header:
    Korean source text \\t English reference translation
    준거법은 대한민국법을 따른다.\\tThe governing law shall be the law of Korea.
    ...

Usage:
    # Install training deps (not in requirements.txt — only for training):
    pip install "transformers>=4.40,<5" datasets torch accelerate

    # Fine-tune on your TSV corpus:
    python train.py --data path/to/legal_pairs.tsv --output models/ko-en-finetuned

    # Swap the fine-tuned model in as the production model:
    python convert_model.py --source models/ko-en-finetuned --pair ko-en

    # On Apple Silicon use --device mps for faster training (default: auto-detect):
    python train.py --data legal.tsv --output models/ko-en-finetuned --device mps

Training notes:
  - 16 GB M-series Mac: fine-tunes MarianMT comfortably on 10 k–100 k examples.
  - ~2–6 hours for 100 k sentence pairs at the default settings (3 epochs, bs=16).
  - Increase --batch_size to 32 if you have spare RAM; decrease to 8 on 8 GB machines.
  - 3 epochs is usually enough for domain adaptation; watch the eval loss.
  - After fine-tuning, re-run convert_model.py to get an INT8 CTranslate2 model.
"""

from __future__ import annotations

import argparse
import csv
import os
from pathlib import Path
from typing import Any


# ---------------------------------------------------------------------------
# Dataset helpers
# ---------------------------------------------------------------------------

def load_pairs(tsv_path: str) -> tuple[list[str], list[str]]:
    """Load (source, target) pairs from a two-column TSV file."""
    sources, targets = [], []
    with open(tsv_path, encoding="utf-8", newline="") as f:
        reader = csv.reader(f, delimiter="\t")
        for row_num, row in enumerate(reader, start=1):
            if len(row) < 2:
                print(f"  Warning: skipping malformed row {row_num}: {row!r}")
                continue
            src, tgt = row[0].strip(), row[1].strip()
            if src and tgt:
                sources.append(src)
                targets.append(tgt)
    return sources, targets


def make_dataset(sources: list[str], targets: list[str]):
    """Build a HuggingFace Dataset from parallel lists."""
    try:
        from datasets import Dataset  # type: ignore[import]
    except ImportError as exc:
        raise SystemExit(
            "datasets is required for training but is not installed.\n"
            "Run: pip install datasets"
        ) from exc

    return Dataset.from_dict({"src": sources, "tgt": targets})


# ---------------------------------------------------------------------------
# Preprocessing
# ---------------------------------------------------------------------------

def preprocess_function(examples: dict[str, Any], tokenizer, max_length: int = 128):
    """Tokenise a batch of source/target pairs for seq2seq training."""
    model_inputs = tokenizer(
        examples["src"],
        max_length=max_length,
        truncation=True,
        padding=False,
    )
    with tokenizer.as_target_tokenizer():
        labels = tokenizer(
            examples["tgt"],
            max_length=max_length,
            truncation=True,
            padding=False,
        )
    model_inputs["labels"] = labels["input_ids"]
    return model_inputs


# ---------------------------------------------------------------------------
# Main training routine
# ---------------------------------------------------------------------------

def train(
    data_path: str,
    output_dir: str,
    base_model: str = "Helsinki-NLP/opus-mt-ko-en",
    num_epochs: int = 3,
    batch_size: int = 16,
    learning_rate: float = 5e-5,
    eval_fraction: float = 0.02,
    max_length: int = 128,
    device: str = "auto",
) -> None:
    try:
        import torch
        from transformers import (
            MarianMTModel,
            MarianTokenizer,
            Seq2SeqTrainer,
            Seq2SeqTrainingArguments,
            DataCollatorForSeq2Seq,
        )
    except ImportError as exc:
        raise SystemExit(
            "transformers and torch are required for training.\n"
            "Run: pip install 'transformers>=4.40,<5' torch accelerate"
        ) from exc

    # ---- device ----
    if device == "auto":
        if torch.backends.mps.is_available():
            device = "mps"
            print("Using Apple Silicon MPS backend for training.")
        elif torch.cuda.is_available():
            device = "cuda"
            print("Using CUDA GPU for training.")
        else:
            device = "cpu"
            print("Using CPU for training (will be slow — consider --device mps on Apple Silicon).")

    # ---- data ----
    print(f"Loading pairs from {data_path} ...")
    sources, targets = load_pairs(data_path)
    print(f"  Loaded {len(sources):,} sentence pairs.")

    dataset = make_dataset(sources, targets)

    # Split off a small eval set.
    n_eval = max(1, int(len(dataset) * eval_fraction))
    split = dataset.train_test_split(test_size=n_eval, seed=42)
    train_dataset = split["train"]
    eval_dataset = split["test"]
    print(f"  Train: {len(train_dataset):,}   Eval: {len(eval_dataset):,}")

    # ---- model + tokenizer ----
    print(f"Loading base model: {base_model} ...")
    tokenizer = MarianTokenizer.from_pretrained(base_model)
    model = MarianMTModel.from_pretrained(base_model)

    # ---- tokenise ----
    from functools import partial
    preprocess = partial(preprocess_function, tokenizer=tokenizer, max_length=max_length)
    train_dataset = train_dataset.map(preprocess, batched=True, remove_columns=["src", "tgt"])
    eval_dataset = eval_dataset.map(preprocess, batched=True, remove_columns=["src", "tgt"])

    # ---- training args ----
    Path(output_dir).mkdir(parents=True, exist_ok=True)
    training_args = Seq2SeqTrainingArguments(
        output_dir=output_dir,
        num_train_epochs=num_epochs,
        per_device_train_batch_size=batch_size,
        per_device_eval_batch_size=batch_size,
        learning_rate=learning_rate,
        warmup_steps=min(500, len(train_dataset) // (batch_size * 4)),
        weight_decay=0.01,
        logging_steps=max(1, len(train_dataset) // (batch_size * 20)),
        eval_strategy="epoch",
        save_strategy="epoch",
        load_best_model_at_end=True,
        metric_for_best_model="eval_loss",
        greater_is_better=False,
        predict_with_generate=False,  # faster; we don't compute BLEU during training
        fp16=(device == "cuda"),       # fp16 on CUDA only; MPS and CPU use float32
        use_mps_device=(device == "mps"),
        report_to="none",
    )

    data_collator = DataCollatorForSeq2Seq(tokenizer, model=model, padding=True)

    trainer = Seq2SeqTrainer(
        model=model,
        args=training_args,
        train_dataset=train_dataset,
        eval_dataset=eval_dataset,
        tokenizer=tokenizer,
        data_collator=data_collator,
    )

    # ---- train ----
    print("Starting fine-tuning ...")
    trainer.train()

    # ---- save ----
    trainer.save_model(output_dir)
    tokenizer.save_pretrained(output_dir)
    print(f"\nFine-tuned model saved to {output_dir}")
    print(
        "\nNext steps:\n"
        "  1. Evaluate translation quality on held-out legal documents.\n"
        f"  2. Convert to CTranslate2: python convert_model.py --source {output_dir} --pair ko-en\n"
        "  3. Restart the translation sidecar — it will pick up the new model automatically."
    )


# ---------------------------------------------------------------------------
# Entry point
# ---------------------------------------------------------------------------

def main() -> None:
    parser = argparse.ArgumentParser(
        description="Fine-tune MarianMT on a Korean legal corpus."
    )
    parser.add_argument(
        "--data",
        required=True,
        help="Path to a two-column TSV file: Korean\\tEnglish",
    )
    parser.add_argument(
        "--output",
        default="models/ko-en-finetuned",
        help="Directory to save the fine-tuned model (default: models/ko-en-finetuned).",
    )
    parser.add_argument(
        "--base_model",
        default="Helsinki-NLP/opus-mt-ko-en",
        help="HuggingFace model to fine-tune from.",
    )
    parser.add_argument("--epochs", type=int, default=3)
    parser.add_argument("--batch_size", type=int, default=16)
    parser.add_argument("--lr", type=float, default=5e-5)
    parser.add_argument(
        "--device",
        default="auto",
        choices=["auto", "mps", "cuda", "cpu"],
        help="Training device. 'auto' picks mps → cuda → cpu.",
    )

    args = parser.parse_args()

    train(
        data_path=args.data,
        output_dir=args.output,
        base_model=args.base_model,
        num_epochs=args.epochs,
        batch_size=args.batch_size,
        learning_rate=args.lr,
        device=args.device,
    )


if __name__ == "__main__":
    main()
