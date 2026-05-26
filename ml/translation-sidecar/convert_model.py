"""
One-time model conversion: Helsinki-NLP MarianMT → CTranslate2 INT8.

Downloads the specified language pair model from HuggingFace, converts it to
CTranslate2 INT8 format, and writes the output to models/{src}-{tgt}/.

Run this once after cloning the repo (or after adding a new language pair).
The output is gitignored — models are either baked into the Docker image at
build time (see Dockerfile) or generated locally with this script.

Usage:
    # Install conversion deps (not needed at runtime):
    pip install "transformers>=4.40,<5" huggingface_hub

    # Convert Korean→English (default):
    python convert_model.py

    # Convert a specific pair:
    python convert_model.py --pair ko-en

    # Add Japanese→English (add entry to HF_MODELS first):
    python convert_model.py --pair ja-en

    # Convert all configured pairs:
    python convert_model.py --pair all

    # Use float16 instead of int8 (larger, more accurate on GPU):
    python convert_model.py --pair ko-en --quantization float16

Output:
    models/{pair}/
        model.bin              CTranslate2 INT8 weights (~100 MB for ko-en)
        config.json            CTranslate2 model config
        source_vocabulary.json Source-side vocabulary
        target_vocabulary.json Target-side vocabulary
        source.spm             SentencePiece model for source language
        target.spm             SentencePiece model for target language
"""

from __future__ import annotations

import argparse
import shutil
from pathlib import Path

MODELS_DIR = Path(__file__).parent / "models"

# Language pair → HuggingFace model ID.
# To add a new language: append an entry and run:
#   python convert_model.py --pair {new-pair}
HF_MODELS: dict[str, str] = {
    "ko-en": "Helsinki-NLP/opus-mt-ko-en",
    # "ja-en": "Helsinki-NLP/opus-mt-ja-en",
    # "zh-en": "Helsinki-NLP/opus-mt-zh-en",
    # "fr-en": "Helsinki-NLP/opus-mt-fr-en",
    # "de-en": "Helsinki-NLP/opus-mt-de-en",
}


def convert(pair: str, quantization: str = "int8", source: str | None = None) -> None:
    """
    Convert a MarianMT model to CTranslate2 format.

    Args:
        pair: language pair key (e.g. "ko-en"), used for the output directory name.
        quantization: CTranslate2 quantization format.
        source: optional path to a local fine-tuned model directory.
                If None, the HuggingFace model ID from HF_MODELS[pair] is used.
    """
    if source is None and pair not in HF_MODELS:
        raise ValueError(
            f"Unknown pair: {pair!r}. Available: {list(HF_MODELS)}. "
            f"To add a new pair, add an entry to HF_MODELS in convert_model.py."
        )

    # Import here so the module is importable without transformers installed.
    import ctranslate2  # noqa: F401 (already in requirements.txt)

    try:
        from transformers import MarianTokenizer
    except ImportError as exc:
        raise SystemExit(
            "transformers is required for model conversion but is not installed.\n"
            "Run: pip install 'transformers>=4.40,<5' huggingface_hub"
        ) from exc

    # Use the provided local path (fine-tuned model) or the HF model ID.
    model_source = source if source is not None else HF_MODELS[pair]
    hf_model_id = HF_MODELS.get(pair, pair)  # fallback label for logging
    output_dir = MODELS_DIR / pair
    output_dir.mkdir(parents=True, exist_ok=True)

    if source:
        print(f"[{pair}] Converting local fine-tuned model at {model_source} ...")
    else:
        print(f"[{pair}] Downloading {hf_model_id} from HuggingFace ...")
    tokenizer = MarianTokenizer.from_pretrained(model_source)

    print(f"[{pair}] Converting to CTranslate2 ({quantization}) → {output_dir} ...")
    # TransformersConverter accepts HuggingFace model IDs and local checkpoint dirs.
    # Requires torch to be installed (not in runtime requirements.txt; installed
    # separately in the venv / at Docker build time).
    converter = ctranslate2.converters.TransformersConverter(
        model_source,
        low_cpu_mem_usage=True,
    )
    converter.convert(str(output_dir), quantization=quantization, force=True)

    # The CTranslate2 converter writes model.bin and vocab files but NOT the
    # SentencePiece .spm files needed at inference time.  Save them from the
    # tokenizer into a temp directory then copy with predictable names.
    tmp_dir = output_dir / "_hf_tmp"
    tmp_dir.mkdir(exist_ok=True)
    try:
        tokenizer.save_pretrained(str(tmp_dir))
        _copy_spm_files(tmp_dir, output_dir, pair)
    finally:
        shutil.rmtree(tmp_dir, ignore_errors=True)

    print(f"[{pair}] Done. Model at {output_dir}")
    _print_size_summary(output_dir)


def _copy_spm_files(src_dir: Path, dst_dir: Path, pair: str) -> None:
    """Copy source.spm and target.spm from the HF tokenizer cache to dst_dir."""
    spm_files = sorted(src_dir.glob("*.spm"))

    if len(spm_files) == 0:
        raise RuntimeError(
            f"No .spm files found in {src_dir} after saving tokenizer. "
            f"Check that HuggingFace model {HF_MODELS[pair]} includes SentencePiece files."
        )

    if len(spm_files) == 1:
        # Single shared vocabulary (some models use one SP model for both sides).
        shutil.copy(spm_files[0], dst_dir / "source.spm")
        shutil.copy(spm_files[0], dst_dir / "target.spm")
        print(f"[{pair}]   Shared vocab: copied {spm_files[0].name} → source.spm + target.spm")
    else:
        # Separate source and target sentencepiece models (typical for MarianMT).
        # opus-mt-ko-en ships source.spm (Korean) and target.spm (English).
        for spm_file in spm_files:
            dest_name = spm_file.name  # "source.spm" or "target.spm"
            if dest_name not in ("source.spm", "target.spm"):
                # Fallback: name them by order (first=source, second=target).
                dest_name = "source.spm" if spm_files.index(spm_file) == 0 else "target.spm"
            shutil.copy(spm_file, dst_dir / dest_name)
            print(f"[{pair}]   Copied {spm_file.name} → {dest_name}")


def _print_size_summary(model_dir: Path) -> None:
    total = sum(f.stat().st_size for f in model_dir.rglob("*") if f.is_file())
    print(f"[{model_dir.name}] Total size: {total / 1_048_576:.1f} MB")


def main() -> None:
    parser = argparse.ArgumentParser(
        description="Download and convert a MarianMT model to CTranslate2 INT8 format."
    )
    parser.add_argument(
        "--pair",
        default="ko-en",
        help=f"Language pair to convert, or 'all'. Available: {list(HF_MODELS)}",
    )
    parser.add_argument(
        "--quantization",
        default="int8",
        choices=["int8", "int16", "float16", "float32"],
        help="Weight quantization format (int8 = smallest and fastest on CPU).",
    )
    parser.add_argument(
        "--source",
        default=None,
        metavar="DIR",
        help=(
            "Path to a local fine-tuned model directory to convert instead of downloading "
            "from HuggingFace. Use this after running train.py. "
            "Example: --source models/ko-en-finetuned --pair ko-en"
        ),
    )
    args = parser.parse_args()

    if args.source and args.pair == "all":
        raise SystemExit("--source cannot be used with --pair all (source is for a single pair).")

    pairs = list(HF_MODELS) if args.pair == "all" else [args.pair]
    for p in pairs:
        convert(p, args.quantization, source=args.source)


if __name__ == "__main__":
    main()
