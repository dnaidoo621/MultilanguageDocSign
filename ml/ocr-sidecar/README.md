# LinguaSign OCR Sidecar

Surya-based OCR + layout extraction. The .NET backend talks to this over HTTP
(`POST /ocr`), so the rest of the system is decoupled from Surya's API.

## Run natively (recommended on Apple Silicon — uses MPS acceleration)

```bash
cd ml/ocr-sidecar
python -m venv .venv
source .venv/bin/activate
pip install -r requirements.txt
uvicorn main:app --host 0.0.0.0 --port 8000
```

First run downloads Surya model weights from Hugging Face (a few GB).

## Run with Docker (portable, CPU-only)

```bash
cd ml/ocr-sidecar
docker build -t linguasign-ocr .
docker run -p 8000:8000 linguasign-ocr
```

## API

`POST /ocr` — multipart form, field `file` = PDF. Returns:

```json
{
  "pages": [
    {
      "number": 1,
      "width": 1240.0,
      "height": 1754.0,
      "blocks": [
        { "text": "고용 계약서", "language": null, "confidence": 0.98, "bbox": [x0, y0, x1, y1] }
      ]
    }
  ]
}
```

`bbox` is `[x0, y0, x1, y1]` in page **pixel** coordinates at the render scale; the
backend stores page width/height so the frontend can scale boxes to any display size.

`GET /health` — liveness probe.

## Configure the backend

Point the backend at this service via `Ocr:BaseUrl` (default `http://localhost:8000`).
