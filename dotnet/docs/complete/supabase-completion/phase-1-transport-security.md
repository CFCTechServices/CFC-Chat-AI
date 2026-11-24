# Supabase completion — Phase 1: transport and security foundation

**Status: implemented**

## Changes

- `SupabaseTransport` now applies credentials only to the individual `HttpRequestMessage`; it does not mutate `HttpClient` defaults or retain per-user tokens.
- Authorization modes remain explicit helpers:
  - `SendAnonAsync`: anon key for both `apikey` and bearer authorization.
  - `SendUserAsync`: anon key as `apikey`, supplied user's access token as bearer authorization.
  - `SendServiceAsync`: service-role key for both headers, reserved for privileged adapter operations.
- `SupabaseOptions.RequestTimeout` bounds request establishment/response-header wait. It defaults to 30 seconds and validates in the range `(0, 5 minutes]`. Caller cancellation is passed through; a transport timeout becomes a sanitized `DependencyFailureException`.
- HTTP transport errors and non-success responses remain sanitized. Provider response bodies and credential values are not included in exception messages.
- Existing URI construction uses relative endpoint paths and `Uri.EscapeDataString` for dynamic path/query values. Transport requests remain cancellation-aware and responses are disposed by callers.
- No project or package dependency on Pinecone, API/application projects, or other providers was added. Embedding vectors passed to feedback operations remain data supplied by callers, not a model-service dependency.

## Verification

Ran:

```text
dotnet test dotnet/tests/CFC.ChatAI.Supabase.Tests/CFC.ChatAI.Supabase.Tests.csproj --no-restore
```

Result: **8 passed, 0 failed**. Coverage includes service-role and anon/user authorization headers, concurrent requests with distinct user tokens, dynamic query-value escaping, caller cancellation, bounded-timeout failure, and sanitized provider failures.

## Operational note

The configured transport timeout applies while establishing the HTTP response through response headers. Streaming response-body consumption remains cancellation-aware through the caller-provided token; callers handling streams should supply and honor cancellation rather than assuming an unbounded stream read is protected by the header timeout.

Transport credential isolation does not enforce database least privilege. The supplied profile RLS/grant exports show that authenticated users have effective INSERT/UPDATE privileges on every profile column, while row policies only require `auth.uid() = id`. The Phase 2 self-profile adapter allow-lists fields, but direct PostgREST requests are not similarly constrained. Resolve this database-side issue as described in `docs/plan/supabase-profile-self-service-security.md` before production self-service profile writes.
