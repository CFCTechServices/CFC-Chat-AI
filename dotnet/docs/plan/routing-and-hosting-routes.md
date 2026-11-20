# Python-free development route inventory

Inventory from the repo, not traffic observations. The active frontend is `web/index.html` → `web/main.jsx` and the scripts listed in the index. The runtime allowlist for IIS and the local proxy is [`deployment/routes-dotnet.manifest`](../../../deployment/routes-dotnet.manifest). Python still has its old route map for rollback; nothing routes to it in the .NET target.

In this table **S** means configured Supabase plus the relevant schema, grants and buckets; **P** means configured Pinecone plus the embedding model and vocabulary matching the index. Both are configuration gates, not proof of provider readiness. “Current user” means active account with a valid bearer token; “admin” requires admin/superuser and any operation-specific restrictions the API applies.

| Method and path | Observed caller | .NET auth / prerequisites | Target decision and contract |
| --- | --- | --- | --- |
| `GET /api/health` | Admin settings; deployer | None | Expose liveness only |
| `GET /api/auth/config` | User context | S; public | Expose project URL, public key, backend kind and UI flags, not service key |
| `POST /api/auth/check-email`, `POST /api/auth/forgot-password` | Sign-in, reset pages | S; public rate-limited | Expose; one shared edge quota; configure Supabase callback URL |
| `GET /api/profile/me` | Operator/API tests; current UI uses Supabase for profile reads | S; current user | Expose read-only current profile |
| `PATCH /api/profile/me` | Settings | S; current user; profile opt-in | Expose when enabled; hide editing otherwise |
| `GET /api/chat/sessions` | Chat and history | S; current user | Expose summary by default and preview/details with `detail=true` |
| `POST /api/chat/sessions`, `GET/PATCH/DELETE /api/chat/sessions/{id}` | Chat | S; current user/owner | Expose session creation, history, title and deletion |
| `GET /api/chat/feedback`, `POST /api/chat/feedback`, `POST /api/chat/message` | Chat/history | S; current user; P for both POSTs; optional Azure OpenAI for generation | Expose; GET feedback uses `session_id` query; chat supports extractive fallback |
| `GET /api/chat/content/images/{path}`, `GET /content/images/{path}` | Chat message renderer and legacy image alias | S; current user | Expose both; .NET returns signed URL JSON for supported private image paths, not raw local images |
| `GET/PATCH /api/admin/settings` | Admin settings | S; admin; persistent settings path | Expose supported settings; persist across releases |
| `GET /api/admin/users`, `POST /api/admin/invite`, `POST /api/admin/change-role` | Admin users | S; admin | Expose; API restrictions on actor/target roles apply |
| `POST /api/admin/users/{id}/deactivate`, `POST /api/admin/users/{id}/reactivate`, `DELETE /api/admin/users/{id}` | Admin users | S; admin; lifecycle opt-in and journal; separate permanent-delete opt-in | Expose gated operations; hide disabled actions. Permanent deletion is not reversible |
| `GET /api/admin/documents`, `GET /api/admin/documents/{id}/download` | Admin content | S; admin | Expose metadata and signed downloads |
| `POST /api/files/upload`, `PUT /api/admin/documents/{id}/replace`, `DELETE /api/admin/documents/{id}` | Admin upload/content | S + P; admin | Expose .NET document contracts. Upload/replace take one multipart `file`; UI bulk selection calls single upload repeatedly. No video |
| `GET /api/health/detailed`, `GET /api/admin/ingestion/stats`, `GET /api/admin/invitations/status/{email}`, `GET /api/visibility/vector-store`, `POST /api/ingest/document`, `POST /api/profile/complete` | Operator calls, tests or unmounted UI, not the active UI | Mapped on .NET with their provider, auth and feature prerequisites | Loopback only by default; reconsider edge exposure only with an identified caller |
| `POST /api/ingest/dev`, `DELETE /api/ingest/dev/{id}` | Direct .NET development tooling | Local .NET Development, explicit separate flags, S + P | Always deny at the edge; do not enable for the deployed service |
| `POST /api/videos/upload`, `POST /api/files/bulk`, `POST /api/ingest/bulk`, `POST /api/chat/search`, `POST /api/chat/ask`, `POST /api/chat/ask/video`, `POST /api/chat/recommendations` | Python test/operator or legacy frontend | No mapped .NET API equivalents with these contracts | Intentionally unavailable; no edge fallback. Video controls are not mounted and legacy frontend scripts are not loaded |
| `GET /openapi.json`, `GET /redoc`, Python `GET /docs` | Python tooling, Swagger iframe | No .NET OpenAPI UI in this host | Not proxied; the active `/docs` browser route shows UI notes without Swagger |

Browser routes: `/` redirects to `/ui/`. The existing browser router writes `/chat`, `/admin`, `/docs`, `/settings`, `/history` and `/reset-password`; the edge serves `ui/index.html` for these named paths on GET/HEAD only. Do not add a catch-all for unknown paths or missing UI assets. The unmounted transcript router and a historical login comment about `/api/auth/confirm-registration` do not identify active routes; sign-up/password handling still calls Supabase via its SDK. Some provider interactions (including signed downloads/images) go directly to provider origins after the same-origin API call.

Owner for the initial allowlist is the deployment operator; UI/API changes should update the manifest, this inventory and edge tests together. Repository inspection is not sign-off for retiring any Python route in another deployment.
