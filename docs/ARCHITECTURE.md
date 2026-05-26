# Architecture

LinguaSign is a **modular monolith** on the backend with a separate Next.js front end and two
small Python sidecars — one for OCR, one for translation. The whole thing is designed to run on
one machine for validation and to be lifted onto a server (or Kubernetes) without code changes —
the only things that move are connection strings and base URLs.

The deliberate non-goals are worth stating up front, because they shaped everything: no
microservices, no message broker, no cloud lock-in. A solo project doesn't have the problems
those solve, and they'd have cost more than they returned.

## System overview

```mermaid
flowchart TB
    user([User / mobile browser])

    subgraph front[Next.js front end]
        web[App Router pages<br/>landing · auth · dashboard · reader · sign]
    end

    subgraph api[.NET 10 API · modular monolith]
        documents[Documents]
        translation[Translation]
        analysis[Analysis]
        signing[Signing]
        audit[Audit]
        export[Export]
        hangfire[[Hangfire jobs]]
    end

    subgraph data[Stateful]
        pg[(PostgreSQL<br/>5 schemas)]
        storage[(Object storage<br/>local FS / Supabase)]
    end

    subgraph ml[Inference]
        ocr[Surya OCR sidecar<br/>FastAPI · Python · :8000]
        trans[MarianMT translation sidecar<br/>FastAPI · CTranslate2 INT8 · :8001]
        llm[Ollama<br/>qwen2.5 · :11434]
    end

    supa[Supabase Auth<br/>JWT / JWKS]

    user --> web
    web -- "HTTPS + Bearer JWT" --> api
    web -. "sign in" .-> supa
    api -- validates JWT --> supa
    documents --> pg
    translation --> pg
    analysis --> pg
    signing --> pg
    audit --> pg
    documents --> storage
    hangfire --> ocr
    translation --> trans
    analysis --> llm
```

Each backend module owns its own EF Core `DbContext` and Postgres schema
(`documents`, `translation`, `signing`, `audit`, `analysis`). Modules talk to each other through
public service interfaces (`IDocumentService`, `ITranslationService`, `IAuditService`), never by
reaching into another module's tables. That keeps the seams honest, so if one module ever needs
to become its own service the cut is already drawn.

The front end never holds secrets beyond the public Supabase anon key. It signs in against
Supabase directly, then sends the resulting JWT to the .NET API as a bearer token; the API
validates it against Supabase's JWKS.

## How a document is processed

Uploading kicks off a short, linear pipeline. It's driven by Hangfire background jobs and the
front end polls for status — there's no websocket, just `GET` polling, which is plenty for a
per-document flow.

```mermaid
sequenceDiagram
    participant U as Browser
    participant A as .NET API
    participant H as Hangfire
    participant O as OCR sidecar
    participant T as Translation sidecar
    participant L as Ollama
    participant DB as Postgres

    U->>A: POST /api/documents (PDF)
    A->>DB: Document(Uploaded)
    A-->>H: enqueue OCR
    A-->>U: 201 { id }

    H->>O: POST /ocr (PDF)
    O-->>H: pages + blocks (text, bbox)
    H->>DB: pages/blocks, Document(Extracted)

    U->>A: POST /{id}/translate
    A-->>H: enqueue translate
    loop per page
        H->>T: POST /translate (blocks + source lang)
        T-->>H: translations (GUID-keyed JSON, glossary applied)
        H->>DB: segments (committed per page)
    end

    U->>A: POST /{id}/analyze
    A-->>H: enqueue analyze
    H->>DB: read English segments
    Note over H: deterministic rules + LLM, take higher risk
    H->>L: chat/completions (risk + explanation)
    H->>DB: clause findings

    U->>A: POST /{id}/sign
    A->>DB: Signature (+ hashes), Audit(Signed)
    U->>A: GET /{id}/export
    A-->>U: ZIP (signed PDF + metadata + audit)
```

Three things in here are load-bearing:

