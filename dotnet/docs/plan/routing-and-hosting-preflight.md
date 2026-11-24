# Routing and hosting: operator preflight

Companion to [the routing and hosting plan](routing-and-hosting.md). This is a worksheet, not a record of verified deployment settings or cutover approval. Use it to record the actual target host and backup references before replacing the site. Record secret source names and access owners, never secret values.

| Item | Evidence to capture | Actual / owner |
| --- | --- | --- |
| Deployment and security owners | Named reviewers and switch/rollback operator | Pending |
| IIS site | Site and application names, physical paths, HTTPS host bindings and certificate identity/expiry | Pending |
| Effective routing | Site and inherited rewrite rules, ARR proxy settings, request filtering and upload/timeout limits | Pending |
| UI release | Deployed revision, `/ui/` layout, index and asset paths, matching release backup | Pending |
| Python backend | Service name, executable, arguments, working directory, account, listener and restart policy | Pending |
| .NET backend | Existing service/listener if any; intended architecture, runtime, service account and startup directory | Pending |
| Configuration | Approved secrets source, access owners and service identity permissions | Pending |
| Persistent data | Settings, journals, staging, document roots and model assets; paths, ACLs and backup owners | Pending |
| Dependencies | Provider targets, outbound connectivity, model/vocabulary assets and optional LibreOffice | Pending |
| Rollback baseline | Protected backup reference for effective IIS config, Python service config and matching UI; restore owner | Pending |

## Confirmed development deployment defaults

Replace the existing IIS site in place using the [repeatable .NET checklist](../../../deployment/DOTNET-IIS.md). Use a self-contained .NET 10 Windows x64 release and NSSM, with a restricted `.env` outside the IIS web root. IIS serves `/ui/` and proxies only the [approved method/path pairs](routing-and-hosting-routes.md) to loopback port 5000. Use a lightweight local same-origin proxy; keep public-auth rate limiting shared and do not enable client-IP forwarding. Profile writes, lifecycle and permanent deletion default off and can be enabled explicitly. Python-only UI features are hidden. Provider writes are opt-in; document the actual target before testing them. The current unused development site does not require a scheduled maintenance window.
