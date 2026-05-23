# LinguaSign

Understand and confidently sign multilingual documents. Upload a document, review a
synchronized translation with risk highlights, then sign — with a full audit trail.

> AI-assisted document **comprehension** tool. **Not** certified legal translation or legal advice.

## Status

**Phase 0 — Scaffold.** See the build plan in the project's Obsidian notes
(`Projects/LinguaSign/01-Build-Plan.md`). Strategy: validate cheaply, then decide on
commercialization.

## Architecture (validation phase)

A **modular monolith** backend (.NET 10) + a **Next.js** frontend, with **Supabase**
providing auth + Postgres + storage. Async work runs via Hangfire (added in Phase 1).
A Python ML sidecar (OCR) and Documenso (signing) join in later phases.

```
frontend/                Next.js (TS, Tailwind, App Router) + Supabase auth
backend/
  LinguaSign.slnx
  src/
    LinguaSign.Api/      ASP.NET Core host — composition root, JWT auth, endpoints
    LinguaSign.Shared/   Shared kernel — cross-cutting primitives & contracts
    Modules/
      LinguaSign.Documents/    Upload, metadata, storage (Phase 1)
      LinguaSign.Translation/  Segmentation, glossary, LLM translation, alignment (Phase 2)
      LinguaSign.Signing/      Documenso integration (Phase 3)
      LinguaSign.Analysis/     Risk detection + explanations (Phase 4)
      LinguaSign.Audit/        Append-only audit trail (Phase 3)
      LinguaSign.Export/       Signed PDF + bilingual copy + audit package (Phase 3)
```

## Prerequisites

- **.NET 10 SDK** (`/usr/local/share/dotnet` — ensure it's on PATH)
- **Node 22+** and npm
- A **Supabase** project (free tier) for auth/Postgres/storage
- *Later:* Docker (Rancher Desktop) for the OCR sidecar and Documenso; Ollama for local LLM translation

## Setup

### Backend

```bash
cd backend
# Supabase config via user-secrets (preferred) or appsettings:
dotnet user-secrets init --project src/LinguaSign.Api
dotnet user-secrets set "Supabase:Url" "https://<your-project>.supabase.co" --project src/LinguaSign.Api
dotnet run --project src/LinguaSign.Api
# Health check: GET /health
```

### Frontend

```bash
cd frontend
cp .env.example .env.local   # fill in NEXT_PUBLIC_SUPABASE_URL / ANON_KEY
npm install
npm run dev
```

## Roadmap

| Phase | Focus |
|-------|-------|
| 0 | Scaffold (this) |
| 1 | Upload → OCR → render extracted blocks |
| 2 | Bilingual viewer (translation + synced panes) — **validate here** |
| 3 | Signing (Documenso) + audit + export |
| 4 | Risk detection + clause explanations |
