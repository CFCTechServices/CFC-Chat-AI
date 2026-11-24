# CFC Chat AI

CFC Chat AI is a documentation assistant for CFC Technologies' animal-feed software. It ingests documents and video transcripts, searches relevant content using embeddings and Pinecone, and answers questions using retrieved context and Azure OpenAI. The .NET UI uses Blazor Interactive Server and Supabase authentication.

This repository contains the **.NET Blazor app and API** and the legacy **Python/FastAPI and React application**. They are not interchangeable. Use the [.NET/IIS guide](deployment/DOTNET-IIS.md) for the Blazor deployment. `deploy-windows.ps1` remains a legacy Python deployment script; confirm the actual VM configuration before operating it.

> **Migration release gate:** Do not cut all traffic over to .NET yet. The in-scope internal API routes are implemented, but hosting/routing, live provider security and integration checks, and browser validation remain. Profile self-service writes are implemented behind a default-off flag; video upload is deferred. See the [cutover checklist](dotnet/docs/plan/internal-api-cutover.md), [routing and hosting plan](dotnet/docs/plan/routing-and-hosting.md), and [remaining security and integration results](dotnet/docs/complete/internal-api/supabase-completion-remaining-results.md).

## Repository map

| Path | Purpose |
| --- | --- |
| `main.py`, `app/` | Python API, authentication, ingestion, RAG and services |
| `web/` | Legacy React UI; `web/index.html` loads the JSX components and CDN dependencies |
| `dotnet/src/CFC.ChatAI.Web/` | Blazor frontend; server-side API client, authentication, chat and admin services |
| `dotnet/src/` | .NET API and ChatService, DocumentHandler, DocumentProcessor, VideoProcessor, Feedback, Pinecone and Supabase components |
| `tests/`, `dotnet/tests/` | Python and .NET tests |
| `data/` | Local documents, videos and processed data |
| `deployment/` | Blazor and .NET API IIS staging script, config and runbook; separate legacy Python config |
| `scripts/`, `dotnet/tools/` | Diagnostics, architecture checks and RAG test tools |

## Local development: Python application

Use Python 3.10 or 3.11; CI uses 3.10 and the Windows deployment script expects 3.11. Video transcription requires FFmpeg. Scanned PDFs may require Tesseract and Poppler; legacy `.doc` conversion requires Microsoft Word on Windows or LibreOffice. Install the relevant tools on PATH, or configure the binary paths described below.

From the repository root:

```bash
python -m venv .venv
# macOS/Linux:
source .venv/bin/activate
# Windows PowerShell instead: .\.venv\Scripts\Activate.ps1
python -m pip install -r requirements.txt
# Copy .env.example to .env and fill in the settings needed for your features
uvicorn main:app --reload
```

For the copy step use `cp .env.example .env` on macOS/Linux or `Copy-Item .env.example .env` in PowerShell. Do not overwrite an existing `.env` unintentionally.

- UI: <http://localhost:8000/> or <http://localhost:8000/ui/>
- FastAPI OpenAPI/Swagger: <http://localhost:8000/docs>
- Health: <http://localhost:8000/api/health>; dependency details: `/api/health/detailed`

The UI needs the configured Supabase project for its sign-in flows. Full search/chat requires the corresponding provider configuration and ingested content. Without Azure OpenAI configuration, the Python chat service can fall back to retrieved content rather than generating a synthesized answer; a running server alone does not prove the providers work. Python uses `sentence-transformers/all-MiniLM-L6-v2` (384 dimensions) for embeddings. Its vector store uses Pinecone; verify the intended index, namespace and data before testing search.

## Configuration and secrets

Start with [`.env.example`](.env.example); placeholders are not working credentials. Obtain project-specific access and values from the CFC project owner or IT team. Never commit `.env`, expose service-role keys to the browser, or place credentials in documentation or logs. The Supabase anon key is client-facing; the service-role key is privileged and bypasses RLS.

| Setting | Purpose |
| --- | --- |
| `SUPABASE_URL`, `SUPABASE_ANON_KEY`, `SUPABASE_SERVICE_ROLE_KEY` | Supabase Auth, database and backend operations; use the correct key for each role |
| `SUPABASE_BUCKET`, `SUPABASE_BUCKET_VIDEOS` | Python document and video storage buckets |
| `PINECONE_API_KEY`, `PINECONE_INDEX_NAME` | Python and .NET vector search; the index must match the embedding dimensions |
| `PINECONE_VIDEO_INDEX_NAME`, `PINECONE_NAMESPACE` | Python video index and namespace; inspect the .NET host configuration separately before assuming parity |
| `AZURE_OPENAI_ENDPOINT`, `AZURE_OPENAI_API_KEY`, `AZURE_OPENAI_DEPLOYMENT` | Azure OpenAI answer generation; configure all three together. `AZURE_OPENAI_API_VERSION` is used by Python |
| `FRONTEND_BASE_URL`, `CORS_ORIGINS` | Python base URL for email links and allowed browser origins; the Blazor API client does not require CORS |
| `PASSWORD_RESET_REDIRECT_URL` | .NET full `/reset-password` URL for Supabase recovery links; allow it in Supabase |
| `RESEND_API_KEY`, `ENABLE_EMAIL_INVITES` | Optional Python invitation email delivery |
| `SOFFICE_PATH`, `TESSERACT_CMD` | Python executable overrides when LibreOffice or Tesseract is not on PATH |

