# CFC Chat AI

CFC Chat AI answers questions about CFC Technologies’ animal-feed software using retrieved documentation. The **Blazor Interactive Server** UI calls the **ASP.NET Core API** on the server. The API uses Supabase for authentication, profiles, chat history, feedback, and file storage; it embeds text locally with ONNX, searches Pinecone, and optionally uses Azure OpenAI to generate answers with citations. Python and React are [archived](.archive/README.md), not required to run either .NET host.

**Deployment status:** This repository has a .NET-only deployment path, not approval to switch live traffic. Before cutover, complete the [API cutover](docs/plan/internal-api-cutover.md) and [IIS/ARR acceptance](docs/plan/routing-and-hosting.md) checks against the intended host and providers. Video upload and some legacy endpoints are intentionally unavailable. Profile writes and account lifecycle operations default off.

## Repository map

| Path | Purpose |
| --- | --- |
| [`CFC.ChatAI.slnx`](CFC.ChatAI.slnx) | Full .NET 10 solution |
| [`src/`](src/) | API and Blazor entry points, chat and feedback, document and video processing, Pinecone and Supabase adapters |
| [`tests/`](tests/) | .NET tests, JavaScript browser-bridge tests, and optional offline document-upload parity harness |
| [`tools/`](tools/) | Architecture checks and retrieval/embedding diagnostics |
| [`deployment/`](deployment/) | Windows publish script, IIS route allowlist, and deployment/rollback checklist |
| [`docs/`](docs/) | Configuration guides, API and frontend notes, and migration records |
| [`.archive/`](.archive/) | Legacy application, superseded deployment/setup guides, and rollback references |

`models/` (local model assets), `data/` (local app state), `.env`, and build output are ignored. Use [the route inventory](docs/plan/routing-and-hosting-routes.md) for the API contract and edge routing; the .NET API does not serve the old Python Swagger UI.

## Local development

1. Install the **.NET 10 SDK**. From the repository root, optionally copy [`.env.example`](.env.example) to `.env` (`Copy-Item .env.example .env` in PowerShell, `cp .env.example .env` from a POSIX shell). Do not overwrite an existing `.env`. Leave provider settings blank for liveness-only testing; fill them in for sign-in, chat, or uploads.
2. Build the solution and start the API:

   ```sh
   dotnet build CFC.ChatAI.slnx
   dotnet run --project src/CFC.ChatAI.Api
   ```

3. In a second terminal, start Blazor and open <http://localhost:5077/>. `/` and the legacy `/ui/` bookmark redirect to `/login`.

   ```sh
   dotnet run --project src/CFC.ChatAI.Web --launch-profile http
   ```

The API loads the root `.env` on startup without overwriting environment variables. Blazor does not load that file and does not need provider secrets. It connects to `http://127.0.0.1:5000` by default; if the API listener changes (`DOTNET_API_PORT` or `Api__Urls`), set `Backend__BaseUrl` in the Blazor process environment to the matching loopback HTTP or HTTPS base URL. No Node build, Python service, or CORS change is needed. Stop either host with Ctrl+C.

Check <http://127.0.0.1:5000/api/health> for liveness without credentials. `/api/health/detailed` is for loopback diagnostics: inspect the `ok` field and dependency statuses, not just HTTP 200. Its Azure OpenAI check does not make a live completion. A credential-free Blazor host can render the login page but cannot authenticate.

## Configuration and feature availability

Use isolated development resources for testing, especially for writes. Get the correct project, index, bucket and key assignments from the project owner. Keep `.env` and provider secrets out of Git, the browser, and logs. Never log user tokens. In production, provide API secrets from a restricted service environment or a private `.env`, never from the IIS site directory.

| Settings | What they enable |
| --- | --- |
| `SUPABASE_URL`, `SUPABASE_ANON_KEY`, `SUPABASE_SERVICE_ROLE_KEY` | Set all three to map Supabase-backed routes: auth, profiles, sessions, invitations, admin reads and signed media. The anon key is client-facing; the service-role key bypasses RLS and is server-only. |
| `SUPABASE_DOCUMENTS_BUCKET`, `SUPABASE_VIDEOS_BUCKET` | Supabase Storage areas; defaults are `cfc-docs` and `cfc-videos`. The legacy Python bucket settings are different. |
| `PINECONE_API_KEY`, `PINECONE_INDEX_NAME`, `Embedding__ModelPath`, `Embedding__VocabularyPath` | Set all four in addition to Supabase for chat, feedback writes, vector stats and document mutations. `PINECONE_NAMESPACE` specifies the optional namespace. Use the matching 384-dimension model, vocabulary, and index. |
| `AZURE_OPENAI_ENDPOINT`, `AZURE_OPENAI_API_KEY`, `AZURE_OPENAI_DEPLOYMENT` | Set all three for generated answers, or leave all three empty for extractive fallback. |
| `PASSWORD_RESET_REDIRECT_URL` | Full Blazor `/reset-password` URL, allowed in Supabase; local default in the example is `http://localhost:5077/reset-password`. It replaces the old .NET use of `FRONTEND_BASE_URL`. See [email and signup setup](docs/EMAIL_SETUP.md). |
| `Document__MaxUploadBytes`, `Document__LibreOfficePath` | One multipart document per request, 50 MiB default with 1 MiB HTTP overhead. Legacy `.doc` needs the LibreOffice executable; PDF extraction requires a text layer (no OCR). Supported formats and receipts are in the [upload guide](docs/admin-upload.md). |
| `Api__AdminSettingsPath`, `Api__AccountLifecycleJournalDirectory` | Use private, persistent paths in deployed environments. The journal is required when lifecycle actions are enabled. |

