# CI and deployment plan

## What runs today

The [CI workflow](../.github/workflows/ci.yml) runs on pushes, pull requests and manual dispatch. It does not need provider credentials.

| Job | Checks |
| --- | --- |
| `dotnet` (Windows, .NET 10 SDK, Node.js 22) | Tests the root `CFC.ChatAI.slnx` in Release, runs the Blazor browser-bridge unit tests, and stages the matching self-contained `win-x64` API and IIS site in the runner's temporary directory. |
| `supabase-boundary` (Ubuntu, Python 3.10) | Runs `python tools/check_supabase_boundary.py` with the standard library to check that the Supabase adapter has no application or provider project references or foreign `CFC.ChatAI.*` namespaces. |

The FastAPI/React test suite is archived and is not a CI gate. The .NET tests can skip cases requiring local model or private fixture assets. The publish step exercises the package layout and IIS config, not a running IIS site. CI does not upload a release, deploy to a VM, or switch traffic. Run the [README checks](../README.md#testing-and-ci) locally before opening a pull request. Repository branch-protection rules are managed on GitHub outside this workflow.

## Releasing today

An authorized operator stages a versioned release, runs the [.NET/IIS checklist](../deployment/DOTNET-IIS.md), and verifies routing, sign-in and provider behavior before switching traffic. Record the deployed revision, provider targets, health/smoke results, and rollback release. Keep secrets and persistent data outside publish directories. The [cutover plan](plan/internal-api-cutover.md) still governs release acceptance.

## Possible automation (not implemented)

A future Windows deployment runner could **pull** an explicitly approved, immutable release using read-only credentials; it would not require inbound management access from GitHub. Before enabling it, agree on approvers, release integrity checks, a deployment lock and maintenance policy, health and smoke gates, rollback and reconciliation procedures, and notifications. Do not automatically deploy the newest branch commit or scripts from pull requests.
