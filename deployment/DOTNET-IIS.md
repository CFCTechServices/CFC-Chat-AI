# Deploy the development UI and .NET API on IIS

This checklist replaces the existing development site in place. It does not run the Python deployment script or require a scheduled outage for an unused development site. Keep the old site's matching UI, IIS rules and Python service available until the new release has been checked.

## 1. Prepare the host and back up the old deployment

- [ ] On the build machine, install the .NET 10 SDK and Node.js 22+. On the Windows x64 server, install IIS Static Content, Default Document, URL Rewrite, ARR and NSSM. A separate .NET runtime or IIS .NET hosting bundle is not required: IIS proxies to a self-contained executable. Enable proxying in IIS Manager → server → Application Request Routing Cache → Server Proxy Settings. Check for server-wide or inherited rules that could still route to Python.
- [ ] Record the IIS site name, physical path, HTTPS binding/certificate, ARR settings, Python service name/start mode, and any service already using port 5000. Back up the effective IIS configuration (for example, `appcmd add backup`), the old site's physical root, and Python's service config separately. Keep these backups private. Use [the operator worksheet](../dotnet/docs/plan/routing-and-hosting-preflight.md) if helpful.

## 2. Stage the matching API and UI

From the repository root in PowerShell (adjust the base path as needed):

