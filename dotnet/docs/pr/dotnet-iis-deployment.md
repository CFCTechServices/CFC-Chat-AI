# Prepare .NET deployment on IIS

**Base:** `migration/dotnet` ← **Head:** `migration/c-sharp-deployment`

## Summary

This PR adds an IIS deployment path for the .NET API and the browser UI, with a route manifest that defines which requests go to .NET. Merging does not switch production traffic or repair existing document metadata.

## What changed

- Add the UI build script, edge routing configuration generator, and .NET route manifest under `deployment/`. Add edge routing tests to CI.
- Document IIS setup in the [deployment guide](../../../deployment/DOTNET-IIS.md), with separate [route inventory](../plan/routing-and-hosting-routes.md) and [preflight checks](../plan/routing-and-hosting-preflight.md).
- Update API startup, endpoint handling, Pinecone integration, and frontend request handling for the deployment path. Add internal API contract coverage.
- Track the document image MIME issue from legacy Python ingestion in a separate [remediation plan](../../../docs/plan/document-metadata-issue/legacy-document-image-mime-remediation.md). This PR does not backfill existing records.

## Verification

- The branch includes automated edge routing and API contract tests. No tests were rerun while preparing these PR notes.
- Before cutover, verify the generated routing configuration on the target IIS host and smoke-test sign-in, chat, document browsing, and admin actions through the deployed UI.
- Confirm rollback routing using the deployment guide before switching traffic. Handle legacy MIME remediation separately from deployment.