For Python, an unset `CORS_ORIGINS` falls back to `*`; explicitly restrict origins for deployment. The `.env.example` also lists `API_HOST` and `API_PORT`, but `app/config.py` currently hard-codes `0.0.0.0` and `8000`; choose Uvicorn's `--host` and `--port` explicitly rather than relying on those variables.

## Local development: .NET migration

Install the **.NET 10 SDK**. From the repository root, use the full solution explicitly (the root `CFC.ChatAI.slnx` contains only two test projects):

```bash
dotnet restore dotnet/CFC.ChatAI.slnx
dotnet build dotnet/CFC.ChatAI.slnx
dotnet test dotnet/CFC.ChatAI.slnx
dotnet run --project dotnet/src/CFC.ChatAI.Api/CFC.ChatAI.Api.csproj
```

In a second terminal, run `dotnet run --project dotnet/src/CFC.ChatAI.Web/CFC.ChatAI.Web.csproj --launch-profile http` and open <http://localhost:5077/>. Blazor calls the API server-to-server at `http://127.0.0.1:5000` by default; no Python, Node or CORS changes are required. The API loads the root `.env` without overwriting existing environment variables. If you change its port (`DOTNET_API_PORT` or `Api__Urls`), set `Backend__BaseUrl` in Blazor's environment to match. For local password reset, set `PASSWORD_RESET_REDIRECT_URL=http://localhost:5077/reset-password` in the root `.env` and allow the URL in Supabase. Existing environment variables take precedence, and the API must be restarted after changes. Existing .NET deployments must migrate their reset URL from `FRONTEND_BASE_URL`; Python continues to use that variable. See the [IIS guide's local steps](deployment/DOTNET-IIS.md#local-development).

- Health routes are mapped without provider credentials. `/api/health` checks the host, not end-to-end readiness. `/api/health/detailed` checks dependencies, but its Azure OpenAI check only checks configuration, not a live completion.
- Supabase-backed routes are mapped only when `SUPABASE_URL`, `SUPABASE_ANON_KEY` and `SUPABASE_SERVICE_ROLE_KEY` are all nonempty. Chat, feedback submission and vector stats additionally require `PINECONE_API_KEY`, `PINECONE_INDEX_NAME`, `Embedding:ModelPath` and `Embedding:VocabularyPath`. Missing configuration means routes are absent (404), not mock successes. Nonempty placeholder values do not prove connectivity.
- Set the two embedding paths to the local ONNX model and vocabulary. For environment variables use `Embedding__ModelPath` and `Embedding__VocabularyPath`; absolute paths avoid content-root ambiguity. The files are not committed. Download and test the matching `all-MiniLM-L6-v2` assets as described in [embedding setup](dotnet/docs/rag/embeddings.md); pin and verify model assets before deployment.
- The .NET host reads `SUPABASE_DOCUMENTS_BUCKET` and `SUPABASE_VIDEOS_BUCKET`, not the Python bucket variable names. Defaults are `cfc-docs` and `cfc-videos`. `PINECONE_NAMESPACE` selects the index namespace. Verify the development Supabase project, buckets, index, namespace and matching embedding model/vocabulary before writing data.
- Document upload is available when both providers are configured: `POST /api/files/upload` requires an active admin/superuser bearer token and one multipart `file` (.pdf, .docx, .doc, .txt, .md, .eml). `Document__MaxUploadBytes` defaults to 50 MiB; HTTP allows an additional 1 MiB multipart overhead. `.doc` requires LibreOffice (`Document__LibreOfficePath`, default `soffice`); PDF extraction uses embedded text, not OCR. Writes are not transactional; a failure can require reconciliation. Duplicate checks are not atomic and cannot prevent concurrent collisions.
- For local-only development, set `ASPNETCORE_ENVIRONMENT=Development` and `Api__EnableDevIngest=true`, bind only to loopback, then run `curl -F "file=@./unique-document.txt" http://127.0.0.1:5000/api/ingest/dev` (PowerShell: `curl.exe`). This unauthenticated endpoint requires a direct loopback connection, skips duplicate checks and must never be exposed or proxied. Reusing a normalized filename can mix chunks and overwrite originals; use a unique filename, not this route for replacement. Leave the flag off on shared, containerized, remote and production deployments.
- Document deletion: `DELETE /api/admin/documents/{docId}` requires an active admin/superuser. The separate unauthenticated `DELETE /api/ingest/dev/{docId}` requires Development, `Api__EnableDevDelete=true`, configured providers and a direct loopback connection; never proxy it. Verify the actual Supabase project, buckets and Pinecone index/namespace before deleting. These routes operate on configured providers, not just local files. Only known document metadata is supported; video/unknown types return 409, and missing metadata returns 404 without proving all stores are clean. No durable recovery or cross-instance lock exists: serialize writes manually and reconcile all stores after a failure, cancellation or lost response before retrying or reusing an ID. See [deletion PR notes](dotnet/docs/complete/document-deletion/document-deletion-changes.md).
- Profile writes (`PATCH /api/profile/me`, `POST /api/profile/complete`) map only with `Api__EnableProfileWrites=true`; leave it off until enabled-route tests and the [database security gate](dotnet/docs/plan/supabase-profile-self-service-security.md) pass. Account lifecycle actions also default off and need a private persistent journal; permanent deletion has a separate flag. See the [delivery notes](dotnet/docs/complete/internal-api/internal-api-remaining-endpoints-delivery.md) for their configuration and reconciliation.
- Azure OpenAI's endpoint, key and deployment must be configured together or all left empty for extractive fallback. The .NET API implements the in-scope internal API, not every Python endpoint; authentication and behavior differ in places.

