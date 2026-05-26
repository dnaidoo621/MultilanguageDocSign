"""
CTranslate2 / MarianMT inference pool.

One TranslatorPool singleton is shared across all FastAPI requests.
Language pair models are loaded eagerly at startup (via TRANSLATION_PRELOAD env var)
and kept warm in memory. Loading is thread-safe; inference is lock-free once loaded
because ctranslate2.Translator.translate_batch() is thread-safe.
"""

from __future__ import annotations

import threading
from pathlib import Path

import ctranslate2
import sentencepiece as spm

from glossary import get_target_prefix_tokens, post_process

MODELS_DIR = Path(__file__).parent / "models"

# Special piece tokens to strip from CTranslate2 output before decoding.
_SPECIAL_PIECES = {"</s>", "<pad>", "<unk>", "<s>"}


class TranslatorPool:
    """
    Lazy-loading pool of CTranslate2 Translator instances, keyed by language pair.

    Concurrency model:
    - A per-pair threading.Lock guards the initial model load.
    - Once loaded, ctranslate2.Translator.translate_batch() is called without a lock —
      CTranslate2 is thread-safe for concurrent inference calls on the same Translator.
    """

    def __init__(self) -> None:
        self._translators: dict[str, ctranslate2.Translator] = {}
        self._sp_src: dict[str, spm.SentencePieceProcessor] = {}
        self._sp_tgt: dict[str, spm.SentencePieceProcessor] = {}
        self._load_locks: dict[str, threading.Lock] = {}
        self._registry_lock = threading.Lock()

    # ------------------------------------------------------------------
    # Public API
    # ------------------------------------------------------------------

    def load(self, src: str, tgt: str) -> None:
        """Eagerly load a language pair. Safe to call multiple times."""
        self._ensure_loaded(f"{src}-{tgt}")

    def translate_batch(
        self,
        texts: list[str],
        src: str,
        tgt: str,
    ) -> list[str]:
        """
        Translate a list of source strings and return translations in the same order.

        Args:
            texts: source text blocks (one per OCR block, not one per page).
            src: source language code (e.g. "ko").
            tgt: target language code (e.g. "en").

        Returns:
            list of translated strings, same length and order as ``texts``.

        Raises:
            FileNotFoundError: if no model exists for the requested pair.
        """
        if not texts:
            return []

        pair = f"{src}-{tgt}"
        self._ensure_loaded(pair)

        translator = self._translators[pair]
        sp_src = self._sp_src[pair]
        sp_tgt = self._sp_tgt[pair]

        # Tokenise all inputs in one pass.
        tokenized: list[list[str]] = [
            sp_src.encode_as_pieces(t) for t in texts
        ]

        # Build per-sentence target_prefix lists for block-opening glossary terms.
        # get_target_prefix_tokens returns [] when no constraint applies; CTranslate2
        # treats an empty list (or None entry) as "no constraint".
        target_prefixes: list[list[str] | None] = [
            (get_target_prefix_tokens(t, src, tgt, sp_tgt) or None)
            for t in texts
        ]
        # Only pass target_prefix if at least one sentence has a prefix constraint.
        tp_kwarg: dict = {}
        if any(p is not None for p in target_prefixes):
            # CTranslate2 requires the list to be the same length as the input batch.
            tp_kwarg["target_prefix"] = [
                p if p is not None else [] for p in target_prefixes
            ]

        results = translator.translate_batch(
            tokenized,
            beam_size=4,
            # 128 tokens covers most legal clause translations comfortably.
            # Prevents runaway output on short/ambiguous phrases — MarianMT is prone
            # to repetition loops on isolated short phrases without this guard.
            max_decoding_length=128,
            no_repeat_ngram_size=4,
            repetition_penalty=1.3,
            replace_unknowns=True,
            max_batch_size=32,
            **tp_kwarg,
        )

        translated: list[str] = []
        for source_text, result in zip(texts, results):
            pieces = [p for p in result.hypotheses[0] if p not in _SPECIAL_PIECES]
            raw = sp_tgt.decode(pieces)
            # Apply post-translation glossary substitution for legal terms.
            final = post_process(source_text, raw, src, tgt)
            translated.append(final)

        return translated

    def available_pairs(self) -> list[str]:
        """Return language pairs for which a converted model directory exists."""
        if not MODELS_DIR.exists():
            return []
        return [
            d.name
            for d in MODELS_DIR.iterdir()
            if d.is_dir() and (d / "model.bin").exists()
        ]

    def loaded_pairs(self) -> list[str]:
        return list(self._translators.keys())

    # ------------------------------------------------------------------
    # Internals
    # ------------------------------------------------------------------

    def _get_lock(self, pair: str) -> threading.Lock:
        with self._registry_lock:
            if pair not in self._load_locks:
                self._load_locks[pair] = threading.Lock()
            return self._load_locks[pair]

    def _ensure_loaded(self, pair: str) -> None:
        if pair in self._translators:
            return

        lock = self._get_lock(pair)
        with lock:
            if pair in self._translators:  # double-checked locking
                return

            model_dir = MODELS_DIR / pair
            if not model_dir.exists() or not (model_dir / "model.bin").exists():
                raise FileNotFoundError(
                    f"No converted model found at {model_dir}. "
                    f"Run: python convert_model.py --pair {pair}"
                )

            # CTranslate2 INT8 on CPU — on ARM64/macOS this uses Apple Accelerate.
            # intra_threads=4 lets BLAS parallelise the matmul within each call.
            # inter_threads=1 is fine for a sidecar that serialises at the HTTP layer.
            translator = ctranslate2.Translator(
                str(model_dir),
                device="cpu",
                compute_type="int8",
                inter_threads=1,
                intra_threads=4,
            )

            src, tgt = pair.split("-", 1)

            sp_src_proc = spm.SentencePieceProcessor(
                model_file=str(model_dir / "source.spm")
            )
            sp_tgt_proc = spm.SentencePieceProcessor(
                model_file=str(model_dir / "target.spm")
            )

            self._translators[pair] = translator
            self._sp_src[pair] = sp_src_proc
            self._sp_tgt[pair] = sp_tgt_proc


# Module-level singleton shared across all FastAPI requests.
pool = TranslatorPool()
