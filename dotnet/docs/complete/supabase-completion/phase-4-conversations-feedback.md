# Supabase completion — Phase 4: conversations and feedback read

**Status: contracts, adapters, DI registration, and unit coverage implemented. Live/controlled Supabase verification is still required; Phase 4 is not fully closed for production rollout.**

## Delivered

### Conversation/session persistence

Added `IConversationRepository` and application-owned DTOs in `dotnet/src/CFC.ChatAI.Supabase/Conversations/`:

- `ChatSession` with optional detail projection (`MessageCount`, `LastUserMessagePreview`).
- `ChatMessage` and `CreateChatMessage`, with `JsonElement` metadata for citations/relevant images.
- Session listing, creation, owner-filtered read/title update, ordered history, bounded last-N context, message insert, and feedback-message context reads.
- `SessionDeletionPartialFailureException` for the two-step session deletion case.

`SupabaseConversationRepository` uses anon API key plus the supplied caller token on each request. Session table operations include `user_id` filters. The adapter checks for an owned session before message reads/inserts/deletes; `chat_messages` has no `user_id` column in the supplied schema excerpt, so message isolation additionally depends on suitable deployed RLS policies. No user token is retained in adapter or shared client state.

Sessions are ordered by `created_at DESC, id ASC`; message history is oldest-first, and the last-N context result is reversed into chronological order. Detail listing performs a bounded exact-count request and a latest-user-message lookup per session; each preview is truncated to 100 characters, and detailed pages are capped at 100 sessions. Assistant message metadata and its preceding user message can be read for higher-level feedback processing. Inserts return DB-generated IDs/timestamps and round-trip JSON metadata.

Session deletion preserves the Python workflow: delete messages, then delete the owner-filtered session. These are separate requests, not a transaction. If the session delete request fails after message deletion succeeds, the repository reports a sanitized `SessionDeletionPartialFailureException`. No cascade behavior is assumed.

### Feedback read

Extended `IFeedbackRepository` with `GetUserScoresForMessagesAsync`. It queries `feedback` in batches of at most 100 message IDs, filters by the supplied `user_id`, omits null scores, and rejects input lists larger than 1000 IDs. This uses the existing repository's service-role path, with the explicit user filter; callers remain responsible for validating message/session ownership in the application workflow.

Existing vote submission remains one `submit_message_feedback` RPC call. Query-event delete and insert remain separate operations and are not described as atomic. No chat generation, embeddings, or cross-provider behavior was added.

### Registration/documentation

Registered `IConversationRepository` through `AddSupabaseInfrastructure`. Updated `dotnet/docs/supabase/README.md` with the contracts, user-token boundary, deletion semantics, feedback-read scope, and outstanding database prerequisites.

## Verification

Ran:

```text
dotnet test dotnet/tests/CFC.ChatAI.Supabase.Tests/CFC.ChatAI.Supabase.Tests.csproj --no-restore
```

Result: **21 passed, 0 failed**. Tests cover caller-token/anon-key use, owner-filtered session lookup, ordered last-N mapping, metadata JSON serialization/deserialization, bounded session detail count/preview, partial failure during two-request deletion, and feedback user filtering/null omission/input bounds. Existing adapter tests also pass.

No target-project or controlled Supabase integration test was run. No database credentials/project connection were provided for this phase.

## Database verification blockers

The supplied `docs/complete/supabase-completion/schema.sql` includes `chat_sessions(user_id,id,title,created_at,updated_at)`, `chat_messages(session_id,role,content,id,citations,created_at,metadata)`, and a message-to-session foreign key. It does **not** establish the deployed project identity, effective grants, RLS policies, insert/update/delete permissions, FK `ON DELETE` action, or live PostgREST count/return behavior. The supplied RLS/privilege exports cover profiles, not these conversation tables. In particular:

1. Verify that user-token reads/inserts/updates/deletes on `chat_sessions` and `chat_messages` are allowed only for the owning user. Confirm direct cross-user PostgREST access is rejected.
2. Confirm message insert policy checks ownership through the associated session, and that message delete policy prevents cross-user deletion. An ownership read alone is not a replacement for RLS.
3. Verify whether the session-message FK cascades. The adapter deliberately deletes messages first and does not depend on cascade.
4. Verify exact-count `Content-Range`, returned insert rows, and JSON metadata behavior on the target PostgREST version.
5. Verify `feedback` grants/RLS and the `score` column type/nullability. The supplied schema excerpt shows nullable integer scores but no unique constraints or deployed access policy.
6. Verify `submit_message_feedback` definition, argument SQL types, grants/security-definer behavior, and whether vote plus aggregate changes are transactionally atomic as the Python route comments describe.

Phase 4's implementation and unit-test criteria are met. Its live integration criterion and production security prerequisites remain **open** until these checks pass in a representative non-production Supabase project.
