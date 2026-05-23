"""
LinguaSign OCR sidecar — Surya-based text + layout extraction.

Exposes POST /ocr (multipart PDF) and returns pages/blocks with bounding boxes
in page pixel coordinates. The .NET backend consumes this JSON contract via
SuryaOcrClient, so the rest of the system is decoupled from Surya's API.

Run (native, recommended on Apple Silicon for MPS acceleration):
    python -m venv .venv && source .venv/bin/activate
    pip install -r requirements.txt
    uvicorn main:app --host 0.0.0.0 --port 8000
"""

import io

import pypdfium2 as pdfium
from fastapi import FastAPI, File, HTTPException, UploadFile
from surya.detection import DetectionPredictor
from surya.recognition import RecognitionPredictor

# Render scale: ~144 DPI. Bounding boxes are returned at this resolution and the
# backend stores page width/height so the frontend can scale boxes to any display size.
RENDER_SCALE = 2.0

app = FastAPI(title="LinguaSign OCR Sidecar")

# Models are heavy — load once at process start. First run downloads weights.
detection_predictor = DetectionPredictor()
recognition_predictor = RecognitionPredictor()


@app.get("/health")
def health():
    return {"status": "ok", "service": "linguasign-ocr"}


@app.post("/ocr")
async def ocr(file: UploadFile = File(...)):
    if not (file.filename or "").lower().endswith(".pdf"):
        raise HTTPException(status_code=400, detail="Only PDF files are supported.")

    data = await file.read()
    if not data:
        raise HTTPException(status_code=400, detail="Empty file.")

    images, sizes = _render_pages(data)

    # Surya: detect text regions, then recognize. Language is auto-detected.
    predictions = recognition_predictor(images, det_predictor=detection_predictor)

    pages = []
    for index, prediction in enumerate(predictions):
        width, height = sizes[index]
        blocks = []
        for line in getattr(prediction, "text_lines", []):
            bbox = [float(c) for c in line.bbox]  # [x0, y0, x1, y1]
            blocks.append(
                {
                    "text": line.text,
                    "language": None,  # populated in a later phase
                    "confidence": float(getattr(line, "confidence", 0.0) or 0.0),
                    "bbox": bbox,
                }
            )
        pages.append(
            {
                "number": index + 1,
                "width": float(width),
                "height": float(height),
                "blocks": blocks,
            }
        )

    return {"pages": pages}


def _render_pages(pdf_bytes: bytes):
    """Render each PDF page to a PIL image at RENDER_SCALE."""
    pdf = pdfium.PdfDocument(io.BytesIO(pdf_bytes))
    images, sizes = [], []
    try:
        for i in range(len(pdf)):
            page = pdf[i]
            pil = page.render(scale=RENDER_SCALE).to_pil()
            images.append(pil)
            sizes.append((pil.width, pil.height))
        return images, sizes
    finally:
        pdf.close()
