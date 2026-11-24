# Endpoint provider dependency checklist

Use this checklist when porting or reviewing the active API routes. For each route, ask: **“Does this endpoint need Pinecone, Supabase, both, or none?”** The answer covers the route's end-to-end behavior, not just which provider package owns persistence. “Supabase” includes Supabase Auth, Postgres/PostgREST, and Storage. Local files, model APIs, and other services are not represented by these four choices.

This inventory covers the 42 active API routes identified in `complete/supabase-completion/phase-5-document-queries-parity-audit.md`. It does not imply those routes are already implemented in .NET; currently the .NET API maps only the health routes. Unmounted transcript routes and non-API/static routes are excluded.

| Active endpoint | Simple question | Needs | Why |
|---|---|---|---|
| `GET /api/health` | Does this endpoint need Pinecone, Supabase, both, or none? | **None** | Reports process/application health only. |
| `GET /api/health/detailed` | Does this endpoint need Pinecone, Supabase, both, or none? | **Both** | Probes Pinecone and Supabase (as well as host/model configuration). The Supabase probe currently lives directly in the API host. |
| `GET /api/auth/config` | Does this endpoint need Pinecone, Supabase, both, or none? | **None** | Returns public host/auth configuration; it does not call Supabase. |
| `GET /api/visibility/vector-store` | Does this endpoint need Pinecone, Supabase, both, or none? | **Pinecone** | Reads vector-store/index visibility or statistics. |
| `POST /api/auth/forgot-password` | Does this endpoint need Pinecone, Supabase, both, or none? | **Supabase** | Uses Supabase Auth to initiate password recovery. |
| `POST /api/auth/check-email` | Does this endpoint need Pinecone, Supabase, both, or none? | **Supabase** | Checks Auth/profile and invitation records. |
| `GET /api/profile/me` | Does this endpoint need Pinecone, Supabase, both, or none? | **Supabase** | Reads the authenticated user's profile. |
| `PATCH /api/profile/me` | Does this endpoint need Pinecone, Supabase, both, or none? | **Supabase** | Updates the authenticated user's profile; production use remains blocked pending database privilege/RLS remediation. |
| `POST /api/profile/complete` | Does this endpoint need Pinecone, Supabase, both, or none? | **Supabase** | Completes the authenticated user's profile using the same profile persistence. |
| `POST /api/admin/invite` | Does this endpoint need Pinecone, Supabase, both, or none? | **Supabase** | Creates/checks invitation and profile records. |
| `GET /api/admin/invitations/status/{email}` | Does this endpoint need Pinecone, Supabase, both, or none? | **Supabase** | Reads invitation status. |
| `GET /api/admin/users` | Does this endpoint need Pinecone, Supabase, both, or none? | **Supabase** | Lists user profiles. |
| `POST /api/admin/change-role` | Does this endpoint need Pinecone, Supabase, both, or none? | **Supabase** | Updates a user's profile role. |
| `POST /api/admin/users/{user_id}/deactivate` | Does this endpoint need Pinecone, Supabase, both, or none? | **Supabase** | Updates profile state and performs Supabase Auth administration. |
| `POST /api/admin/users/{user_id}/reactivate` | Does this endpoint need Pinecone, Supabase, both, or none? | **Supabase** | Updates profile state and performs Supabase Auth administration. |
| `DELETE /api/admin/users/{user_id}` | Does this endpoint need Pinecone, Supabase, both, or none? | **Supabase** | Deletes invitation/Auth/profile-related data; lifecycle/cascade behavior needs deployment verification. |
| `GET /api/admin/settings` | Does this endpoint need Pinecone, Supabase, both, or none? | **None** | Reads local/application settings. |
| `PATCH /api/admin/settings` | Does this endpoint need Pinecone, Supabase, both, or none? | **None** | Updates local/application settings. |
| `GET /api/admin/ingestion/stats` | Does this endpoint need Pinecone, Supabase, both, or none? | **None** | Reports local ingestion/filesystem status. |
| `POST /api/chat/search` | Does this endpoint need Pinecone, Supabase, both, or none? | **Both** | Searches Pinecone, then hydrates/scorers results using Supabase chunk and feedback data. |
| `POST /api/chat/ask` | Does this endpoint need Pinecone, Supabase, both, or none? | **Both** | Performs vector retrieval and answer/context work with Supabase-backed chunk and feedback data. |
| `POST /api/chat/ask/video` | Does this endpoint need Pinecone, Supabase, both, or none? | **Both** | Retrieves video-related chunks from Pinecone and uses Supabase chunk metadata/hydration. |
| `POST /api/chat/recommendations` | Does this endpoint need Pinecone, Supabase, both, or none? | **Both** | Uses vector retrieval plus Supabase chunk/feedback data. |
| `GET /api/chat/sessions` | Does this endpoint need Pinecone, Supabase, both, or none? | **Supabase** | Lists the user's persisted chat sessions. |
| `POST /api/chat/sessions` | Does this endpoint need Pinecone, Supabase, both, or none? | **Supabase** | Creates a persisted chat session. |
| `GET /api/chat/sessions/{session_id}` | Does this endpoint need Pinecone, Supabase, both, or none? | **Supabase** | Reads an owned session and its message history. |
| `DELETE /api/chat/sessions/{session_id}` | Does this endpoint need Pinecone, Supabase, both, or none? | **Supabase** | Deletes persisted messages and session data. |
| `PATCH /api/chat/sessions/{session_id}` | Does this endpoint need Pinecone, Supabase, both, or none? | **Supabase** | Updates persisted session metadata/title. |
| `POST /api/chat/message` | Does this endpoint need Pinecone, Supabase, both, or none? | **Both** | Persists/loads conversation history in Supabase and runs RAG retrieval using Pinecone. |
| `POST /api/chat/feedback` | Does this endpoint need Pinecone, Supabase, both, or none? | **Supabase** | Saves feedback and related scoring/events in Supabase. Embedding work is local; it does not call Pinecone. |
| `GET /api/chat/feedback` | Does this endpoint need Pinecone, Supabase, both, or none? | **Supabase** | Reads the user's feedback for messages in a session. |
| `POST /api/files/upload` | Does this endpoint need Pinecone, Supabase, both, or none? | **Both** | Stores source/chunk data in Supabase and indexes vectors in Pinecone as part of ingestion. |
| `POST /api/files/bulk` | Does this endpoint need Pinecone, Supabase, both, or none? | **Both** | Bulk ingestion persists content/chunks in Supabase and indexes vectors in Pinecone. |
| `POST /api/ingest/document` | Does this endpoint need Pinecone, Supabase, both, or none? | **Both** | Ingests document objects/chunks into Supabase and vectors into Pinecone. |
| `POST /api/ingest/bulk` | Does this endpoint need Pinecone, Supabase, both, or none? | **Both** | Bulk ingestion uses Supabase storage/persistence and Pinecone indexing. |
| `GET /api/admin/documents` | Does this endpoint need Pinecone, Supabase, both, or none? | **Supabase** | Lists document/chunk metadata; vector search is not needed. |
| `GET /api/admin/documents/{doc_id}/download` | Does this endpoint need Pinecone, Supabase, both, or none? | **Supabase** | Looks up document metadata and downloads/signs source objects from Supabase Storage. |
| `DELETE /api/admin/documents/{doc_id}` | Does this endpoint need Pinecone, Supabase, both, or none? | **Both** | Removes Supabase chunks/objects and coordinates deletion of related Pinecone vectors. |
| `PUT /api/admin/documents/{doc_id}/replace` | Does this endpoint need Pinecone, Supabase, both, or none? | **Both** | Replaces Supabase document/chunk data and coordinates vector purge/re-ingestion in Pinecone. |
| `POST /api/videos/upload` | Does this endpoint need Pinecone, Supabase, both, or none? | **Both** | Stores video assets/chunks in Supabase and indexes searchable vectors in Pinecone. |
| `GET /api/chat/content/images/{path:path}` | Does this endpoint need Pinecone, Supabase, both, or none? | **Supabase** | Reads the image object from Supabase Storage (with a possible local fallback). |
| `GET /content/images/{path:path}` | Does this endpoint need Pinecone, Supabase, both, or none? | **Supabase** | Compatibility alias for the image-serving path; reads the same stored object. |

## Applying the checklist

- **None** means the route should not require either provider; keep its operation local/configuration-only.
- **Pinecone** means vector search/index operations are needed, without Supabase persistence for that route.
- **Supabase** means Auth, database, or Storage operations are needed, without Pinecone vector operations.
- **Both** means the endpoint workflow crosses the two providers. Keep that coordination in the application/workflow layer, not inside either provider adapter, and account for partial failures because there is no cross-provider transaction.
- Provider needs are not the same as migration status: some listed routes are not yet implemented in the .NET API. Detailed health's direct Supabase HTTP call is a known boundary gap; it should use a focused Supabase health capability.
