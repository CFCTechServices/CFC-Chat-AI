# Implement the remaining .NET internal API endpoints

**Base:** `migration/dotnet` ← **Head:** `migration/remaining-endpoints`

## Summary

This PR implements the eight remaining in-scope method/path pairs for the .NET internal API. Six cover admin and operator workflows; the two self-profile write routes are implemented but not mapped by default. The merge does not switch traffic, apply database changes, or add video upload.

## What changed

- **Local staging:** Add admin-only `GET /api/admin/ingestion/stats` and `POST /api/ingest/document`. Stats are a bounded scan of configured local directories, not cloud ingestion progress; unavailable directories return 503. Staged ingest accepts one filename from an operator-controlled root without following links or deleting the source. The stats tab remains unmounted.
- **Document replacement:** Add `PUT /api/admin/documents/{doc_id}/replace` and restore the replacement action in Content Library. The replacement filename must derive the existing document ID and keep the existing extension. The handler prepares the new content before purging old data, then overwrites the canonical original last. Partial failures return a stage and reconciliation reference; writes across Supabase and Pinecone are not transactional, and same-document exclusion only covers a single .NET process.
- **Account lifecycle:** Add `POST /api/admin/users/{user_id}/deactivate`, `POST /api/admin/users/{user_id}/reactivate`, and `DELETE /api/admin/users/{user_id}`. These actions reject self-targets and superusers, require explicit lifecycle enablement and a private persistent journal, and use that journal to resume the same action after a partial failure. Permanent deletion has a separate default-off flag. The users UI checks for confirmed outcomes and surfaces reconciliation references. The PR includes Supabase inspection and session-revocation SQL for operator review; it does not apply either script.
- **Self-profile writes:** Add `PATCH /api/profile/me` and `POST /api/profile/complete` behind `Api:EnableProfileWrites` (default off). Both require an active user and pass that caller's ID and token to Supabase to update `full_name` and/or `avatar_url`. The API field list does not protect against direct Supabase clients; the target database grants and RLS must be verified before enabling these routes.

The [delivery notes](../complete/internal-api/internal-api-remaining-endpoints-delivery.md) describe configuration, reconciliation, and the disposable smoke script for the first six routes. Their statement that profile writes are unmapped predates the final commit: those routes now map only when the feature flag is enabled. The [document upload/deletion parity PR notes](document-upload-deletion-parity.md) are included as a record of previously merged functionality.

## Verification

- `dotnet test CFC.ChatAI.slnx --no-restore` — **257 passed, 3 skipped** (fixture-dependent document parity tests), 0 failed.
- Local tests cover staging and replacement contracts, authorization, provider failure handling, and account journal retries. The profile write routes have default-off contract coverage; enabled-route coverage remains outstanding.
- No live Supabase/Pinecone acceptance tests or browser smoke tests were run for this PR.

## Before cutover

- [ ] Keep `Api:EnableProfileWrites` off until enabled-route tests pass and the [profile security checks](../plan/supabase-profile-self-service-security.md) verify grants, RLS, signup, and rejection of unauthorized writes against the target database.
- [ ] In disposable providers, verify staged ingest and replacement, including cleanup of Supabase rows and Storage objects and independent checks of Pinecone vectors. Exercise partial-failure recovery before routing writes to .NET.
- [ ] Before enabling account actions, verify the session-revocation RPC and that deactivated users lose application access. Configure a private persistent journal and use a single writer. Independently verify actual foreign-key and data-retention behavior before enabling permanent deletion.
- [ ] Verify same-origin routing from the separately hosted UI and run the [frontend smoke tests](../frontend-changes.md). Keep Python routing for unverified routes or disable those features, and document how to restore routing. See the [cutover checklist](../plan/internal-api-cutover.md).
