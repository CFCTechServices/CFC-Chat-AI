# Supabase profile self-service privilege remediation plan

## Status and scope

**Status: planned; no database changes have been applied.** This plan addresses the profile privilege-escalation risk found in the supplied exports. It is a database security remediation, not an adapter feature or a substitute for application authorization.

Evidence is in `docs/complete/supabase-completion/`:

- `schema.sql` defines `public.profiles`, including `id`, `email`, `full_name`, `avatar_url`, `role`, `status`, `created_at`, `deleted_at`, and `deleted_by`.
- `user-rls-policies.csv` shows policies applied to `{public}`: INSERT checks only `auth.uid() = id`; UPDATE uses and checks only `auth.uid() = id`; SELECT checks the same user ID and `deleted_at IS NULL`.
- `privileges.csv` reports effective INSERT and UPDATE privileges on every profile column for `authenticated` and `anon`. It also reports profile read/update privileges for `service_role`.

Together, the exports mean that an authenticated caller can attempt to change privileged columns on their own profile, including `role` and account-state/deletion metadata. If profile insertion is available in the signup flow, its policy likewise does not stop the caller from supplying privileged values. The .NET self-profile adapter only serializes `full_name` and `avatar_url`, but that allow-list does not constrain direct PostgREST clients. The exports do not identify the project/environment or whether effective grants originate from table-level ACLs, column-level ACLs, inherited roles, or `PUBLIC`; verify those before authoring a migration.

## Security goals

1. Signed-in users can read only their own non-deleted profile, consistent with the current intended behavior.
2. A signed-in user can update only approved self-service fields (`full_name`, `avatar_url`). They cannot set or modify `role`, `status`, `deleted_at`, `deleted_by`, `email`, `id`, or `created_at` through PostgREST.
3. Anonymous clients cannot insert or update profiles.
4. Profile creation, if client initiated, cannot let the caller choose a privileged role/status or forge profile identity fields. Prefer a trusted Auth-trigger path when that matches the existing signup architecture; otherwise explicitly constrain allowed insert columns and validate identity fields against the authenticated Auth user.
5. Trusted service-role admin reads and updates continue to work. Application-level admin checks and role-transition policy remain outside the repository/database ACL.
6. RLS remains enabled; grants and policies are both tested. The adapter field allow-list remains as defense-in-depth.

## Work plan

### 1. Confirm the target and inspect the full security boundary

- Confirm that all three supplied exports came from the intended Supabase project/environment and record when they were captured (do not record secrets).
- Inspect `pg_policies`, table ACLs, column ACLs, role memberships/inherited privileges, and `PUBLIC` grants for `public.profiles`. The current CSV lists effective column privileges but does not reveal their source.
- Inspect all triggers/functions that create or update profiles, including function owner, `SECURITY DEFINER` status, `search_path`, and whether user-controlled role/status values are accepted.
- Confirm whether signup creates a profile through a trusted trigger or through a client INSERT. Do not remove a required signup path until its replacement is tested.
- Confirm the application behavior requiring self-read and self-update; keep the existing soft-deleted-row visibility rule unless an explicit product/security decision changes it.
- Treat the privilege as potentially exploitable for the exposure period. Review current elevated profile roles/status against trusted administrative records. The schema shown has no audit history, so do not claim past changes can be reliably attributed without other logs.

### 2. Design a reviewed least-privilege migration

Use a reviewed, version-controlled SQL migration, applied first to a non-production project. Do not run a generic grant script without checking the actual ACL sources and signup flow.

Recommended permission intent:

- Revoke INSERT and UPDATE from `anon`.
- Revoke table-level and any per-column INSERT/UPDATE privileges from `authenticated`; revoke relevant grants inherited through `PUBLIC` or other roles as needed.
- Grant `authenticated` UPDATE only on `full_name` and `avatar_url`.
- For profile INSERT, prefer revoking client INSERT if a trusted trigger already creates the row. If client INSERT is required, grant only the strictly necessary columns and enforce that `id` and any supplied email correspond to the authenticated Auth user. Keep `role` and `status` database-controlled/defaulted; do not allow the caller to supply them. Confirm defaults/trigger behavior rather than assuming omitted fields are safe.
- Preserve `authenticated` SELECT only as needed for the existing self-row policy. A row policy does not hide individual columns, so separately decide whether exposing role/status/deletion fields on the user's own non-deleted row is acceptable; column-level SELECT restrictions may be considered if the client does not need them.
- Preserve the service-role privileges needed by admin profile list/read/update operations. Verify this with the actual service-role path after changes.
- Keep role/status CHECK constraints and RLS enabled. The self UPDATE policy must continue to require the authenticated user's own `id` in both `USING` and `WITH CHECK`.

PostgreSQL table-level grants and column-level grants are distinct. Revoking a table-level privilege alone may not remove separately granted column privileges; the migration must revoke every effective write path identified during inspection, then add only the required column grants. Review role inheritance and `PUBLIC` ACLs too.

### 3. Validate in non-production

Apply the migration and inspect the resulting ACLs/policies from the database catalog. Exercise PostgREST directly with test credentials—not just through the .NET adapter:

- Authenticated user can read their own non-deleted profile and cannot read or mutate another user's row.
- Authenticated user can update `full_name` and `avatar_url`.
- Attempts to update `role`, `status`, `deleted_at`, `deleted_by`, `email`, `id`, or `created_at` fail or leave protected values unchanged; explicitly test both a single protected field and a mixed allowed/protected-field PATCH.
- Signup/profile creation follows the intended path; a user cannot insert or set an `admin`/`superuser` role, inactive/deleted status, or forged identity fields.
- Anonymous INSERT/UPDATE attempts fail.
- Service-role admin profile reads and role/status/deletion metadata updates continue to succeed.
- Existing application flows for profile completion and profile status/role reads still behave correctly, including the hidden deleted-profile behavior.

Run adapter/unit tests as well. They should continue proving that `UpdateOwnProfile` contains only allowed fields, but those tests do not replace direct-PostgREST integration tests.

### 4. Roll out and monitor

- Get database/security-owner approval for the migration and test results.
- Back up/export the pre-change ACL/policy definitions and deploy through the normal migration process.
- Re-run the direct API checks in the deployed environment using test accounts and confirm service-role admin operations.
- Review elevated roles before rollout and monitor failed/abnormal profile writes after rollout. If profile privilege changes are not audited, enable appropriate audit logging before relying on future attribution.
- Do not roll back to the vulnerable grants merely to restore a broken signup flow. Diagnose and repair the signup path while preserving least privilege.

## Completion criteria

- [ ] Target project/environment confirmed; full effective ACLs, role inheritance, policies, and profile triggers reviewed.
- [ ] Reviewed migration removes all unauthorized `anon`/`authenticated` write paths and grants only intended self-service update columns.
- [ ] Profile creation cannot set privileged fields or forge identity, without breaking signup.
- [ ] Direct authenticated and anonymous PostgREST negative tests pass for protected writes; allowed self-edits and own-row reads pass.
- [ ] Service-role admin profile reads/updates pass after the migration.
- [ ] Existing elevated accounts reviewed against trusted records; migration and verification results documented.
- [ ] Phase 0 profile security gate and Phase 2 target-database verification are explicitly closed by the project owner.
