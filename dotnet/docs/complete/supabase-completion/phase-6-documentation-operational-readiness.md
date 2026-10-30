# Supabase completion — Phase 6: documentation and operational readiness

**Status: documentation and local configuration-readiness work delivered. Production readiness remains blocked on target-project/database verification and the open work listed in `docs/plan/supabase-completion-remaining-plan.md`.**

## Delivered

- Updated `dotnet/docs/supabase/README.md` to reflect the implemented contract map, authorization modes, registration/options behavior, persistence boundaries, partial-operation guarantees, and current migration prerequisites.
- Added operational guidance for secret handling, bucket verification, RLS/grants, rate limits and safe retry behavior, request timeouts, monitoring, and the distinction between unit tests and deployment integration tests.
- Tightened `SupabaseOptions.Validate()` so the project URL must be an absolute HTTP(S) URL without embedded user credentials, query, or fragment. Local HTTP remains supported for local development. Existing validation still requires both keys and bucket names and constrains request timeout to `(0, 5 minutes]`; production HTTPS is an operational requirement documented in the README.
- Added a unit test for URL validation, including acceptance of a local HTTP URL.
- Moved the completion plan to `docs/complete/supabase-completion/supabase-completion-plan.md`. The remaining-work checklist is now maintained at `docs/plan/supabase-completion-remaining-plan.md`.

## Verification

Ran:

```text
dotnet test dotnet/tests/CFC.ChatAI.Supabase.Tests/CFC.ChatAI.Supabase.Tests.csproj --no-restore
```

Result: **25 passed, 0 failed, 0 skipped**. This is adapter/configuration unit coverage using recording HTTP handlers; it does not establish live Supabase schema, RLS, grants, RPC, or Storage behavior.

## Operational and security caveats

- No credentials were used and no deployment-specific policy facts were added to source control.
- The existing module validates configuration shape at registration. It cannot prove that configured secrets, URL, buckets, or database policies belong to the same project.
- The supplied profile ACL/RLS evidence still shows a self-service profile write escalation risk. Do not enable production self-service profile writes until database-side permissions/policies have been reviewed and direct user-token negative tests pass.
- No controlled or production Supabase integration suite was run. Invitation concurrency, conversation ownership/RLS, RPC semantics, Storage policies/paths, service-role behavior, and all other deployment prerequisites remain open.

## Phase 6 exit assessment

- [x] README matches the implemented contracts, auth modes, DI registrations, and documented persistence guarantees.
- [x] Secret, bucket, RLS/grant, rate-limit/retry, timeout, and monitoring guidance is recorded without embedding environment secrets.
- [x] Configuration rejects malformed/insecure URL components while retaining local HTTP testability.
- [x] Adapter/configuration suite passes (25 tests).
- [ ] New endpoint capability documentation does not remove the need to finish the target schema/security and application migration work in the remaining plan.

Phase 6 documentation/operational-readiness deliverables are complete. Overall Supabase completion is **not** declared: see the remaining-work plan and the preceding phase reports for security, database, integration, and host-boundary blockers.
