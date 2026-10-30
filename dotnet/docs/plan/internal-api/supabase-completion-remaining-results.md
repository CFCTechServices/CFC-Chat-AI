# Supabase completion — remaining-plan execution results

**Assessment: not complete; production go-live is blocked.** This records the local work completed against `./supabase-completion-remaining-plan.md` and the evidence that still requires project-owner/database access. It does not certify a live Supabase environment.

## Work completed in this execution

- Ran `dotnet test dotnet/CFC.ChatAI.slnx --no-restore`: **105 passed, 0 failed, 0 skipped** across the .NET solution. The build emitted nullable-dereference warnings in `dotnet/tests/CFC.ChatAI.Pinecone.Tests/PineconeServiceTests.cs` (lines 23 and 43); they did not fail the run.
- Ran `python dotnet/tools/check_supabase_boundary.py`: passed. It verifies that the Supabase project has no `ProjectReference` entries and its C# source does not import or declare non-Supabase `CFC.ChatAI.*` namespaces.
- Added that boundary check to `.github/workflows/ci.yml` so it runs in CI.
- Ran `git diff --check`: passed.
- Reviewed the active endpoint source and searched for Supabase REST/Auth/Storage client paths outside the Supabase package. The detailed health probe already uses `ISupabaseHealthGateway`; no direct Supabase SDK/REST/Auth/Storage call path outside the package was found by the source search. Pinecone/model probes remain host-owned.
- Confirmed `PATCH /api/profile/me` and `POST /api/profile/complete` are not mapped. Keep them disabled until the database security gate passes.
- Updated `../supabase-completion-remaining-plan.md` to mark the health capability, active-endpoint direct-call review, and now-CI-enforced architecture boundary complete, and corrected its current-status paragraph accordingly.

## Gate-by-gate status

### Priority 0 — security and deployment identity: **BLOCKED**

The committed profile exports remain serious evidence: `privileges.csv` lists `INSERT` and `UPDATE` on profile columns for both `anon` and `authenticated`, including `role`, `status`, `deleted_at`, and `deleted_by`; `user-rls-policies.csv` limits row identity with `auth.uid() = id` but does not constrain writable columns. This is not a safe database boundary.

No target-project identity confirmation, source ACL/role-inheritance inspection, profile-trigger/function review, approved migration, or direct PostgREST negative tests were performed. Do not enable self-service writes. Apply the detailed workflow in `../supabase-profile-self-service-security.md` only after the database owner confirms the intended non-production target and signup path.

### Priority 1 — target schema, RLS, RPC, and Storage: **BLOCKED**

[`schema.sql`](../../complete/supabase-completion/schema.sql) is explicitly contextual, while the supplied privilege and RLS exports are profile-specific. They do not establish the active environment or prove the live schema, grants, FK behavior, policies, RPC definitions, vector dimension/index, or Storage configuration. No live database/PostgREST/Storage requests or concurrency/cross-user tests were run. Every target-project verification item in Priority 1 remains open.

### Priority 2 — authorization and API boundary: **PARTIALLY COMPLETE**

- **Complete locally:** detailed health uses the focused Supabase health gateway; the Supabase package boundary check passes and is enforced by CI; no direct SDK/REST/Auth/Storage path outside the package was found in the source search.
- **Open:** application-owner decisions on token-validation credential mode, recovery key mode, and caller-token/RLS versus service-role chat/message/feedback access. No approval or end-to-end tests were supplied, so these access modes are not declared equivalent or approved.

### Priority 3 — consuming application behavior and integration tests: **OPEN**

No endpoint business-policy implementation or live integration test was added as part of this execution. Profile role/status authorization, invitation lifecycle and races, account lifecycle/reconciliation, chat/feedback ownership and validation, multi-request partial-failure behavior, and disposable-project integration tests remain the responsibility of the consuming application and its release gates. The 105 passing tests are local .NET tests and do not establish database/RLS/RPC/Storage behavior.

### Priority 4 — scale and rollout: **OPEN**

No representative corpus benchmark, Supabase retry/rate-limit policy review, deployment secret-manager/HTTPS/alerting verification, or go/no-go review against the original completion criteria was performed. Keep all of these open.

## Release decision and next actions

**No-go for production readiness or traffic cutover.** The local test suite and static architecture check do not close the live security or integration gates. In particular, the supplied profile privilege evidence is unresolved, and no live target-project evidence was collected.

1. Have the project/database owner confirm a non-production Supabase project and review the profile ACL sources, role inheritance, RLS, and creation trigger/function.
2. Apply a reviewed least-privilege profile migration in that environment; run direct authenticated and anonymous negative tests and verify service-role admin operations.
3. Complete the target schema/RLS/FK/RPC/Storage checks and the authorization-mode decisions in the remaining-work plan.
4. Only then enable the blocked self-profile routes and complete application-level and disposable-project integration coverage.
5. Complete the scale/operations and original-plan go/no-go evidence before any production declaration.

No credentials, JWTs, signed URLs, user PII, or unrestricted database dumps were recorded or used in this report.
