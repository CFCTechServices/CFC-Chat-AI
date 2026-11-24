# Deploy Blazor and the .NET API on IIS

IIS runs the Blazor Interactive Server app (`dotnet/src/CFC.ChatAI.Web`) through the ASP.NET Core Module. The Blazor server calls `CFC.ChatAI.Api` directly at `http://127.0.0.1:5000` using the signed-in user's bearer token. IIS/ARR also forwards the previously approved public API routes to that listener; the [IIS config](iis-web.config) is the allowlist. IIS hands page routes, static assets and `/_blazor` (including WebSocket traffic) to Blazor. There is no Python service, Node.js build, React bundle, or CORS change for this deployment.

## Local development

Install the .NET 10 SDK. For local password recovery, set the following in the repository root `.env` before starting the API (copy `.env.example` to `.env` first if needed; do not overwrite an existing file):

```dotenv
PASSWORD_RESET_REDIRECT_URL=http://localhost:5077/reset-password
```

Allow this URL in Supabase's redirect settings. This setting is independent of Python's `FRONTEND_BASE_URL`. Existing environment variables take precedence over `.env`; restart the API after changing `.env`. Existing .NET deployments must migrate their reset URL from `FRONTEND_BASE_URL` to `PASSWORD_RESET_REDIRECT_URL`; the .NET API no longer reads the old name.

Run these from the repository root in two PowerShell terminals:

```powershell
# Terminal 1: API (default http://127.0.0.1:5000)
dotnet run --project dotnet/src/CFC.ChatAI.Api/CFC.ChatAI.Api.csproj
```

```powershell
# Terminal 2: Blazor (http://localhost:5077)
dotnet run --project dotnet/src/CFC.ChatAI.Web/CFC.ChatAI.Web.csproj --launch-profile http
```