## API and data flow

The Python service processes uploaded documents into chunks and embeddings; video uploads use Whisper and FFmpeg for transcription. Pinecone provides vector search, while content and metadata can be stored locally or in Supabase. Chat retrieves context, applies feedback-based ranking where configured, and returns answers with source information. Supabase also supports authentication, profiles, sessions, feedback and storage. The actual features available depend on configuration and the running backend.

Common Python endpoints (see `/docs` for request schemas and the complete exposed API):

| Area | Endpoints |
| --- | --- |
| Health | `GET /api/health`, `GET /api/health/detailed` |
| Auth/profile | `GET /api/auth/config`, `POST /api/auth/check-email`, `POST /api/auth/forgot-password`, `GET/PATCH /api/profile/me` |
| Chat | `POST /api/chat/message`, `POST /api/chat/search`, `POST /api/chat/ask`, `POST /api/chat/ask/video`, `POST /api/chat/recommendations` |
| History/feedback | `GET/POST /api/chat/sessions`, `GET/PATCH/DELETE /api/chat/sessions/{session_id}`, `GET/POST /api/chat/feedback` |
| Ingestion | `POST /api/files/upload`, `POST /api/videos/upload`; operator ingestion routes under `/api/ingest/` |
| Admin | `/api/admin/users`, `/api/admin/invite`, `/api/admin/documents`, `/api/admin/settings`, `/api/admin/ingestion/stats` |
| Vector stats | `GET /api/visibility/vector-store` |

The .NET host implements health, auth, profile read, sessions, feedback, chat message, vector stats, document and admin workflows, conditional on provider configuration. Upload, deletion, replacement and staged ingest require both providers; profile writes require an explicit default-off flag, and account lifecycle operations have separate enablement requirements. Video upload and some Python routes are out of scope. See the [delivery notes](dotnet/docs/complete/internal-api/internal-api-remaining-endpoints-delivery.md), [cutover checklist](dotnet/docs/plan/internal-api-cutover.md) and [frontend status](dotnet/docs/frontend-changes.md). Authentication and persistent sessions/feedback exist in the code; older handover statements describing them as entirely unimplemented are obsolete.

## Testing and CI

```bash
# Python, from the repository root; some integration tests require a live server and credentials
pytest tests/ -q

# Full .NET solution; some parity tests skip if model assets or reference documents are missing
dotnet test dotnet/CFC.ChatAI.slnx

# Supabase component dependency boundary
python dotnet/tools/check_supabase_boundary.py
```

Use a disposable non-production project for provider-backed tests and inspect test requirements before running them: some tests operate on users or other remote state. Local/unit tests do not validate the live database's grants, RLS, RPCs, storage policies or deployed services.

[CI](.github/workflows/ci.yml) checks the Supabase architecture boundary and runs the Python tests. A separate job runs the Blazor and .NET API tests, browser-bridge unit tests and Windows IIS staging. No workflow switches traffic or updates the VM.

## Windows deployment and operations

For a Python-free deployment, follow the [.NET/IIS deployment checklist](deployment/DOTNET-IIS.md). IIS hosts the self-contained Blazor app and proxies approved API routes to the loopback .NET service managed by NSSM. Validate IIS routing and browser flows on the target host before relying on it. The instructions below are for legacy Python only.

### One-time preparation

