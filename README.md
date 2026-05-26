# LinguaSign

Read a contract written in a language you don't speak — clause by clause, with the risky bits
flagged in plain English — and sign it with a record of everything that happened. Built for the
person standing in a foreign rental office holding a lease they can't read.

> LinguaSign helps you *understand* a document. It is not a certified translation and it is not
> legal advice. AI output can be wrong; for anything that matters, talk to a qualified
> professional.

![Landing](docs/screenshots/01-landing-light.png)

## The problem it's actually solving

An English speaker in Seoul gets handed a `전세` lease. A new hire signs a Korean employment
contract on day one. The usual options are bad: screenshot it into a translator and lose all
the structure, ask a friend, or just sign and hope. LinguaSign takes the PDF and gives back the
original and a translation side by side, every clause linked to its source, with auto-renewals,
penalties, arbitration and the like called out — then lets you sign and walk away with a sealed
audit package.

The bilingual reader is the heart of it. Hover any clause and its counterpart lights up on the
other side; a line is drawn between them so you can see exactly which source text produced which
translation. Nothing is paraphrased away.

![Bilingual reader](docs/screenshots/06-reader-mirrored.png)

## How it works

Upload a PDF and a short pipeline runs: OCR (Surya) pulls text and bounding boxes, the document
is segmented into clauses, each page is translated by a dedicated neural MT model with a legal
glossary applied, and a hybrid risk pass (deterministic rules + an LLM) flags dangerous clauses.
You watch it happen and then open the reader.

![Processing pipeline](docs/screenshots/05-pipeline-done.png)

A few decisions that matter:

- **Everything runs locally by default.** OCR and translation are self-hosted Python services.
  Risk analysis goes through [Ollama](https://ollama.com). No document content leaves the machine,
  which is the whole point for sensitive paperwork. Swapping to a GPU host you control is a config
  change, not a rewrite — see [docs/DEPLOY.md](docs/DEPLOY.md).
- **Translation uses a purpose-built MT model, not a general LLM.** MarianMT via CTranslate2 INT8
  is 78 MB, uses ~500 MB RAM, and starts in 2 seconds — compared to 4 GB and 30 seconds for a 7B
  LLM. This frees up the 16 GB unified memory for the OCR and risk models.
- **Translation is per-page, never the whole document in one pass.** The first page is readable in
  seconds while the rest is still working.
- **Risk detection is hybrid and the rules win ties.** Missing a high-risk clause is the worst
  failure mode for this kind of tool, so keyword rules act as a floor the model can't undershoot.

For the full picture — system diagram, processing sequence, data model, deployment topology —
see [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md).

When you're done reviewing, you sign (a visible stamp is written onto the PDF with a hash,
timestamp and IP), and every action lands in an append-only audit ledger you can export as a ZIP.

![Signing and audit ledger](docs/screenshots/09-signing.png)

## Stack

- **Front end** — Next.js 16 / React 19, App Router, TypeScript. Light/dark theme.
- **Backend** — ASP.NET Core on .NET 10, a modular monolith (Documents, Translation, Analysis,
  Signing, Audit, Export), Hangfire for the background pipeline.
- **Data / auth** — Supabase (Postgres + auth + storage). Five Postgres schemas, one per module.
- **OCR** — Surya, in a FastAPI sidecar.
- **Translation** — MarianMT (`Helsinki-NLP/opus-mt-ko-en`) via CTranslate2 INT8, in a dedicated
  FastAPI sidecar. 78 MB model, ~500 MB RAM, 2s startup. Legal-term glossary post-processing.
  Switch to Ollama at any time with `Translation:Engine=ollama`.
- **Risk analysis** — Ollama, `qwen2.5:7b`. Hybrid: deterministic keyword rules + LLM, higher risk wins.
- **PDF** — PdfSharp for the signature stamp (MIT-licensed; deliberately not iText).

## Quick start (local)

You'll need the .NET 10 SDK, Node 22+, Docker, Python 3.12, Ollama, and a free Supabase project.

```bash
docker compose up -d                              # Postgres

# OCR sidecar (downloads Surya weights on first run, a few GB)
cd ml/ocr-sidecar && python3.12 -m venv .venv && source .venv/bin/activate \
  && pip install -r requirements.txt && uvicorn main:app --port 8000

# Translation sidecar — one-time model download + conversion (~300 MB → 78 MB INT8)
cd ml/translation-sidecar && python3.12 -m venv .venv && source .venv/bin/activate \
  && pip install -r requirements.txt \
  && pip install "transformers>=4.40,<5" huggingface_hub torch --extra-index-url https://download.pytorch.org/whl/cpu \
  && python convert_model.py --pair ko-en \
  && uvicorn main:app --port 8001

ollama pull qwen2.5:7b                            # for risk analysis

cd backend && dotnet run --project src/LinguaSign.Api --launch-profile http   # API on :5080

cd frontend && cp .env.example .env.local && npm install && npm run dev        # web on :3000
```

Put your Supabase URL + anon key in `frontend/.env.local`, and the URL in the backend's
user-secrets (`dotnet user-secrets set "Supabase:Url" ...`). Turn off email confirmation in
Supabase for local dev. Full instructions, plus **cloud** and **Kubernetes** deployments, are in
[docs/DEPLOY.md](docs/DEPLOY.md).

## Tests

```bash
cd backend && dotnet test            # xUnit: unit + EF integration (integration needs Postgres up)
cd frontend && npx playwright test   # e2e: landing, auth, dashboard, full pipeline, negatives
```

The backend suite (52 tests) covers the risk rules, the legal glossary, and the LLM JSON parsers
(including malformed responses and the smaller-model output shape that once crashed a job), plus
EF-backed service tests against an ephemeral Postgres database — ownership checks, the
not-yet-extracted negative paths, OCR-failure handling, and the rules-override-LLM risk merge.

The Playwright suite covers each screen positively and negatively (auth gating, wrong
credentials, non-PDF rejection, theme persistence) and runs the whole upload → translate →
analyze → sign → export flow end to end. The screenshots in this README are produced by
`CAPTURE=1 npx playwright test screenshots.spec.ts`.

## Layout

```
backend/               .NET 10 modular monolith (+ Dockerfile, tests/)
frontend/              Next.js app (+ Dockerfile, e2e/ Playwright)
ml/ocr-sidecar/        FastAPI + Surya OCR service
ml/translation-sidecar/ FastAPI + MarianMT/CTranslate2 INT8 translation service
deploy/k8s/            Kubernetes manifests
docs/                  ARCHITECTURE.md, DEPLOY.md, screenshots/
docker-compose.yml
```

## Status

All four MVP phases plus a full UI pass are done and verified end to end: upload → OCR →
bilingual translation → risk analysis → e-signature → audit export. It's a working, demoable
product, not a prototype.

Known next steps: source-language auto-detection in the OCR sidecar, hand-drawn signature images,
a layout-matched bilingual PDF export, and — for legally-weightier signatures — Documenso / AES.

_License: TBD._
