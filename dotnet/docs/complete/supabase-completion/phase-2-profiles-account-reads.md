# Supabase completion — Phase 2: profiles and account reads

**Status: adapter implemented; target-database verification is blocked by a confirmed self-service privilege-escalation risk.**

## Changes

Added focused contracts and profile DTOs under `dotnet/src/CFC.ChatAI.Supabase/Profiles/`:

- `IUserProfileRepository` provides current-user read and update operations.
- `IAdminProfileRepository` provides bounded profile listing, lookup by ID, role updates, and account-state/deletion-metadata updates.
- `Profile` models `id`, email, name/avatar, role/status, creation time, and deletion metadata.
- `UpdateOwnProfile` contains only `FullName` and `AvatarUrl`; it cannot carry role, status, or deletion metadata.

Added internal PostgREST adapters and registered both contracts in `AddSupabaseInfrastructure`:

- Self-service requests use the anon key plus the supplied access token via `SendUserAsync`; the token is attached to that request only. Reads and writes include the supplied user ID as an explicit filter, in addition to relying on the deployment's RLS policy.
- Self-service updates serialize only non-null `full_name`/`avatar_url` fields. Empty updates are rejected before an HTTP request. A missing/invisible row returns `null` for reads and updates.
- Admin list/read/update operations use the explicit service-role transport path. Listing is offset-paged with a bounded request and stable `created_at DESC, id ASC` ordering; it fetches at most one extra row to report `HasMore`.
- Admin role and account-state updates are separate focused operations. They do not decide allowed roles, self-action/superuser rules, active/inactive transitions, or other account policy.
- Wire types remain internal; persistence failures use the existing sanitized dependency exceptions.

## Verification

Ran:

```text
dotnet test dotnet/tests/CFC.ChatAI.Supabase.Tests/CFC.ChatAI.Supabase.Tests.csproj --no-restore
```

Result: **12 passed, 0 failed**. New adapter coverage verifies caller-token/anon-key use, self-service field allow-listing, profile field and timestamp mapping, missing profiles, empty-update rejection, service-role admin access, bounded pagination and continuation, and sanitized database failures. Existing transport/adapter tests also pass.

## Scope and outstanding verification

No API endpoints or application authorization policy were changed. `IIdentityGateway` token validation remains separate from profile status/role persistence; callers must still perform authorization and interpret status. These contracts make the operations available to migrated application workflows but do not themselves migrate the Python endpoints.

Sanitized profile exports have now been added under `docs/complete/supabase-completion/`: `schema.sql`, `user-rls-policies.csv`, and `privileges.csv`. They show the expected `profiles` fields and constraints, a self-row UPDATE policy whose `USING` and `WITH CHECK` expressions only constrain `id`, and effective `INSERT`/`UPDATE` privileges on every profile column for `authenticated` (also reported for `anon`). Thus the database policy/privilege combination does **not** restrict self-service writes to `full_name` and `avatar_url`: a caller with a valid user token can attempt to change their own `role`, `status`, `deleted_at`, and `deleted_by`; the signup INSERT policy likewise does not constrain those fields. The SELECT policy exposes only the caller's non-deleted row. The export also reports service-role profile read/update privileges needed by the admin adapter.

The privileges export does not identify whether the effective permissions originate from table-level or per-column grants, and the supplied files must still be confirmed as belonging to the intended target project. No live Supabase integration test or schema/grant change was made. The adapter's allow-list protects its own request path but cannot prevent direct PostgREST clients from exploiting the broader database permissions. Keep the Phase 0 verification gate open and do not use self-service profile writes in production until a reviewed database-side least-privilege fix has been applied and tested. See `docs/plan/supabase-profile-self-service-security.md` for the remediation plan.
