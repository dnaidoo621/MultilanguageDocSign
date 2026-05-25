# Deploying LinguaSign

Three paths, smallest to largest: a laptop for development, a single VM for a hosted demo, and
Kubernetes for something closer to production. The application code is identical in all three —
what changes is where Postgres, the OCR sidecar, and Ollama live, and a handful of connection
strings.

A note that runs through all of this: **the LLM is the bottleneck, not the app.** On a
16 GB machine, OCR (Surya) and the model (Ollama) fight over memory and translation crawls.
Anywhere you can give Ollama a real GPU — a cloud GPU node you control, a workstation — the whole
thing gets several times faster *without touching the code*, just by pointing `Llm__BaseUrl` at
that host. That keeps documents on infrastructure you own, which is the point.

---

## 1. Local (development)

What you need: .NET 10 SDK, Node 22+, Docker (for Postgres), Python 3.12 (for the OCR sidecar),
Ollama, and a Supabase project for auth.

```bash
# Postgres
docker compose up -d

# OCR sidecar (first run downloads Surya weights)
cd ml/ocr-sidecar
python3.12 -m venv .venv && source .venv/bin/activate
pip install -r requirements.txt
uvicorn main:app --port 8000

# Model
ollama pull qwen2.5:7b      # accurate; qwen2.5:3b is faster but lower quality

# Backend  (stores Supabase config in user-secrets, never in git)
cd backend
dotnet user-secrets set "Supabase:Url" "https://YOUR-PROJECT.supabase.co" --project src/LinguaSign.Api
dotnet run --project src/LinguaSign.Api --launch-profile http   # http://localhost:5080

# Front end
cd frontend
cp .env.example .env.local   # fill NEXT_PUBLIC_SUPABASE_URL / ANON_KEY
npm install && npm run dev    # http://localhost:3000
```

Migrations apply automatically on API startup. In Supabase, turn **off** email confirmation
during development so sign-up logs you straight in.

---

## 2. Single VM (hosted demo)

Good enough for putting the app in front of people. One box runs everything via containers.

1. Provision a VM. For usable translation speed, pick one with a **GPU** (e.g. a 24 GB card) — a
   7B model there runs at 100+ tok/s versus ~20 on a CPU/16 GB laptop.
2. Build and run the three images plus Postgres and Ollama:

```bash
docker build -t linguasign/api  backend/
docker build -t linguasign/ocr  ml/ocr-sidecar/
docker build -t linguasign/web  frontend/ \
  --build-arg NEXT_PUBLIC_SUPABASE_URL=https://YOUR-PROJECT.supabase.co \
  --build-arg NEXT_PUBLIC_SUPABASE_ANON_KEY=YOUR_ANON_KEY \
  --build-arg NEXT_PUBLIC_API_BASE_URL=https://api.yourdomain.com
```

3. Run them (Postgres + Ollama as containers, or managed Postgres + a GPU Ollama host). Wire the
   API with environment variables: `ConnectionStrings__Postgres`, `Supabase__Url`,
   `Ocr__BaseUrl`, `Llm__BaseUrl`, `Cors__Origins` (your front-end origin), and a persistent
   `Storage__LocalRoot`.
4. Put a reverse proxy (Caddy/Nginx) with TLS in front of the web (`:3000`) and API (`:8080`).
5. `ollama pull qwen2.5:7b` on the Ollama host.

**Privacy posture:** as long as Ollama and the OCR sidecar are on infrastructure you control,
no document content leaves your servers. Supabase only ever sees auth + the stored PDFs (under
its row-level security), never the model prompts.

---

## 3. Kubernetes

Manifests live in [`deploy/k8s/`](../deploy/k8s). They define a `linguasign` namespace, a
Postgres StatefulSet, Ollama and OCR deployments, the API and web deployments, and an Ingress.

```bash
# 1. Build + push images to your registry, then update image: refs in the manifests.
docker build -t REGISTRY/linguasign-api backend/   && docker push REGISTRY/linguasign-api
docker build -t REGISTRY/linguasign-ocr ml/ocr-sidecar/ && docker push REGISTRY/linguasign-ocr
docker build -t REGISTRY/linguasign-web frontend/  \
  --build-arg NEXT_PUBLIC_SUPABASE_URL=... \
  --build-arg NEXT_PUBLIC_SUPABASE_ANON_KEY=... \
  --build-arg NEXT_PUBLIC_API_BASE_URL=https://api.yourdomain.com
docker push REGISTRY/linguasign-web

# 2. Edit 00-namespace-secrets.yaml (DB password, Supabase URL) and the host names in 60-ingress.yaml.

# 3. Apply in order.
kubectl apply -f deploy/k8s/

# 4. Pull the model into the Ollama pod (one-time; persists on its PVC).
kubectl -n linguasign exec deploy/ollama -- ollama pull qwen2.5:7b
```

Things worth knowing:

- **Migrations** run on API startup, so the manifest pins `api` to a single replica. To scale the
  API out, move migrations into a one-shot `Job` and drop the on-startup call.
- **Ollama wants a GPU.** The manifest requests CPU/memory only; on a GPU node add an
  `nvidia.com/gpu` limit and use the CUDA image. Without a GPU it works but is slow.
- **Storage.** The API uses a `ReadWriteOnce` PVC for uploaded/signed PDFs, which matches the
  single replica. For multi-replica or HA, swap `LocalFileDocumentStorage` for a Supabase
  Storage / S3 implementation (the `IDocumentStorage` seam already exists for exactly this).
- **Secrets** belong in a real secret manager (Sealed Secrets, External Secrets, your cloud's
  KMS) — `00-namespace-secrets.yaml` is a template, not something to commit filled in.

---

## Configuration reference

| Key (env uses `__`) | Example | Notes |
|---------------------|---------|-------|
| `ConnectionStrings__Postgres` | `Host=postgres;...` | EF Core + Hangfire |
| `Supabase__Url` | `https://x.supabase.co` | Derives the JWKS / issuer |
| `Supabase__JwtSecret` | *(empty)* | Only for legacy HS256 projects |
| `Ocr__BaseUrl` | `http://ocr:8000` | Surya sidecar |
| `Llm__BaseUrl` | `http://ollama:11434/v1` | OpenAI-compatible endpoint |
| `Llm__Model` | `qwen2.5:7b` | `qwen2.5:3b` = faster, lower quality |
| `Storage__LocalRoot` | `/data/storage` | Document blob storage path |
| `Cors__Origins` | `https://app.yourdomain.com` | Front-end origin(s), comma-separated |
| `NEXT_PUBLIC_API_BASE_URL` | `https://api.yourdomain.com` | Baked into the web image at build |
