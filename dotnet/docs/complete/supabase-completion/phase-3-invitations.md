# Supabase completion — Phase 3: invitations

**Status: invitation persistence implemented and unit-tested; target-database and application-layer verification remain outstanding.**

## Changes delivered

Added `dotnet/src/CFC.ChatAI.Supabase/Invitations/`:

- `Invitation` models persisted values including nullable `created_by`, `used_by`, registration state, creation timestamp, and expiry timestamp.
- `CreateInvitation` contains only `email` and `created_by`; database-owned ID/timestamps/registration/expiry fields cannot be supplied through this insert contract.
- `IInvitationRepository` exposes latest-by-email and latest-unregistered-by-email lookups, insert, delete-by-ID, and delete-by-email.
- `InvitationConflictException` is a sanitized typed conflict for the known active-invitation uniqueness rule.

`SupabaseInvitationRepository` is an internal PostgREST adapter, registered as a singleton in `AddSupabaseInfrastructure`. Like the Python invitation code, it uses the service-role path; authorization must happen in the trusted application workflow. Email and ID filter values are URL escaped. Reads are bounded to one row and order latest-first (`created_at DESC`, stable `id ASC` tie-break). Inserts request and return the persisted row, including the database-generated expiry. Deletes are individually scoped; absent rows are no-ops.

`IAdminProfileRepository.GetByEmailAsync` was added to support the profile-existence check used by invitation creation. The lookup is service-role scoped and returns `null` for an absent profile.

The transport now reads at most 8 KiB of an error response for internal classification. Only a response with SQLSTATE `23505` and the known constraint/index name `idx_one_active_invite_per_email` maps to `InvitationConflictException`; provider response bodies, email addresses, and SQL details are not exposed in the exception. Other uniqueness failures remain ordinary sanitized dependency failures. Service-role and anon/user-token authorization behavior otherwise remains unchanged.

No invitation expiry, eligibility, status, duplicate-profile, or caller authorization policy was put in the repository. Application code must compare timestamps against an injected UTC clock and interpret `active`, `expired`, `registered`, or `none`. Expired-invite deletion followed by insertion is two independent HTTP operations and is **not atomic**; the database uniqueness rule is expected to arbitrate concurrent inserts.

## Verification

Ran:

```text
dotnet test dotnet/tests/CFC.ChatAI.Supabase.Tests/CFC.ChatAI.Supabase.Tests.csproj --no-restore
```

Result: **16 passed, 0 failed**. Added adapter tests cover email-escaped latest/unregistered queries, nullable/persisted-field mapping, insert payload allow-list and generated expiry return, known unique-conflict translation with sanitized error text, delete-by-ID/delete-by-email filters, and profile lookup by email. Existing tests continue to cover service/user credential isolation and sanitized errors.

Updated `dotnet/docs/supabase/README.md` with the new contract map, privilege boundary, persistence semantics, and remaining prerequisites.

## Verification blockers and exit status

The supplied `docs/complete/supabase-completion/schema.sql` shows an `invitations.expires_at` default of `now() + 7 days`, nullable `is_registered`/timestamps, and the referenced columns. It does **not** establish that it belongs to the intended deployment project, nor does it show the partial unique index, its predicate/name, invitation grants, or whether the target database currently returns generated values as expected. Phase 0 explicitly records those facts as unverified. Confirm the active invite index name/predicate and default directly in the target project before relying on conflict translation or launching this implementation.

No live Supabase integration test was run. In particular, concurrent insert arbitration, service-role grants, delete permissions, and the generated expiry default remain unverified against deployment.

There is no migrated C# invitation application service in this repository, so deterministic-clock tests of the `active`/`expired`/`registered`/`none` state machine, check-email responses, profile-already-exists handling, and workflow behavior after a delete/insert partial failure cannot yet be implemented at the application layer without introducing business policy into the persistence package. The exposed contracts support those workflows, but the phase's application-level test requirement and full exit criteria remain open for the consuming application phase.
