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

**Available (Phase 0):**
- Project scaffold: authenticated app shell (Supabase) + modular backend.

**Planned (by phase):**
- 📄 **Document upload & OCR** — text, layout, and coordinate extraction (Phase 1).
- 🌐 **Bilingual viewer** — original and translation side-by-side, synchronized scrolling, clause
  linking, and click-to-explain (Phase 2).
- ✍️ **Digital signing** — legally-meaningful e-signatures via Documenso, with full metadata (Phase 3).
- 🧾 **Audit trail & export** — append-only logs, signed PDF, bilingual review copy, audit package (Phase 3).
- 🚩 **Risk detection & explanations** — flags auto-renewals, penalties, arbitration, etc., in plain language (Phase 4).

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
| Signing | Documenso (self-hosted) | AES-level e-signatures + audit |
| PDF generation | PdfSharp / QuestPDF | Avoids iText (AGPL) |

## Project Structure

```
.
├── README.md
├── .gitignore
├── frontend/                       # Next.js app
│   ├── .env.example
│   └── src/
│       ├── app/                    # App Router pages
│       └── lib/supabase/           # Browser + server Supabase clients
│           ├── client.ts
│           └── server.ts
└── backend/
    ├── LinguaSign.slnx             # .NET 10 solution (new .slnx format)
    └── src/
        ├── LinguaSign.Api/         # Host: composition root, JWT auth, endpoints
        ├── LinguaSign.Shared/      # Shared kernel — cross-cutting primitives & contracts
        └── Modules/
            ├── LinguaSign.Documents/    # Upload, metadata, storage          (Phase 1)
            ├── LinguaSign.Translation/  # Segmentation, glossary, translate  (Phase 2)
            ├── LinguaSign.Signing/      # Documenso integration              (Phase 3)
            ├── LinguaSign.Analysis/     # Risk detection + explanations      (Phase 4)
            ├── LinguaSign.Audit/        # Append-only audit trail            (Phase 3)
            └── LinguaSign.Export/       # Signed PDF / bilingual / audit zip (Phase 3)
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

### 2. Backend

```bash
cd backend

# Store secrets outside source control (preferred):
dotnet user-secrets init --project src/LinguaSign.Api
dotnet user-secrets set "Supabase:Url" "https://<your-project>.supabase.co" --project src/LinguaSign.Api

dotnet run --project src/LinguaSign.Api
```

Verify it's up:

```bash
curl http://localhost:5080/health
# → {"status":"ok","service":"LinguaSign.Api"}
```

### 3. Frontend

```bash
cd frontend
cp .env.example .env.local      # fill in your Supabase URL + anon key
npm install
npm run dev                     # http://localhost:3000
```

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
| `GET` | `/openapi/v1.json` | none (dev) | OpenAPI document |

More endpoints are added per phase (document upload in Phase 1, etc.).

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

## Roadmap

| Phase | Focus | Status |
|-------|-------|--------|
| 0 | Scaffold (auth, modular backend, app shell) | ✅ Done |
| 1 | Upload → OCR → render extracted blocks | ⏭ Next |
| 2 | Bilingual viewer (translation + synced panes) — **validation milestone** | |
| 3 | Signing (Documenso) + audit trail + export | |
| 4 | Risk detection + clause explanations | |

**Future:** human-review/certified-translation tier, voice explanations, contract version comparison.

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
