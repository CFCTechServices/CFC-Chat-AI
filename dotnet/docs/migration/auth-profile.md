# Chunk 2: auth self-service and profile

## Status

The five routes are implemented in the C# host and covered by deterministic tests. `deployment/iis-web.config` now includes five exact method-specific rules targeting port 5000, above the Python catch-all, with query strings preserved and backend JSON errors passed through. **Live cutover and production readiness remain unverified:** live schema/RLS/grants, recovery redirects, deployed Python parity, abuse thresholds/proxy trust, and IIS forwarding still require the operational gates below. Python remains the fallback for unclaimed routes and the rollback implementation.

## Routes and boundaries

| Method/path | Access | Behavior |
|---|---|---|
| POST `/api/auth/forgot-password` | Anonymous | Enumeration-safe recovery acknowledgment; upstream 429 becomes HTTP 200 with `success=false`. |
| POST `/api/auth/check-email` | Anonymous | Latest **unregistered** invitation, ordered by `created_at` descending, limited to one; UTC expiry at or before now is expired. |
| GET `/api/profile/me` | Supabase identity + `ActiveAccount` | Read verified ID with anon apikey and caller token/RLS. |
| PATCH `/api/profile/me` | Same | Update non-null `full_name`/`avatar_url` only. |
| POST `/api/profile/complete` | Same | Independently mapped route, same update service; no creation/invitation/status mutation. |

`Application/Abstractions` exposes `IIdentityGateway`, `IProfileRepository`, `IProfileAccessReader`, and `IInvitationRepository`. `Infrastructure/Supabase` owns provider URLs, queries, credentials, JSON mapping, and transport. `AddSupabaseInfrastructure()` registers the module. There is no generic table/RPC capability, mutable SDK session, local JWT shortcut, admin mutation interface, or new RAG/Pinecone dependency.

`Authentication/SupabaseAuthentication.cs` composes Auth validation and privileged account-state reads. A `VerifiedUser` immutable request feature becomes available through scoped `CurrentUser` **only after the active-account gate succeeds**. Tokens are not claims, persistent state, cached identities, or log fields. Account status is checked on every protected request. Anonymous/diagnostic routes do not validate even an accidentally supplied bearer token. There is no fallback authorization policy.

## Configuration

Existing dotenv discovery and precedence apply (see [runtime diagnostics](runtime-diagnostics.md)). Hierarchical settings take precedence over their Python-style equivalents:

| Hierarchical | Legacy | Default |
|---|---|---|
| `Supabase:Url` | `SUPABASE_URL` | Missing |
| `Supabase:AnonKey` | `SUPABASE_ANON_KEY` | Missing |
| `Supabase:ServiceRoleKey` | `SUPABASE_SERVICE_ROLE_KEY` | Missing |
| `Frontend:BaseUrl` | `FRONTEND_BASE_URL` | `http://localhost:8000/ui` (matches `app/config.py`) |
| `Supabase:RequestTimeoutSeconds` | — | 5; positive finite number, at most 60 |
| `Auth:RecoveryRequestsPerMinute` | — | 10 |
| `Auth:EligibilityRequestsPerMinute` | — | 30 |

Missing credentials/unsafe Supabase URLs do not prevent host startup. Operations fail closed or use their generic public outcome. Invalid timeout/rate-limit settings are configuration errors, not missing-credential behavior. Supabase URLs must be HTTPS without credentials/query/fragment. Self operations reject obviously privileged keys mistakenly configured as anon (matching service key, `sb_secret_`, or JWT service-role claim).

Auth validation uses the service key as `apikey` and the caller token as bearer. Recovery, status, and invitation reads use service-role apikey/bearer credentials. Self reads/updates use **only** anon apikey/caller bearer, with no fallback. Verify these Auth apikey choices against the deployed project before cutover.

The named `CFC.Supabase` client disables redirects, removes URI-bearing default HTTP loggers, redacts headers, and sets per-message authorization. One linked deadline spans send and JSON parsing; caller cancellation propagates. There are **no retries**, including recovery/email side effects. Fixed operational warnings contain only classifications or HTTP status codes, not exception/provider text, emails, profile values, IDs, or credentials. Keep external tracing/APM body/header/URL capture disabled or appropriately scrubbed too.

Recovery forwards the configured frontend URL after removing trailing `/`, URL-encoded in `redirect_to`, with a body containing only `email`. The response acknowledges a request; it does not guarantee delivery. Ensure this configured redirect is in the Supabase allowlist.

## Contract capture and parity decisions

`local/scripts/capture-auth-profile-contracts.py` extracts the actual Python request models with AST (avoids live SDK initialization) and probes installed FastAPI `HTTPBearer` in an offline TestClient. Results: `local/auth-profile-contract-capture.jsonl`. Local versions were FastAPI 0.142.1 and Pydantic 2.13.5. This proves local schema/extraction behavior, **not** deployed package versions, live Auth rejection behavior, or RLS. Repeat on the deployment before approval.