```powershell
$base = 'C:\cfcchat-dotnet'
$revision = git rev-parse --short HEAD
$release = Join-Path "$base\releases" "$(Get-Date -Format yyyyMMdd-HHmmss)-$revision"
$api = Join-Path $release 'api'
$site = Join-Path $release 'site'
New-Item -ItemType Directory -Path $site -ErrorAction Stop | Out-Null

dotnet publish dotnet/src/CFC.ChatAI.Api/CFC.ChatAI.Api.csproj -c Release -r win-x64 --self-contained true -o $api
if ($LASTEXITCODE -ne 0) { throw 'Publish failed' }
npm.cmd --prefix ./deployment ci --include=dev
if ($LASTEXITCODE -ne 0) { throw 'UI dependency install failed' }
node deployment/build-ui.mjs (Join-Path $site 'ui')
if ($LASTEXITCODE -ne 0) { throw 'UI build failed' }
$config = node deployment/dotnet-edge.mjs --iis
if ($LASTEXITCODE -ne 0) { throw 'IIS configuration generation failed' }
$xmlText = $config -join "`n"
[xml]$parsed = $xmlText  # Parse before staging
[IO.File]::WriteAllText((Join-Path $site 'web.config'), $xmlText)
```

The IIS physical path will be `$site`, not `$release` or `$api`. Check that `$site\ui\index.html` references `/ui/app-<hash>.js` and that the file exists alongside it. Do not copy `web/` into the IIS site: the .NET UI is precompiled and does not load Babel or raw JSX in the browser. React, ReactDOM and the Supabase client are still loaded from their CDNs. Transfer the staged release to the host if you built on another machine; Node.js and npm are only needed on the build machine. Give the IIS site identity read access only to its site directory. Do not put API binaries, `.env`, logs, staging files, journals or backups under `$site`.

If an earlier deployment shows a blank page with a Babel warning and `import declarations may only appear at top level of a module`, stage the updated release and switch IIS to its new `$site` as below. Hard-refresh `/ui/` and check the browser's Network tab: the HTML should request `/ui/app-<hash>.js` (HTTP 200), not `babel.min.js` or `.jsx` files. If the old requests persist, check the IIS physical path and site binding.

## 3. Configure the NSSM service

Create a dedicated Windows service account with **Log on as a service**. Give it read/execute on `$api` and only the required access to private configuration, models and persistent directories. Provision `$base\private` and subdirectories for settings, logs, upload temp, and (when testing lifecycle operations) the journal; restrict them to the service account and administrators. Set `AppDirectory` to `$base\private`, and create `$base\private\.env` with restricted ACLs. During service startup the API loads `.env` from that directory; variables already defined in the service environment take precedence.

Example `.env` keys (replace placeholders; no provider credentials should be checked in, logged or placed in a release):

```dotenv
DOTNET_ENVIRONMENT=Production
ASPNETCORE_ENVIRONMENT=Production
ASPNETCORE_FORWARDEDHEADERS_ENABLED=false
Api__Urls=http://127.0.0.1:5000
Api__AdminSettingsPath=C:/cfcchat-dotnet/private/settings/admin.json
ASPNETCORE_TEMP=C:/cfcchat-dotnet/private/uploads
SUPABASE_URL=https://your-project.supabase.co
SUPABASE_ANON_KEY=
SUPABASE_SERVICE_ROLE_KEY=
SUPABASE_DOCUMENTS_BUCKET=cfc-docs
SUPABASE_VIDEOS_BUCKET=cfc-videos
FRONTEND_BASE_URL=https://your-site.example/ui/
PINECONE_API_KEY=
PINECONE_INDEX_NAME=
PINECONE_NAMESPACE=
Embedding__ModelPath=C:/cfcchat-dotnet/models/model.onnx
Embedding__VocabularyPath=C:/cfcchat-dotnet/models/vocab.txt
AZURE_OPENAI_ENDPOINT=
AZURE_OPENAI_API_KEY=
AZURE_OPENAI_DEPLOYMENT=
Api__EnableDevIngest=false
Api__EnableDevDelete=false
Api__EnableProfileWrites=false
Api__EnableAccountLifecycle=false
Api__EnablePermanentAccountDeletion=false
# Api__AccountLifecycleJournalDirectory=C:/cfcchat-dotnet/private/journal
```

Use the actual site URL and configure the matching Supabase email redirect allowlist. See [model setup](../dotnet/docs/rag/embeddings.md) for the ONNX and vocabulary files and the [.NET setup section](../README.md#local-development-net-migration) for configuration-dependent routes. Azure OpenAI takes all three settings or none (extractive fallback). Legacy `.doc` ingestion requires LibreOffice; use `Document__LibreOfficePath` if `soffice` is not on the service account's PATH. Use full paths and keep persistent settings and model files across releases.

From an elevated shell on the host (set `$api` and `$base` to their host paths):

```powershell
nssm install CFCChatApi "$api\CFC.ChatAI.Api.exe"
nssm set CFCChatApi AppDirectory "$base\private"
nssm set CFCChatApi Start SERVICE_AUTO_START
nssm set CFCChatApi AppExit Default Restart
nssm set CFCChatApi AppRestartDelay 5000
nssm set CFCChatApi AppStdout "$base\private\logs\api-stdout.log"
nssm set CFCChatApi AppStderr "$base\private\logs\api-stderr.log"
nssm set CFCChatApi AppRotateFiles 1
nssm set CFCChatApi AppRotateOnline 1
nssm set CFCChatApi AppRotateBytes 10485760
nssm edit CFCChatApi  # Set the dedicated account in the Log on tab before starting
```

Do not start under the default LocalSystem identity. Set a retention policy for rotated logs (NSSM rotation does not prune history), and check service health/restart failures and disk space. If another service owns port 5000, record its configuration and stop it before starting the replacement. Run only one .NET writer for shared document/account workflows.

Profile editing and reversible account actions are opt-in; permanent deletion also requires the separate delete flag. When opting into account operations, provision the journal directory before enabling the flags. Restart the service and reload the UI after edits. Run the corresponding [profile](../dotnet/docs/plan/supabase-profile-self-service-security.md) and [account checks](../dotnet/docs/complete/internal-api/internal-api-remaining-endpoints-delivery.md) before relying on these operations. Leave development ingest and delete off.

## 4. Verify, switch IIS, and smoke test

- [ ] Run `Start-Service CFCChatApi`, then `curl.exe --fail http://127.0.0.1:5000/api/health`. Confirm restart works (`Restart-Service CFCChatApi`) and check the listener is bound only to loopback. Check `http://127.0.0.1:5000/api/health/detailed` directly for dependency diagnostics; liveness and configuration checks do not prove provider readiness. Do not publish the detailed endpoint.
- [ ] Confirm HTTPS and the intended site/app pool. For a shared host, expose only HTTPS or redirect HTTP to HTTPS before serving UI or API traffic. Enable IIS Anonymous Authentication for the site so the .NET API handles bearer authentication. Do not enable ARR response caching for authenticated API traffic. The generated IIS config allows 53,477,376 bytes (50 MiB documents plus 1 MiB HTTP overhead). If changing `Document__MaxUploadBytes`, align this IIS limit. In ARR Server Proxy Settings, pick a suitable timeout (e.g. 300 seconds for development) and verify it; server-level changes can affect other sites. Do not enable forwarded-header processing on the API. Public-auth endpoints deliberately share one rate-limit quota behind IIS. ARR may add forwarding headers, but the API must ignore them when identifying clients.
- [ ] Stop writes and drain the old backend if the backends share provider data. Change the existing site's physical path to `$site` in IIS Manager → site → Basic Settings. This switches UI and routing together. Stop Python if it is still running; if leaving .NET in place, prevent the old service from restarting automatically. Do not remove the rollback artifacts.
- [ ] With Python stopped, check `/` redirects to `/ui/`, the compiled JS and CSS load, and refreshing `/chat`, `/admin` or the password-reset page loads the UI. Unknown pages, missing assets and unsupported/wrong-method API calls must not return the UI as HTTP 200. Test both image aliases with a real signed-in browser, including a reopened chat and a URL-encoded image name. Confirm unauthenticated admin reads fail and authenticated reads work. Check effective HTTPS behavior and non-spoofable shared rate limiting.
- [ ] Confirm POST and DELETE requests to `/api/ingest/dev` and descendants fail, including trailing slash, mixed case, and percent-encoded variants. Use only bodyless requests and confirm they do not appear at the upstream rather than relying solely on the response status. Confirm `/api/videos/upload`, `/api/ingest/document`, and `/api/health/detailed` are unavailable through IIS. Verify direct access to the API port fails from another host.
- [ ] Browser-smoke sign-in, history, chat, image resolution and admin reads as applicable. Identify the configured provider targets before opting into upload/replace/delete, feedback, invite or account writes. Exercise upload limits and timeouts when doing write tests. Record skipped and unverified workflows rather than silently treating them as passed.