1. Install Python 3.11, Git, FFmpeg, and NSSM. Install Tesseract/Poppler for scanned PDFs and LibreOffice for `.doc` ingestion where needed. Put binaries on the service's PATH or set the supported paths in `.env`.
2. Enable IIS, install URL Rewrite and Application Request Routing, and enable ARR proxy. Provision the application and IIS web-root directories and appropriate service/file permissions. Arrange DNS, firewall/NSG rules and HTTPS according to the approved network design; do not expose RDP or HTTP to the internet by default.
3. Clone the repository, copy `.env.example` to `.env` and supply the actual secrets and URLs securely. Configure the Supabase Site URL and allowed redirect URLs to match the frontend; verify invitation and password-reset links.
4. Configure an IIS site at `C:\inetpub\cfcchat` with the necessary bindings and certificate. The script copies `web/` to its `ui/` directory and `deployment/legacy-python-iis-web.config` to its root.
5. **Resolve the split-health dependency before deployment:** the checked-in IIS config sends health GETs to port 5000. Ensure a separately managed .NET API is running there, or make an explicitly reviewed routing change. The Python deploy script alone cannot satisfy those routes. Verify both direct backends and IIS after any change.

### Deploy/update Python

In an appropriately privileged PowerShell session on the VM, after reviewing the intended revision and preserving `.env`:

```powershell
cd C:\cfcchat
git pull
.\deploy-windows.ps1
```

The script creates/updates `.venv`, installs dependencies, copies frontend files, installs or restarts the `CFC-ChatAI` NSSM service and probes Python at `127.0.0.1:8000`. It configures Uvicorn with two workers on that fixed address/port. On an existing service it attempts to stop it and kill listeners on port 8000; check ownership before running it on a shared host. A failed health probe is a warning, so independently verify the result. Changes to `.env` require restarting the Python process.

```powershell
nssm status CFC-ChatAI
Invoke-RestMethod http://127.0.0.1:8000/api/health  # Python directly
Invoke-RestMethod http://127.0.0.1:5000/api/health  # .NET directly, if running
# Also check /api/health and /ui/ through the actual IIS hostname and bindings.
nssm restart CFC-ChatAI
netstat -ano | findstr ":8000"
```

Consult the service's configured stdout/stderr destinations and Windows Application event log for failures. The checked-in script does **not** configure `C:\cfcchat\logs\stdout.log` or `stderr.log`; those paths only apply if an operator has configured them. Investigate process ownership before terminating any stale listener.

### Troubleshooting and maintenance

- **No results:** check the selected Pinecone index, dimensions, namespace, credentials and ingestion state; verify the matching backend configuration.
- **Document/video processing fails:** check service account permissions, disk space, supported file type and FFmpeg, Tesseract, Poppler or LibreOffice availability as appropriate. On Windows set `SOFFICE_PATH` or `TESSERACT_CMD` if needed, then restart the service.
- **Fallback answers or model errors:** inspect application logs and Azure OpenAI endpoint, key and deployment. If a model is retired, create an approved replacement deployment, update configuration and restart. Health configuration checks do not prove the model is usable.
- **Login, invitation or reset links fail:** verify Supabase project configuration, email delivery, the redirect setting (`FRONTEND_BASE_URL` for Python or `PASSWORD_RESET_REDIRECT_URL` for .NET) and the Supabase redirect allowlist. Confirm browser origin and CORS settings for the actual deployment.
- **IIS health fails while Python is healthy:** check whether .NET is listening on port 5000 and inspect the deployed IIS rewrite rules. Do not interpret the Python script's direct probe as a check of the IIS route.
- **Domain/HTTPS change:** configure DNS, firewall and an IIS certificate, update frontend origin and Supabase URL settings, restart affected services, and test from the intended network. In a private deployment, obtain the current VPN and site address from client IT.

## Security and release considerations

The migration's checked-in Supabase privilege exports indicate potentially unsafe profile column grants; they do not establish the live project's state. The database owner must verify the target project, grants, role inheritance, RLS, signup triggers and direct negative tests before enabling .NET self-profile writes. See [profile self-service security](dotnet/docs/plan/supabase-profile-self-service-security.md). Complete provider integration tests, frontend compatibility, deployment/monitoring, and the remaining cutover gates before production traffic is moved. Verify the superuser safeguards and database triggers in the actual target environment rather than assuming they exist.

## Documentation status

This README consolidates the root setup, deployment, runbook, access and handover guides plus `docs/OPERATIONS_CENTRAL.md` against the checked-in code and configuration. Those older files are retained for history and may contain stale or contradictory instructions; prefer this README and the current source. Migration-specific implementation plans and verification records under `dotnet/docs/` provide further detail, but are not proof of the current live deployment. Confirm production credentials, access and operational ownership with the CFC project owner or IT team.
