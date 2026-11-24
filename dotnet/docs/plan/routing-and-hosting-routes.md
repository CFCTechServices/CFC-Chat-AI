# Python-free development route inventory

Inventory from the repo, not traffic observations. [Blazor](../../src/CFC.ChatAI.Web) calls the .NET API server-to-server, directly on loopback by default. For existing public API clients, IIS retains the method/path allowlist in [`deployment/iis-web.config`](../../../deployment/iis-web.config). No request falls back to Python in the .NET deployment.

In this table **S** means configured Supabase plus the relevant schema, grants and buckets; **P** means configured Pinecone plus the embedding model and vocabulary matching the index. Both are configuration gates, not proof of provider readiness. “Current user” means active account with a valid bearer token; “admin” requires admin/superuser and any operation-specific restrictions the API applies.

| Method and path | Observed caller | .NET auth / prerequisites | Target decision and contract |
| --- | --- | --- | --- |
| `GET /api/health` | Admin settings; deployer | None | Expose liveness only |
| `GET /api/auth/config` | Blazor session and upload capability check | S; public | Expose project URL, public key, backend kind and UI flags (including document upload availability), not service key |
| `POST /api/auth/check-email`, `POST /api/auth/forgot-password` | Sign-in, reset pages | S; public rate-limited | Expose; one shared edge quota; configure Supabase callback URL |
| `GET /api/profile/me` | Blazor session verification | S; current user | Expose read-only current profile |
| `PATCH /api/profile/me` | Settings | S; current user; profile opt-in | Expose when enabled; hide editing otherwise |
| `GET /api/chat/sessions` | Chat and history | S; current user | Expose summary by default and preview/details with `detail=true` |
| `POST /api/chat/sessions`, `GET/PATCH/DELETE /api/chat/sessions/{id}` | Chat | S; current user/owner | Expose session creation, history, title and deletion |
| `GET /api/chat/feedback`, `POST /api/chat/feedback`, `POST /api/chat/message` | Chat/history | S; current user; P for both POSTs; optional Azure OpenAI for generation | Expose; GET feedback uses `session_id` query; chat supports extractive fallback |
| `GET /api/chat/content/images/docs/{root}/images/{file}`, `GET /content/images/docs/{root}/images/{file}` | Chat renderer and legacy image clients | S; current user | Retain both aliases; .NET returns signed URL JSON. Blazor resolves supported legacy paths with the canonical API route |
| `GET/PATCH /api/admin/settings` | Admin settings | S; admin; persistent settings path | Expose supported settings; persist across releases |
| `GET /api/admin/users`, `POST /api/admin/invite`, `POST /api/admin/change-role` | Admin users | S; admin | Expose; API restrictions on actor/target roles apply |
| `POST /api/admin/users/{id}/deactivate`, `POST /api/admin/users/{id}/reactivate`, `DELETE /api/admin/users/{id}` | Admin users | S; admin; lifecycle opt-in and journal; separate permanent-delete opt-in | Expose gated operations; hide disabled actions. Permanent deletion is not reversible |
| `GET /api/admin/documents`, `GET /api/admin/documents/{id}/download` | Admin content | S; admin | Expose metadata and signed downloads |
| `POST /api/files/upload`, `PUT /api/admin/documents/{id}/replace`, `DELETE /api/admin/documents/{id}` | Admin upload/content | S + P; admin | Expose .NET document contracts. Upload/replace take one multipart `file`; UI bulk selection calls single upload repeatedly. No video |
| `GET /api/health/detailed`, `GET /api/admin/ingestion/stats`, `GET /api/admin/invitations/status/{email}`, `GET /api/visibility/vector-store`, `POST /api/ingest/document`, `POST /api/profile/complete` | Operator calls, tests or unmounted UI, not the active UI | Mapped on .NET with their provider, auth and feature prerequisites | Loopback only by default; reconsider edge exposure only with an identified caller |
| `POST /api/ingest/dev`, `DELETE /api/ingest/dev/{id}` | Direct .NET development tooling | Local .NET Development, explicit separate flags, S + P | Always deny at the edge; do not enable for the deployed service |
| `POST /api/videos/upload`, `POST /api/files/bulk`, `POST /api/ingest/bulk`, `POST /api/chat/search`, `POST /api/chat/ask`, `POST /api/chat/ask/video`, `POST /api/chat/recommendations` | Python test/operator or legacy frontend | No mapped .NET API equivalents with these contracts | Intentionally unavailable; no edge fallback. Video controls are not mounted and legacy frontend scripts are not loaded |
| `GET /openapi.json`, `GET /redoc`, Python `GET /docs` | Legacy Python tooling | No .NET Swagger UI | Not proxied; Blazor redirects `/docs` to `/chat` |

IIS sends page routes, assets and the `/_blazor` SignalR hub to Blazor. `/` and the old `/ui/` bookmark redirect to `/login`. The app serves `/chat`, `/chat/{sessionId}`, `/admin`, `/history`, `/settings`, `/transition` and `/reset-password` without an SPA fallback. Browser sign-in, signup and password handling use the Supabase SDK. The browser also accesses signed images and downloads directly after Blazor obtains the URL from the API.

The deployment operator owns the public allowlist; update the IIS config, this inventory and IIS policy tests together. Repository inspection is not sign-off for retiring any Python route in another deployment.