Open [http://localhost:5077/](http://localhost:5077/) or the old `/ui/` bookmark; both redirect to `/login`. The API loads the repository root `.env` without overriding environment variables. Blazor does not load that file or need provider credentials. If the API is on another port, set `$env:Backend__BaseUrl` in terminal 2 to its root URL before starting Blazor. The URL must be HTTPS or loopback HTTP. To test with local HTTPS, trust the .NET developer certificate (`dotnet dev-certs https --trust`), use the `https` launch profile, set `PASSWORD_RESET_REDIRECT_URL` to `https://localhost:7011/reset-password` in `.env`, and allow the HTTPS callback URLs in Supabase.

`curl.exe --fail http://127.0.0.1:5000/api/health` checks API liveness without provider credentials. If `/api/auth/config` is 404 on the API, check `SUPABASE_URL`, `SUPABASE_ANON_KEY` and `SUPABASE_SERVICE_ROLE_KEY` and restart it. Chat, feedback submission and document writes also require Pinecone and the embedding assets. See [.NET local setup](../README.md#local-development-net-migration) for the provider settings. Use disposable development providers for write tests. Stop each process with Ctrl+C.

## 1. Prepare the Windows x64 host and preserve rollback

- [ ] Install the **.NET 10 SDK** on the build machine. On the host, install IIS with **WebSocket Protocol**, **.NET 10 Hosting Bundle** (for ASP.NET Core Module V2 even though the apps are self-contained), **URL Rewrite**, **ARR**, and **NSSM**. If the Hosting Bundle was installed before IIS, repair it after installing IIS and restart IIS/WAS after module installation. Enable ARR proxying in IIS Manager → server → Application Request Routing Cache → Server Proxy Settings. Check for global rules, server-level caching, and other inherited settings before the switch.
- [ ] Record the site's current HTTPS binding/certificate, app pool, physical root, rewrite rules, ARR settings and any service already using port 5000. Back up the effective IIS config (e.g. `appcmd add backup`), prior site files and backend service config/start mode. Keep the old IIS rules and UI together with their matching backend. See the [operator worksheet](../dotnet/docs/plan/routing-and-hosting-preflight.md).
- [ ] Use a dedicated **64-bit**, **Integrated** app pool with **No Managed Code** and one worker process. Enable Anonymous Authentication; the app and API handle sign-in and authorization. Provision [persistent ASP.NET Core Data Protection keys for the pool](https://learn.microsoft.com/aspnet/core/host-and-deploy/iis/#data-protection) (for example, load the app-pool user profile) and retain them across releases. Plan for Blazor circuits to reconnect or reload when IIS recycles the worker.

## 2. Stage a matching release

From the repository root in PowerShell:

```powershell
$base = 'C:\cfcchat-dotnet'
$revision = git rev-parse --short HEAD
$release = Join-Path "$base\releases" "$(Get-Date -Format yyyyMMdd-HHmmss)-$revision"
.\deployment\Publish-DotnetIis.ps1 -ReleasePath $release
$api = Join-Path $release 'api'
$site = Join-Path $release 'site'
```

The command publishes both projects self-contained for `win-x64` and stages the [IIS config](iis-web.config) as `$site\web.config`. It refuses to reuse an existing release directory and does not modify IIS or services. Confirm `$api\CFC.ChatAI.Api.exe`, `$site\CFC.ChatAI.Web.exe`, `$site\web.config` and `$site\wwwroot\_framework\blazor.web*.js` are present. Transfer both directories to the host if building elsewhere. The IIS site's physical path is **`$site`** (not `$release` or `$site\wwwroot`). Do not copy `web/` into it. Give the app-pool identity read/execute on `$site`, not access to the API's credential files, models, logs or private data.

## 3. Configure the loopback API service

Use a dedicated Windows service account with **Log on as a service** and read/execute on `$api`. Provision `$base\private` with subdirectories for settings, logs and upload temp; provision a journal directory for lifecycle operations only if needed. Give the service account only the required access to private configuration, model files and persistent data. Set NSSM `AppDirectory` to `$base\private`; the API loads `.env` from there at startup, with explicitly configured service environment variables taking precedence. Store `.env` outside the release and IIS site, and restrict its ACL.

Example `$base\private\.env` (replace placeholders, never commit real credentials):

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
PASSWORD_RESET_REDIRECT_URL=https://your-site.example/reset-password
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

Use the site's actual HTTPS origin. In Supabase, set the site URL and allow the actual password-reset URL (`/reset-password`) and signup confirmation URL (`/login?signup=confirm`). The browser loads the pinned Supabase JS SDK from `cdn.jsdelivr.net` and talks to Supabase Auth and Storage; it must be able to reach those origins. See [embedding setup](../dotnet/docs/rag/embeddings.md) for the model and vocabulary. Azure OpenAI takes all three settings or none (extractive fallback). Legacy `.doc` ingestion requires LibreOffice (`Document__LibreOfficePath` if it is not on the service account's PATH).

From an elevated PowerShell on the host, with `$api` and `$base` set to the host paths (first install only):

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

Do not run as LocalSystem. Prune rotated logs according to a retention policy. If updating an existing `CFCChatApi`, use the update steps below instead of reinstalling. If another service already owns port 5000, record its config and stop it before starting the replacement. Run only one .NET API writer for shared document/account workflows.

Leave development ingest and delete off. Profile editing and account lifecycle actions are disabled by default; permanent deletion also requires the separate delete flag. Before enabling them, run the corresponding [profile](../dotnet/docs/plan/supabase-profile-self-service-security.md) or [account checks](../dotnet/docs/complete/internal-api/internal-api-remaining-endpoints-delivery.md). Set the private journal path before enabling lifecycle operations. Restart the API and reload the browser after changing flags.

## 4. Switch IIS and verify

- [ ] Start `CFCChatApi` and run `curl.exe --fail http://127.0.0.1:5000/api/health`. Confirm `Restart-Service CFCChatApi` works. Use `/api/health/detailed` on loopback only for diagnostics; health is not proof of provider readiness. Verify port 5000 cannot be reached from another host.
- [ ] Serve the Blazor app at the **root of the HTTPS site**, not as a `/ui` sub-application. Use HTTPS only or redirect HTTP at IIS before serving app/API traffic. Ensure the Web app runs in `Production`. Keep ARR response caching off for authenticated API routes. Allow sufficient ARR proxy timeout (e.g. five minutes, after checking other sites); Blazor's direct API client also times out after five minutes. Do not enable forwarded-header processing on the API. Public auth calls share a loopback rate limit, regardless of client-supplied forwarding headers.
- [ ] If changing `Document__MaxUploadBytes`, set it for **both** the API service and the Blazor app (app-pool environment or its non-secret configuration) and adjust IIS `maxAllowedContentLength` in the staged `web.config` for direct API multipart requests. Defaults are 50 MiB per file and 51 MiB (53,477,376 bytes) including multipart overhead. Blazor sends browser uploads in chunks over the circuit. If changing the API listener, keep it loopback-only and update both `Backend__BaseUrl` for Blazor and the IIS allowlist's upstream address.
- [ ] Pause writes and drain in-flight requests if backends share provider data. Switch the site's app pool and physical path to the staged Blazor site in IIS Manager → site → Basic Settings. Stop Python and prevent it from restarting automatically after verifying the switch. Preserve rollback files and config.
- [ ] With Python stopped, check `/` and `/ui/` redirect to `/login`, the Blazor framework script and CSS load, and refreshing `/chat`, `/chat/{sessionId}`, `/admin`, `/history`, `/settings` and `/reset-password` works. Confirm in the browser Network tab that `POST /_blazor/negotiate` succeeds and `wss://.../_blazor` upgrades (HTTP 101). A missing asset or unknown page should not return HTTP 200. No React/JSX requests should appear.
- [ ] Through IIS, confirm `GET /api/health` succeeds and unauthenticated admin API calls fail when configured. Verify unsupported methods, `/api/health/detailed`, `/api/ingest/document`, `/api/videos/upload`, and `/api/ingest/dev` plus descendants do not reach the API. Include bodyless POST/DELETE checks for mixed case, trailing slash and encoded forms of the dev routes; confirm at the upstream, not just by response code. See the [route inventory](../dotnet/docs/plan/routing-and-hosting-routes.md).
- [ ] In a browser, test sign-in and email links, history and chat, reopened answers with signed images, both supported image aliases with a real bearer token, and admin reads. Test an encoded document/image path where applicable. Identify the configured provider targets and opt in explicitly before testing uploads, replacement, deletion, feedback, invitations or account actions. Check file sizes and timeouts. Record skipped checks.

If the old UI still loads, check the IIS binding and physical path. A 500.19 may indicate missing/locked IIS sections; a 500.30 points to Web startup (check the Application event log). If the page loads but sign-in configuration is unavailable, check loopback API health and `/api/auth/config` and confirm `Backend__BaseUrl`. If Blazor stays on “Checking access…” or reconnects repeatedly, check WebSocket Protocol and the `/_blazor` requests before debugging provider credentials.

## Updates and rollback

Stage a new matching release. Pause writes and drain requests. Stop `CFCChatApi`, set NSSM `Application` to the new full executable path, restart and verify loopback health, then repoint IIS to the new site directory and recycle the pool. Preserve `AppDirectory`, `.env`, settings, model assets, journal, and the app pool's Data Protection keys. `nssm remove CFCChatApi confirm` removes only the service registration.

On failure, stop mutations and drain requests, stop the new service, restore the prior IIS physical path, rules and app pool, and start the matching backend with its saved configuration. Verify health, UI and sign-in. Reconcile ambiguous writes across Supabase, Storage, Pinecone and the account journal before allowing retries. Do not blindly undo provider writes or restore server-wide IIS backups over other sites.

## Automated checks

```powershell
dotnet test dotnet/CFC.ChatAI.slnx
# Optional browser-bridge unit tests (Node 22+, no npm install)
node --test dotnet/tests/CFC.ChatAI.Web.Tests/frontend-browser.test.mjs dotnet/tests/CFC.ChatAI.Web.Tests/supabase-auth.test.mjs
```

The Web tests cover Blazor entry points, framework assets, SignalR negotiation, the IIS rewrite policy and calls to a real loopback .NET API without live providers. CI also stages the publish outputs. These checks do not validate IIS URL normalization, WebSocket upgrades through IIS, live provider writes or the target site's configuration; run the host checks above.