- **Translation uses a purpose-built MT model, not a general LLM.** MarianMT
  (`Helsinki-NLP/opus-mt-ko-en`) via CTranslate2 INT8 is 78 MB, uses ~500 MB RAM, and starts in
  2 seconds. This frees the rest of system memory for OCR and risk analysis and makes the whole
  stack viable on a 16 GB machine.
- **Translation runs one page at a time, never the whole document in one call.** Page-level
  batching lets segments stream into the reader as each page finishes, so the first page is
  readable in seconds.
- **Risk detection is hybrid.** Deterministic keyword rules run alongside the LLM and the higher
  risk wins. For a tool people use before signing, a missed high-risk clause is the worst
  outcome, so the rules act as a floor the model can't undershoot.

## Data model

```mermaid
erDiagram
    documents ||--o{ document_pages : has
    document_pages ||--o{ text_blocks : has
    document_translations ||--o{ translation_segments : has
    document_analyses ||--o{ clause_findings : has

    documents {
        guid Id PK
        string UserId
        string FileName
        string ContentHash
        string Status
        string SourceLanguage
    }
    text_blocks {
        guid Id PK
        string Text
        double X_Y_W_H
    }
    translation_segments {
        guid Id PK
        guid SourceBlockId "→ text_blocks (loose)"
        string SourceText
        string TranslatedText
    }
    clause_findings {
        guid Id PK
        guid SourceBlockId "→ text_blocks (loose)"
        string RiskLevel
        string RiskType
        string Explanation
    }
    signatures {
        guid Id PK
        guid DocumentId
        string SignerName
        string OriginalHash
        string SignedHash
    }
    audit_events {
        guid Id PK
        guid DocumentId
        string EventType
        string DocumentHash
    }
```

The `SourceBlockId` columns are the spine of the whole product. A translation segment and a risk
finding both point back to the OCR text block they came from, which is what lets the reader draw
the "kinship" line between a Korean clause and its English translation, and tint the right block
when a risk is flagged. They're loose references (plain GUIDs, no cross-schema foreign key) on
purpose — that's the price of keeping each module's schema independent.

## Tech choices, briefly

| Area | Choice | Why |
|------|--------|-----|
| Backend | .NET 10, modular monolith | One deployable, clean module seams |
| Async | Hangfire (Postgres-backed) | The pipeline is a linear per-document job, not a stream |
| OCR | Surya (self-hosted) | Strong layout + Korean; runs on the same box |
| Translation | MarianMT via CTranslate2 INT8 | 78 MB model, ~500 MB RAM, 2 s startup; purpose-built for translation, not a general LLM. Legal-term glossary applied post-decode. `Translation:Engine=ollama` reverts to qwen2.5 |
| Risk analysis | Ollama (`qwen2.5:7b`) | Needs reasoning, not just translation; hybrid with deterministic keyword rules |
| Auth/DB/storage | Supabase | One provider for auth + Postgres + storage |
| PDF stamping | PdfSharp | MIT-licensed; avoids iText's AGPL |
| Front end | Next.js 16 / React 19 | App Router, server components where useful |

## Deployment topology

The same containers run on a laptop, a single VM, or Kubernetes. On k8s it looks like this:

```mermaid
flowchart TB
    ingress[[Ingress]]
    subgraph cluster[Kubernetes namespace: linguasign]
        websvc[web · Next.js<br/>Deployment + Service]
        apisvc[api · .NET<br/>Deployment + Service]
        ocrsvc[ocr · Surya<br/>Deployment + Service]
        transsvc[translation · MarianMT<br/>Deployment + Service + PVC]
        ollamasvc[ollama<br/>Deployment + Service + PVC]
        pgsvc[(postgres<br/>StatefulSet + PVC)]
        secret[/Secret: Supabase + DB/]
    end
    ingress --> websvc
    ingress --> apisvc
    apisvc --> pgsvc
    apisvc --> ocrsvc
    apisvc --> transsvc
    apisvc --> ollamasvc
    apisvc -. reads .-> secret
```

Manifests and step-by-step guides for local, cloud, and Kubernetes are in
[DEPLOY.md](DEPLOY.md).
