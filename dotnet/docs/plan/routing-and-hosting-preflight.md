# Routing and hosting: operator preflight

Companion to [the routing and hosting plan](routing-and-hosting.md). This is a worksheet, not a record of verified deployment settings or cutover approval. Use it to record the actual target host and backup references before replacing the site. Record secret source names and access owners, never secret values.

| Item | Evidence to capture | Actual / owner |
| --- | --- | --- |
| Deployment and security owners | Named reviewers and switch/rollback operator | Pending |
| IIS site | Site and application names, physical paths, HTTPS host bindings and certificate identity/expiry | Pending |
| Effective routing | Site and inherited rewrite rules, ARR proxy settings, request filtering and upload/timeout limits | Pending |
| UI release | Deployed revision, Blazor publish directory, app pool and WebSocket support, matching release backup | Pending |
| Python backend | Service name, executable, arguments, working directory, account, listener and restart policy | Pending |
| .NET backend | Existing service/listener if any; intended architecture, runtime, service account and startup directory | Pending |
| Configuration | Approved secrets source, access owners and service identity permissions | Pending |
| Persistent data | Settings, journals, staging, document roots and model assets; paths, ACLs and backup owners | Pending |
| Dependencies | Provider targets, outbound connectivity, model/vocabulary assets and optional LibreOffice | Pending |
| Rollback baseline | Protected backup reference for effective IIS config, Python service config and matching UI; restore owner | Pending |

## Confirmed development deployment defaults

Use the [repeatable .NET checklist](../../../deployment/DOTNET-IIS.md). Publish a self-contained .NET 10 Windows x64 release with separate Blazor and API directories. IIS hosts Blazor at the site root with ASP.NET Core Module V2 and WebSockets, and ARR proxies only the [approved method/path pairs](routing-and-hosting-routes.md) to loopback port 5000. The API runs under NSSM with a restricted `.env` outside the IIS site. Blazor calls the API directly in development and on IIS. Keep public-auth rate limiting shared and do not enable client-IP forwarding. Profile writes, lifecycle actions and permanent deletion are opt-in, as are provider write tests. Record the actual target before testing writes.