Follow [embedding setup](docs/rag/embeddings.md) to obtain `model.onnx` and `vocab.txt`; use absolute paths in `.env`. If upgrading a checkout with paths under `dotnet/models/`, update them to the new `models/` location. Missing required configuration leaves the corresponding routes unmapped (404); nonempty placeholder values do not establish connectivity.

Leave `Api__EnableDevIngest` and `Api__EnableDevDelete` off on shared hosts. Those development-only routes must never be exposed through a proxy. Enable `Api__EnableProfileWrites` only after [profile grant/RLS and route verification](docs/plan/supabase-profile-self-service-security.md). `Api__EnableAccountLifecycle` and `Api__EnablePermanentAccountDeletion` have their own [enablement and reconciliation checks](docs/complete/internal-api/internal-api-remaining-endpoints-delivery.md). Verify superuser protections in the actual database; do not assume archived schema and policy snapshots describe production. Document writes are not transactional across Storage, Pinecone and database rows, so stop writes for the affected ID and reconcile those stores after a partial failure or lost response before retrying.

## Testing and CI

Run from the repository root:

```sh
dotnet test CFC.ChatAI.slnx -c Release
# Node.js 22+, no npm install needed
node --test tests/CFC.ChatAI.Web.Tests/frontend-browser.test.mjs tests/CFC.ChatAI.Web.Tests/supabase-auth.test.mjs
# Python 3.10+, standard library only
python tools/check_supabase_boundary.py
```

[CI](docs/CICD_PLAN.md) runs these checks and stages the Windows IIS release. Tests do not depend on live credentials. Embedding and private document parity tests skip when the corresponding assets are absent. The [optional upload parity harness](docs/tests/document-upload-parity-report.md#reproduction-and-verification) runs synthetic inputs against both implementations. Neither local tests nor CI validate live provider policies, browser sign-in against the target project, or native IIS behavior.

## Windows deployment and operations

Use the [.NET/IIS checklist](deployment/DOTNET-IIS.md) for one-time setup, staging, installation, smoke tests, updates, and rollback. The host needs IIS with WebSocket Protocol, the .NET 10 Hosting Bundle, URL Rewrite, ARR with proxying enabled, and NSSM for the loopback API. Publish both hosts from the same revision. IIS runs the published Blazor site at the HTTPS site root and proxies only approved API methods/paths; it does not fall back to Python. CI does not install or restart services on the VM.

- **Access:** Ask client IT or the project owner for the current site URL, VPN instructions if it is private, and the approved administrator access path. Connect to the approved network before opening the site. Do not expose RDP or the loopback API publicly. Use Supabase sign-in; an authorized admin adds an invitation for new users, then shares the site's URL. See [email setup](docs/EMAIL_SETUP.md) for registration and recovery. Transfer administrator access through the approved credential store, not a handover document or shared inbox password.
- **Update:** Stage a new release using `deployment/Publish-DotnetIis.ps1`. Pause writes and drain requests before switching IIS and the API to the matching release. Preserve the private `.env`, settings and journal, model assets, IIS bindings, rollback release, and Data Protection keys. After an API configuration change, restart `CFCChatApi`; do not redeploy or rebuild solely to change secrets.
- **Check and diagnose:** On the host, `Get-Service CFCChatApi` and `Invoke-RestMethod http://127.0.0.1:5000/api/health` check the service and listener. Check the same health route through the IIS hostname, then test the UI, sign-in and relevant workflows. Inspect the configured NSSM stdout/stderr logs and the Windows Application event log. If Blazor repeatedly reconnects, check WebSockets and `/_blazor` before changing provider settings. If sign-in or recovery fails, check `/api/auth/config` on the API and the Supabase redirect settings. If search returns no results, check the model, index, namespace and ingested data rather than treating liveness as readiness.
- **Domain or HTTPS change:** Coordinate DNS, certificate and firewall/NSG settings with client IT. Update the IIS bindings, Supabase Site URL and redirect allowlist, and `PASSWORD_RESET_REDIRECT_URL`, then verify sign-in and email links through the new origin. Keep the site on its approved network until cutover is authorized.
- **Rollback:** Restore the prior IIS rules, site, and matching API/backend together. Reconcile any interrupted provider writes first; rolling back the binaries does not undo provider mutations. Use the [rollback checklist](deployment/DOTNET-IIS.md#updates-and-rollback), not the archived Python deployment scripts.