## Updates and rollback

For updates stage a new matching release as above. Stop writes and drain requests. Stop the service, set NSSM `Application` to the new full executable path, restart and verify loopback health, then repoint the IIS site at the new site directory. Preserve `AppDirectory`, `.env`, settings, models and journal. Stop the service to uninstall; `nssm remove CFCChatApi confirm` removes the service registration, not its private data.

If the switch fails, stop mutations and drain, stop the new service, and restore the old IIS rules **and matching UI** (normally by pointing the site back at the backed-up prior root). Restore any settings or start modes you changed and restart the matching backend. Check auth, UI and health. Reconcile ambiguous provider writes across Supabase, Storage, Pinecone and the account journal before permitting retries. Do not roll back provider changes blindly or restore global IIS backups over unrelated sites without checking impact.

## Local development

From the repository root, create a local `.env` from [`.env.example`](../.env.example) if needed and set `SUPABASE_URL`, `SUPABASE_ANON_KEY`, and `SUPABASE_SERVICE_ROLE_KEY` to the intended development project's values. Run `dotnet run --project dotnet/src/CFC.ChatAI.Api/CFC.ChatAI.Api.csproj --no-launch-profile`. The project sets the working directory to the repository root for `dotnet run`, so the API loads that `.env` without overriding existing environment variables. The published service still loads `.env` from its NSSM `AppDirectory`.

Before starting the UI, check the API directly (PowerShell):

```powershell
curl.exe --fail --silent --show-error --output NUL http://127.0.0.1:5000/api/auth/config
```

If `/api/health` works but `/api/auth/config` returns 404, check that all three Supabase settings are nonempty and restart the API. A successful response confirms the route is enabled, not that the credentials work.

In another shell run `npm --prefix ./deployment ci --include=dev` (PowerShell: `npm.cmd`), then `node deployment/dotnet-edge.mjs` and open `http://127.0.0.1:8080/ui/`. The local edge builds the same UI assets in memory at startup; restart it after editing `web/`. Stop both with Ctrl+C. The proxy and generated IIS rules share [`routes-dotnet.manifest`](routes-dotnet.manifest); neither falls back to Python. Named browser routes get the entry page, not arbitrary URLs or missing assets. A provider-dependent route can still return 404 if its prerequisites are missing. A local 502 usually means the API is not listening on port 5000.

After installing the UI dependencies, run `node --test deployment/dotnet-edge.test.mjs` to check the staged UI bundle and the local edge against a fake upstream. These tests do not verify IIS URL normalization, request filtering, headers or ARR. See the [route inventory](../dotnet/docs/plan/routing-and-hosting-routes.md) for what the edge exposes.
