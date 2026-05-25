# LinguaSign

**Understand and confidently sign multilingual documents.** Upload a document, review a
synchronized translation with risk highlights, then sign — with a full audit trail.

> ⚠️ **LinguaSign is an AI-assisted document _comprehension_ tool.** It is **not** certified
> legal translation, and it does **not** provide legal advice. AI translations may contain
> inaccuracies — always consult a qualified professional for legal matters.

---

## Table of Contents

- [The Problem](#the-problem)
- [Who It's For](#who-its-for)
- [Features](#features)
- [Architecture](#architecture)
- [Tech Stack](#tech-stack)
- [Project Structure](#project-structure)
- [Prerequisites](#prerequisites)
- [Getting Started](#getting-started)
- [Configuration](#configuration)
- [API Reference](#api-reference)
- [Development](#development)
- [Roadmap](#roadmap)
- [Cost Model](#cost-model)
- [Design Principles](#design-principles)
- [Contributing](#contributing)

---

## The Problem

People routinely sign documents in languages they don't fully understand — employment
contracts, leases, banking forms, visa paperwork. The current "process" is screenshots into
translators, asking friends, or simply blind-signing. There's no legal context, no risk
awareness, and no bilingual traceability.

LinguaSign turns a foreign-language document into something you can **read, understand, and
sign with confidence** — while always preserving the original wording and a verifiable trail
of what was translated and signed.

## Who It's For

- **Expats** (e.g. English speakers in Korea) receiving contracts, leases, and visa documents.
- **Multilingual citizens** (e.g. South Africa: Afrikaans, isiZulu, isiXhosa, Sesotho) who want
  documents in their preferred language.
- **Cross-border onboarding** — international employment contracts, NDAs, relocation paperwork.

## Features

**Available:**
- Authenticated app shell (Supabase) + modular backend (Phase 0).
- 📄 **Document upload & OCR** — PDF upload, background OCR via the Surya sidecar, and a
  viewer that overlays extracted text blocks with bounding boxes on the original PDF (Phase 1).
- 🌐 **Bilingual viewer** — clause-by-clause translation (local LLM via Ollama, glossary-injected)
  with original + translation in synchronized panes and hover-linked clauses (Phase 2).
- ✍️ **Signing, audit & export** — in-app electronic signature stamped onto the PDF with
  evidentiary metadata (hash, timestamp, IP), an append-only audit trail, and an export of the
  signed PDF + audit package ZIP (Phase 3).
- 🚩 **Risk detection & explanations** — hybrid (deterministic rules + LLM) clause risk
  classification (auto-renewal, penalty, arbitration, non-compete, etc.) with plain-language
  explanations, a risk overlay on the viewer, and a high-risk summary (Phase 4).

**Planned / post-validation:**
- 🔐 Documenso / AES-QES signing, certified human-review translation tier, voice explanations,
  contract version comparison.

## Architecture

A **modular monolith** backend (.NET 10) and a **Next.js** frontend, with **Supabase**
providing authentication, Postgres, and storage. Asynchronous document processing runs through
an in-process job queue (Hangfire, added in Phase 1). A Python ML sidecar (OCR) and Documenso
(signing) are introduced in later phases.

```
                 ┌─────────────────────────┐
                 │   Next.js frontend       │
                 │   (Supabase auth, PDF UI)│
                 └────────────┬────────────┘
                              │ HTTPS + JWT
                 ┌────────────▼────────────┐
                 │   .NET 10 API (host)     │
                 │   composition root       │
                 │  ┌────────────────────┐  │
                 │  │ Documents          │  │
                 │  │ Translation        │  │   modular monolith:
                 │  │ Signing            │  │   one deployable,
                 │  │ Analysis           │  │   clear module boundaries
                 │  │ Audit              │  │
                 │  │ Export             │  │
                 │  └────────────────────┘  │
                 └───┬─────────┬─────────┬──┘
                     │         │         │
        ┌────────────▼──┐ ┌────▼─────┐ ┌─▼──────────────┐
        │ Supabase      │ │ Python   │ │ Documenso      │
        │ auth/db/store │ │ ML (OCR) │ │ (e-signatures) │
        └───────────────┘ └────┬─────┘ └────────────────┘
                               │
                     ┌─────────▼──────────┐
                     │ LLM (Ollama local  │
                     │ or cloud API)      │
                     └────────────────────┘
```

**Why a modular monolith (not microservices)?** For a small team and validation-stage product,
microservices add operational cost (service discovery, inter-service auth, distributed tracing,
multiple deployments) without buying anything we need yet. Modules give us clean boundaries and
the option to split later — without paying the distributed-systems tax up front.

## Tech Stack

| Layer | Choice | Notes |
|-------|--------|-------|
| Frontend | Next.js 16, React 19, TypeScript, Tailwind | App Router, `src/` dir |
| Backend | ASP.NET Core on .NET 10 | Modular monolith, minimal APIs |
| Auth | Supabase Auth (JWT) | Backend validates Supabase-issued JWTs |
| Database | Supabase Postgres | Row-level security for multi-tenancy |
| Storage | Supabase Storage | Original PDFs, renders, signature assets |
| Async jobs | Hangfire (Phase 1) | Postgres-backed; OCR → translate → analyze pipeline |
| OCR | Surya / PaddleOCR (self-host) or Azure Document Intelligence | Python ML sidecar |
| Translation / Analysis | Ollama (local) or cloud LLM | Clause-by-clause, glossary-injected |
| Signing | In-app electronic signature (MVP) | PdfSharp stamp + audit trail; Documenso/AES is a post-validation upgrade |
| PDF stamping | PdfSharp | Avoids iText (AGPL) |

## Project Structure

```
.
├── README.md
├── docker-compose.yml              # local Postgres (+ optional OCR sidecar)
├── frontend/                       # Next.js app
│   └── src/
│       ├── app/                    # routes: / , /dashboard , /documents/[id]
│       ├── components/             # AuthForm, UploadDropzone, DocumentList, PdfBlockViewer
│       └── lib/                    # api client, types, useUser, supabase clients
├── ml/
│   └── ocr-sidecar/                # FastAPI + Surya OCR service (main.py, Dockerfile)
└── backend/
    ├── LinguaSign.slnx             # .NET 10 solution (new .slnx format)
    └── src/
        ├── LinguaSign.Api/         # Host: composition root, JWT auth, endpoints, Hangfire
        ├── LinguaSign.Shared/      # Shared kernel — cross-cutting primitives & contracts
        └── Modules/
            ├── LinguaSign.Documents/    # Upload, storage, OCR, EF Core      (Phase 1) ✅
            ├── LinguaSign.Translation/  # Segmentation, glossary, translate  (Phase 2) ✅
            ├── LinguaSign.Signing/      # E-signature + PDF stamp (PdfSharp)  (Phase 3) ✅
            ├── LinguaSign.Analysis/     # Risk detection + explanations      (Phase 4) ✅
            ├── LinguaSign.Audit/        # Append-only audit trail            (Phase 3) ✅
            └── LinguaSign.Export/       # Signed PDF + audit package ZIP     (Phase 3) ✅
```

Each module exposes an `Add<Module>Module()` extension that registers its own services with DI,
keeping module internals encapsulated behind a single composition seam in `Program.cs`.

## Prerequisites

| Tool | Version | Required for |
|------|---------|--------------|
| .NET SDK | 10.x | Backend |
| Node.js | 22+ | Frontend |
| npm | 11+ | Frontend |
| Supabase project | free tier | Auth, DB, storage |
| Docker (Rancher Desktop) | — | OCR sidecar & Documenso (Phase 1+) |
| Ollama | — | Local LLM translation (optional, Phase 2) |

> **macOS note:** if `dotnet` isn't found, the SDK is at `/usr/local/share/dotnet` — add it to
> your PATH (`export PATH="/usr/local/share/dotnet:$PATH"`).

## Getting Started

### 1. Create a Supabase project

Sign up at [supabase.com](https://supabase.com), create a project, then grab the **Project URL**
and **anon public key** from **Project Settings → API**.

### 2. Start local dependencies

```bash
docker compose up -d            # Postgres on :5432
```

### 3. OCR sidecar

```bash
cd ml/ocr-sidecar
python -m venv .venv && source .venv/bin/activate
pip install -r requirements.txt
uvicorn main:app --host 0.0.0.0 --port 8000   # first run downloads Surya weights
```

### 4. Backend

```bash
cd backend

# Store secrets outside source control (preferred):
dotnet user-secrets init --project src/LinguaSign.Api
dotnet user-secrets set "Supabase:Url" "https://<your-project>.supabase.co" --project src/LinguaSign.Api

dotnet run --project src/LinguaSign.Api   # applies EF migrations on startup (dev)
```

Verify it's up:

```bash
curl http://localhost:5080/health
# → {"status":"ok","service":"LinguaSign.Api"}
```

The default `ConnectionStrings:Postgres` and `Ocr:BaseUrl` already point at the compose
Postgres and the local sidecar. The Hangfire dashboard is at `/hangfire` (dev).

### 5. Frontend

```bash
cd frontend
cp .env.example .env.local      # fill in your Supabase URL + anon key
npm install
npm run dev                     # http://localhost:3000  → open /dashboard
```

**End-to-end:** sign in on `/dashboard`, upload a PDF, watch its status move
`Uploaded → Processing → Extracted`, then open it to see extracted blocks overlaid on the PDF.

## Configuration

### Backend (`appsettings.json`, user-secrets, or environment variables)

| Key | Description |
|-----|-------------|
| `Supabase:Url` | Your Supabase project URL (used to derive JWT issuer/JWKS) |
| `Supabase:JwtSecret` | Only needed for legacy HS256 projects |

Environment variable form uses `__` as the separator, e.g. `Supabase__Url`.

### Frontend (`.env.local`)

| Variable | Description |
|----------|-------------|
| `NEXT_PUBLIC_SUPABASE_URL` | Supabase project URL |
| `NEXT_PUBLIC_SUPABASE_ANON_KEY` | Supabase anon public key |
| `NEXT_PUBLIC_API_BASE_URL` | LinguaSign backend base URL (default `http://localhost:5080`) |

## API Reference

| Method | Route | Auth | Description |
|--------|-------|------|-------------|
| `GET` | `/health` | none | Liveness probe |
| `GET` | `/me` | required | Returns the authenticated user's `sub` and `email` claims |
| `POST` | `/api/documents` | required | Upload a PDF (multipart, field `file`); enqueues OCR |
| `GET` | `/api/documents` | required | List the caller's documents |
| `GET` | `/api/documents/{id}` | required | Document detail incl. pages + extracted blocks |
| `GET` | `/api/documents/{id}/file` | required | Download the original PDF |
| `GET` | `/openapi/v1.json` | none (dev) | OpenAPI document |

All `/api/documents` routes are scoped to the authenticated user.

## Development

```bash
# Backend
cd backend
dotnet build LinguaSign.slnx      # build all projects
dotnet run --project src/LinguaSign.Api

# Frontend
cd frontend
npm run dev                       # dev server
npm run build                     # production build
npm run lint                      # lint
```

### EF Core migrations

```bash
cd backend
dotnet ef migrations add <Name> \
  --project src/Modules/LinguaSign.Documents \
  --startup-project src/Modules/LinguaSign.Documents
```

Migrations apply automatically on backend startup in development. The design-time
connection can be overridden with the `LINGUASIGN_DB` environment variable.

### Tests

```bash
# Backend — xUnit unit + EF integration tests
cd backend
dotnet test                          # unit tests run offline; integration tests
                                     # need local Postgres (docker compose up -d)

# Frontend — Playwright
cd frontend
npx playwright test upload.spec.ts   # full happy-path pipeline (needs all services up)
npx playwright test negative.spec.ts # fast negative/edge cases
```

**Backend tests** (`backend/tests/LinguaSign.Tests`): unit tests cover the deterministic
risk rules, the legal glossary, and the LLM JSON parsers (incl. malformed / smaller-model
shapes); integration tests spin up an ephemeral Postgres database and exercise the
document, translation, audit, analysis, and processing services with mocked OCR/LLM —
including ownership checks and the hybrid rules-override-LLM risk merge.

**Frontend tests** (`frontend/e2e`): `upload.spec.ts` drives the whole pipeline end to end;
`negative.spec.ts` covers auth gating, wrong credentials, non-PDF rejection, and theme
persistence.

## Roadmap

| Phase | Focus | Status |
|-------|-------|--------|
| 0 | Scaffold (auth, modular backend, app shell) | ✅ Done |
| 1 | Upload → OCR → render extracted blocks | ✅ Done |
| 2 | Bilingual viewer (translation + synced panes) — **validation milestone** | ✅ Done |
| 3 | Signing + audit trail + export | ✅ Done |
| 4 | Risk detection + clause explanations | ✅ Done |

All four MVP phases are complete and verified end-to-end (Playwright).

**Future:** Documenso/AES-QES signing, human-review/certified-translation tier, voice explanations, contract version comparison.

## Cost Model

Designed to validate cheaply. During the validation phase (low volume), expect roughly:

- **Fixed:** ~$0–135/mo depending on whether you self-host (Rancher/Ollama on your own machine) or
  use managed cloud. With local OCR + local LLM via Ollama, fixed cost is essentially electricity.
- **Per document:** ~$0.30–0.85 if using cloud OCR + cloud LLM; **~$0** with the local Ollama path.

A one-time GPU investment becomes worthwhile only after volume justifies it — deliberately deferred.

## Design Principles

1. **Always preserve the original.** Original wording, clause linkage, and translation
   traceability are never lost. The original legal document is never blindly replaced.
2. **Trust through traceability.** Every action (upload, translate, sign, export) is logged with
   hashes, timestamps, and the model/version used.
3. **Honest positioning.** Never claim legal equivalence, certified translation, or legal advice.
4. **Comprehension is the moat** — not signatures, PDFs, or OCR. Build depth there.

## Contributing

- Commits are plain and descriptive — .
- Keep module boundaries intact: cross-module access goes through public contracts, not internals.
- Secrets never get committed (`.env`, `appsettings.Development.json`, and user-secrets are git-ignored).

---

_License: TBD._
