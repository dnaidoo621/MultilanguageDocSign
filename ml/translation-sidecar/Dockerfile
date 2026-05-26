FROM python:3.12-slim

WORKDIR /app

# Runtime dependencies.
COPY requirements.txt .
RUN pip install --no-cache-dir -r requirements.txt

# Conversion dependencies — only needed for the model download/convert step below.
# TransformersConverter requires torch; it is NOT imported at runtime.
# Install CPU-only torch to keep the image lean (translation runs on ctranslate2, not torch).
RUN pip install --no-cache-dir \
    "transformers>=4.40,<5" \
    huggingface_hub>=0.23 \
    torch --extra-index-url https://download.pytorch.org/whl/cpu

# Copy application code.
COPY glossary.py translator.py main.py convert_model.py ./

# Download Helsinki-NLP/opus-mt-ko-en from HuggingFace and convert to CTranslate2 INT8.
# This bakes the model (~100 MB) into the image at build time so the container starts
# immediately without any runtime internet dependency.
#
# NOTE: On Apple Silicon running Docker via Rosetta the BLAS backend is x86 emulation.
#       For best CPU performance on M-series Macs, run the sidecar natively in a venv.
ENV HF_HUB_DISABLE_PROGRESS_BARS=1
RUN python convert_model.py --pair ko-en --quantization int8

EXPOSE 8001

CMD ["uvicorn", "main:app", "--host", "0.0.0.0", "--port", "8001"]
