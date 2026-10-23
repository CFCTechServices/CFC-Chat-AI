# Supabase completion — remaining work plan

This checklist consolidates work that is still open after the persistence contracts/adapters and Phase 6 documentation deliverables. It is intentionally **not** a claim that the integration is production-ready. The completed phase reports and original plan are in `docs/complete/supabase-completion/`.

## Priority 0 — security and deployment identity gates

- [ ] Confirm the supplied profile schema, RLS, and privilege exports belong to the intended Supabase project/environment. Inspect the source of effective table/column grants and any profile-creation trigger.
- [ ] Review and apply a database-side least-privilege fix for profile self-service writes. Authenticated/anon clients must not be able to set `role`, `status`, `deleted_at`, or `deleted_by` on UPDATE or INSERT. The adapter DTO allow-list is not an adequate database security boundary.
- [ ] In a non-production project, run direct PostgREST negative tests with a normal authenticated user token, including attempts to self-promote, change account status/deletion metadata, insert a profile with privileged values, and access another user's profile. Verify service-role admin profile operations still work.
- [ ] Keep `PATCH /api/profile/me` and `POST /api/profile/complete` self-service writes disabled/blocked until the database gate above passes.

Detailed existing remediation notes: `docs/plan/supabase-profile-self-service-security.md`.

## Priority 1 — target database and Storage verification

Collect sanitized evidence from the intended non-production target project; do not commit credentials, JWTs, signed URLs, user PII, or unrestricted dumps.

- [ ] Verify actual columns/types, indexes/constraints, effective grants, RLS policies, FK actions, and triggers for `profiles`, `invitations`, `chat_sessions`, `chat_messages`, `feedback`, `document_chunks`, `chunk_feedback_scores`, and `chunk_feedback_events`.
- [ ] Verify the invitation expiry default and the active-invitation partial unique index, including its exact name/predicate. Run concurrent invitation insert tests and confirm only the recognized conflict maps to the typed conflict.
- [ ] Verify chat session/message policies and grants for select/insert/update/delete. Prove cross-user session and message reads/writes/deletes are rejected. In particular, verify message policies enforce ownership through the associated session.
- [ ] Verify `chat_messages.session_id` FK delete behavior; confirm the adapter's message-then-session deletion semantics and exercise its partial-failure case against the project.
- [ ] Verify feedback grants/RLS and uniqueness/nullability; inspect `submit_message_feedback` argument SQL types, grants, security-definer/search-path behavior, and transactional vote/aggregate semantics.
- [ ] Verify `get_query_aware_chunk_scores` signature, parameter/return types, execution grants, vector dimension/index, and event table constraints. Confirm that the actual dimension matches supplied embeddings.
- [ ] Verify `document_chunks` types/conflict target/grants and chunk feedback FK actions.
- [ ] Verify Storage bucket names, privacy/policies, limits, overwrite behavior, and service-role access. Check representative document, current/legacy video, transcript, and image keys; test nested listing pagination/traversal, delete, download, and signed URL expiry.

The supplied `schema.sql` is contextual only and does not prove it is the active schema. Profile exports cover only profile-specific policy/privilege evidence; the other checks above are not evidenced by those exports.

## Priority 2 — authorization decisions and API boundary completion

- [ ] Record the application-owner decision on observed Python-vs-.NET auth behavior: token-validation credential mode, password-recovery key mode, and service-role versus caller-token access for chat message/feedback flows. Do not treat the differing behavior as equivalent without approval and tests.
- [ ] Confirm whether chat/message workflows keep caller-token/RLS access or preserve existing Python service-role access with explicit user/session filters and application authorization. Verify the selected model end-to-end.
- [ ] Move the detailed-health endpoint's direct `GET /rest/v1/profiles` request (`dotnet/src/CFC.ChatAI.Api/Endpoints/Health/HealthEndpoints.cs`) behind a focused Supabase health capability, or explicitly document/approve an exception. Keep Pinecone/model health probes outside the Supabase package.
- [ ] Run a dependency-boundary check that `CFC.ChatAI.Supabase` has no API/application/provider project references or non-Supabase provider namespaces. Add/retain a lightweight CI architecture check if not already enforced.
- [ ] Review active endpoints for any direct Supabase SDK/REST/Auth/Storage call paths outside the package; migrate each to a focused contract or mark it intentionally host-owned.

## Priority 3 — consuming application behavior and integration tests

The adapter contracts do not implement endpoint business policy. Complete/verify these concerns in the consuming application layer:

- [ ] Implement/test profile status and role authorization, admin/superuser/self-action safeguards, and allowed role transitions; keep identity token validation distinct from profile lookup.
- [ ] Implement/test invitation eligibility and status (`active`, `expired`, `registered`, `none`) with an injected UTC clock, profile-already-exists handling, 409 race mapping, and behavior after delete-then-insert partial failure.
- [ ] Verify account deactivation/reactivation and hard deletion workflow ordering, best-effort Auth actions, profile/auth FK cascades, invitation cleanup, and retry/compensation semantics.
- [ ] Verify chat session ownership, message context limits/metadata, feedback rating validation, feedback ownership checks, and endpoint response contracts using fakes at the application boundary.
- [ ] Exercise representative multi-request partial failures (session delete, invitation replacement, feedback event sync, bulk chunk/storage work) and ensure callers do not claim transactionality or blindly retry uncertain writes.
- [ ] Run integration tests in a disposable non-production Supabase project for user-vs-service authorization, invitation concurrency, cross-user access, session deletion, admin lifecycle, RPC behavior, object pagination, and signed URL expiration.

## Priority 4 — scale and rollout readiness

- [ ] Measure `ListDocumentSummariesAsync` against representative corpus size. It currently scans chunk rows and aggregates in memory across paged requests without a snapshot; if this is too costly or inconsistent, propose a separately reviewed SQL view/RPC and test it rather than hiding schema changes in the adapter.
- [ ] Add endpoint-level rate-limit/retry policy for Supabase Auth/REST/Storage operations. Honor `Retry-After` when applicable; only retry operations with understood idempotency and reconcile writes that may have committed before timeout.
- [ ] Confirm production secret-manager injection, HTTPS endpoint configuration, bucket names, request timeout, alerting, and safe operational logs. Never expose service-role keys, access tokens, PII, or signed URLs.
- [ ] Complete a go/no-go review against the original completion criteria in `docs/complete/supabase-completion/supabase-completion-plan.md`, and record evidence, project/environment, test date, and unresolved risks without recording secrets.

## Current status

The Supabase persistence module and its HTTP-handler unit tests are implemented; the Phase 6 documentation/configuration checks are complete. The latest adapter test run passed **25/25** tests, but no live Supabase integration tests were run. The profile self-write ACL issue, target-project identity/schema/RLS/RPC/Storage verification, user/service scope decisions, detailed-health boundary call, and application-level endpoint verification remain unresolved. Do not declare production readiness until the priority 0 and priority 1 gates are closed and the relevant application/integration tests pass.