### Validation

Schema errors use HTTP 422 and `{"detail":[{"type":...,"loc":["body",...],"msg":...,"input":...}]}` rather than ASP.NET ProblemDetails/400. Missing/null bodies and missing required email, null/wrong-type email, wrong-type optional fields, non-object bodies, and the captured malformed `{` case have compatible shapes. Extras are ignored. Recovery requires a string, not a valid email, and does not trim. Names/avatar URLs are arbitrary strings, with no name/URL validation or trimming. Null/omitted update fields are ignored; empty strings are written. No allowed non-null field yields HTTP 400 `No fields to update. Provide full_name or avatar_url.`

Invitation email normalization preserves local-part case and lowercases the domain, trims surrounding whitespace, handles IDN domains/NFC, and accepts display-name addresses. The C# helper is **not a complete port of Python email-validator**: uncommon Unicode local-part acceptance and detailed reasons for invalid syntax can differ. Standard addresses and the captured invalid `bad` case are covered. Likewise, uncaptured malformed JSON uses fixed `Invalid JSON` context instead of Python's decoder-specific wording, and its offset is based on System.Text.Json's byte position (can differ for non-ASCII). **These remaining validation parity gaps need resolution or explicit product approval before cutover.** Non-JSON content types/encodings also need deployed Python parity checks; do not infer complete compatibility from the tested application/json cases.

### Bearer/status response matrix

| Condition | HTTP/detail |
|---|---|
| Missing header, wrong scheme, missing/empty bearer value | 401 `Not authenticated` |
| Rejected Auth credentials (401/403) or absent Auth user ID | 401 `Invalid token` |
| Auth timeout/transport/upstream failure or malformed identity | 503 `Authentication service unavailable` |
| Gate profile absent | 403 `User profile not found` |
| `inactive` | 403 `Account is inactive` |
| `deleted` | 403 `Account has been deleted` |
| Other/null/missing status | 403 `Account is not active` |
| Gate unavailable/unconfigured/malformed row | 503 `Account verification unavailable` |
| Active | Continue with verified request context |

401 responses include `WWW-Authenticate: Bearer`. Scheme matching is case-insensitive. Like FastAPI, extraction partitions at the first space; remaining token text is submitted to Auth, not decoded or trimmed into an identity. Local capture shows extra-space/extra-word credentials pass **extraction**, not authentication. Verify deployed Auth's rejection statuses (including possible 400/error-code variants) and deployed HTTPBearer behavior before approval.

503 dependency responses deliberately replace Python's catch-all 401. Profile provider/mapping failures return fixed HTTP 500 `Failed to get profile` / `Failed to update profile`, not Python exception text. `deleted_at` does not add an independent account policy.

### Profile and absence semantics

Exactly seven response fields: `id`, `email`, nullable `full_name`, nullable `avatar_url`, `role`, `status`, and `created_at`. `deleted_at` and other columns are excluded. Valid provider timestamp strings retain their original offset/precision instead of being reserialized as DateTime. Malformed rows fail safely, including mismatched IDs and invalid/offset-free timestamps.

Self reads and privileged status reads deliberately use bounded **array** responses instead of `.single()`. `[]` is genuine absence: a gate absence is 403; a self-read absence after a successful gate is 404 `Profile not found`. This normalizes the Python SDK's possible zero-row `.single()` exception into the intended contract. A provider error/406 is not blindly treated as absence. Updates request `Prefer: return=representation`, return the first row, and map zero rows to HTTP 500.

Invitation offset-free/missing/malformed expiry fails with generic eligibility failure, separately logged from real absence. No older invitation is searched after the latest unregistered invitation expires; registered rows are filtered before ordering. Eligibility is **not signup authorization**.

### Public abuse controls (new compatibility decision)

Separate fixed-window, per-transport-peer-IP limits: recovery 10/minute and eligibility 30/minute by default, no queue. Local rejection is HTTP 429 `{"detail":"Too many requests. Please try again later."}` with `Retry-After: 60`; this does **not** replace upstream recovery-429's HTTP 200 contract. Invalid requests count toward the limit. These are proposed rollout defaults and require product/operations approval.

No `X-Forwarded-For` headers are trusted. Behind IIS, callers may share the proxy's quota. **Do not enable cutover with per-client rate-limit assumptions until known-proxy/forwarded-IP processing and deployment thresholds are explicitly approved and tested.** Multi-instance limits are process-local, not distributed. `check-email` intentionally distinguishes invitation absence/expiry/eligibility; it is not fully enumeration-safe and should not be advertised as such.

## Tests

From repository root:

```powershell
dotnet test dotnet/CFC.ChatAI.slnx
dotnet test dotnet/tests/CFC.ChatAI.Api.Tests --filter "FullyQualifiedName~AuthProfileTests|FullyQualifiedName~SupabaseSafetyTests"
```

