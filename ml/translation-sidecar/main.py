"""
LinguaSign Translation Sidecar — CTranslate2 / MarianMT inference service.

Endpoints:
  POST /translate  — translate a batch of text blocks
  GET  /health     — readiness probe (reports loaded models)
  GET  /models     — list available and loaded language pairs

Running locally (Apple Silicon — fastest option):
    python3.12 -m venv .venv && source .venv/bin/activate
    pip install -r requirements.txt
    # One-time setup (needs internet + transformers package):
    pip install "transformers>=4.40,<5" huggingface_hub
    python convert_model.py --pair ko-en
    uvicorn main:app --host 0.0.0.0 --port 8001

Running in Docker (cross-platform, model baked into image):
    docker build -t linguasign/translation .
    docker run -p 8001:8001 linguasign/translation

Environment variables:
  TRANSLATION_PRELOAD  Comma-separated language pairs to load at startup.
                       Default: "ko-en". Add pairs as needed: "ko-en,ja-en,zh-en"
"""

from __future__ import annotations

import logging
import os

from fastapi import FastAPI, HTTPException
from pydantic import BaseModel

from translator import pool

logger = logging.getLogger("linguasign-translation")
logging.basicConfig(level=logging.INFO, format="%(levelname)s  %(name)s  %(message)s")

app = FastAPI(
    title="LinguaSign Translation Sidecar",
    description="MarianMT / CTranslate2 inference for per-language translation.",
    version="1.0.0",
)

# ---------------------------------------------------------------------------
# Startup: eagerly warm up language-pair models listed in TRANSLATION_PRELOAD
# ---------------------------------------------------------------------------
_preload_env = os.getenv("TRANSLATION_PRELOAD", "ko-en")

for _pair_str in _preload_env.split(","):
    _pair_str = _pair_str.strip()
    if not _pair_str:
        continue
    if "-" not in _pair_str:
        logger.warning("Skipping malformed TRANSLATION_PRELOAD entry: %r", _pair_str)
        continue
    _src, _tgt = _pair_str.split("-", 1)
    try:
        pool.load(_src, _tgt)
        logger.info("Loaded translation model: %s", _pair_str)
    except FileNotFoundError as exc:
        # Missing model is a deployment error, not a startup crash.
        # /health will report zero loaded models so the k8s readiness probe fails.
        logger.error("Could not load %s: %s", _pair_str, exc)


# ---------------------------------------------------------------------------
# Request / response models
# ---------------------------------------------------------------------------

class TranslationItem(BaseModel):
    id: str    # GUID string from the backend; echoed back verbatim
    text: str


class TranslateRequest(BaseModel):
    source_lang: str
    target_lang: str
    items: list[TranslationItem]


class TranslationResult(BaseModel):
    id: str
    text: str


class TranslateResponse(BaseModel):
    model: str
    translations: list[TranslationResult]


# ---------------------------------------------------------------------------
# Routes
# ---------------------------------------------------------------------------

@app.get("/health")
def health():
    return {
        "status": "ok",
        "service": "linguasign-translation",
        "models_loaded": pool.loaded_pairs(),
    }


@app.get("/models")
def models():
    return {
        "available": pool.available_pairs(),
        "loaded": pool.loaded_pairs(),
    }


@app.post("/translate", response_model=TranslateResponse)
def translate(req: TranslateRequest):
    if not req.items:
        return TranslateResponse(
            model=_model_name(req.source_lang, req.target_lang),
            translations=[],
        )

    texts = [item.text for item in req.items]

    try:
        translated_texts = pool.translate_batch(texts, req.source_lang, req.target_lang)
    except FileNotFoundError as exc:
        raise HTTPException(status_code=503, detail=str(exc))

    return TranslateResponse(
        model=_model_name(req.source_lang, req.target_lang),
        translations=[
            TranslationResult(id=item.id, text=t)
            for item, t in zip(req.items, translated_texts)
        ],
    )


def _model_name(src: str, tgt: str) -> str:
    return f"opus-mt-{src}-{tgt}"