Chunk-2 deterministic tests: **87 passed**. Coverage includes all five routes, gate matrix on all profile routes, deactivation without caching, identity failures, exact profile projection/nulls/timestamp precision, allowlisted patch semantics, credentials/headers/queries, filter escaping, simultaneous tokens, no privileged fallback, recovery's generic outcomes/429/no-retry, invitation normalization/UTC boundaries/malformed rows, configuration bounds, deadlines spanning body parsing, cancellation, local rate limits, and log redaction.

The full solution regression run (`dotnet test dotnet/CFC.ChatAI.slnx --no-restore`) passes: **190 passed, 3 skipped, 0 failed**. Unsupported document-delete tests were removed; the opt-in Pinecone test now uses explicit chunk-ID deletion, including cleanup. Both health messages match Python without the scaffolding suffix. IIS configuration tests cover all nine migrated method/path pairs, Python fallback, query-string preservation settings, and error pass-through. They do not prove live ARR forwarding.

### Opt-in live authorization test

`SupabaseAuthorizationIntegrationTests` is skipped unless `RUN_SUPABASE_AUTH_TESTS=1`. Supply **only an isolated test project** and two distinct disposable active users:

- `TEST_SUPABASE_URL`
- `TEST_SUPABASE_ANON_KEY`
- `TEST_SUPABASE_SERVICE_ROLE_KEY`
- `TEST_SUPABASE_USER_A_TOKEN`, `TEST_SUPABASE_USER_B_TOKEN` (fresh access tokens)
- `TEST_SUPABASE_INVITATION_EMAIL` (a seeded invitation visible to the backend)

```powershell
$env:RUN_SUPABASE_AUTH_TESTS = "1"
dotnet test dotnet/tests/CFC.ChatAI.Api.Tests --filter "Category=Integration"
```

The test validates live Auth identities, both directions of cross-user RLS read/update isolation, direct user-token restrictions on role/status/email/id/deletion/timestamps, API own-ID behavior and updates, immediate rejection after service-role deactivation on all three routes, and anonymous/user inability to read the seeded invitation. It restores profile fields in `finally`; **a broken policy/schema may still require manual cleanup**, especially trigger effects or failed identity restoration. Never run against production. No account creation or recovery emails are triggered by this test. The live test has **not been run** here.

## Outstanding operational gates

1. Review deployed profile schema/types/nullability, identity uniqueness, signup trigger, status values, self-select/self-update RLS, and column grants/policies. Database restrictions must protect direct PostgREST, not only API updates.
2. Review privileged profile/invitation grants, signup enforcement, invitation email storage normalization, duplicate/ordering conventions, UTC expiry columns, and ties in `created_at`. Do not invent a tie-breaker without agreement.
3. Run the opt-in live test with disposable users; verify deleted/unknown status behavior, signup-trigger creation, and invitation latest-unregistered semantics on real data. Mock query assertions cannot prove live database ordering/grants.
4. Resolve/approve remaining validation parity differences, repeat the bearer capture with deployed packages, and verify Auth endpoint apikey/rejection behavior.
5. Verify recovery redirect allowlist and provider success/rate-limit/error behavior with disposable accounts, never production credentials in CI.
6. Approve abuse thresholds, local 429 shape, trusted proxy handling, and distributed/per-instance limits. Ensure IIS forwards auth/body/query and does not replace JSON errors.
7. Rerun full regression tests against the deployment candidate; the local solution run is green, but live opt-in tests remain unverified.

## Cutover and rollback (configuration ready; live application pending)

The repository configuration contains these **five exact method-specific** IIS rules above the Python catch-all, targeting port 5000 and preserving query strings. Apply it to the running IIS site only after the operational gates pass:

| Rule | Match URL | Method condition |
|---|---|---|
| `DotNet_AuthForgotPassword` | `^api/auth/forgot-password$` | `^POST$` |
| `DotNet_AuthCheckEmail` | `^api/auth/check-email$` | `^POST$` |
| `DotNet_ProfileMeGet` | `^api/profile/me$` | `^GET$` |
| `DotNet_ProfileMePatch` | `^api/profile/me$` | `^PATCH$` |
| `DotNet_ProfileComplete` | `^api/profile/complete$` | `^POST$` |

Do not claim `/api/auth/*` or `/api/profile/*`, alter chunk-1 config routing, or migrate admin routes. Verify direct C# first, then IIS: exact methods, request bodies, bearer forwarding, query preservation, validation/status errors, provider rate-limit acknowledgment, local 429, and existing Python-only/admin/wrong-method fallback. Stopping .NET should demonstrate the intended routes fail at the proxy, not silently use another implementation. Keep the Python endpoints intact. Roll back by disabling/removing only these five exact rules; requests fall through to Python on port 8000. No live IIS cutover/rollback verification has occurred in this implementation session.
