# Chunk 1: runtime and service diagnostics

Implemented in `src/CFC.ChatAI.Api`:

| Public GET route | Behavior |
|---|---|
| `/api/health` | Python-identical `ok`, `message`, `version`; no dependency calls. |
| `/api/health/detailed` | HTTP 200 even when degraded; `ok`, `version`, `message`, and four `services` entries. |
| `/api/auth/config` | Exactly `supabaseUrl` and `supabaseKey` (camelCase); missing values are JSON null. Only the configured anon/public key is eligible for exposure. |
| `/api/visibility/vector-store` | Existing document-index statistics; Python snake_case response including nullable `dimension`, `index_fullness`, and `error`. Failure is HTTP 500 with `{"detail":"Failed to fetch vector store stats"}`. |

## Configuration and DI

The API loads `.env` before resolving service configuration. Set `DOTENV_PATH` to an explicit file path, or it finds the nearest `.env` in the content-root directory or its parents (then the executable directory or its parents). This supports both running from `dotnet/src/CFC.ChatAI.Api` and publishing into the repository's `dotnet-publish` directory. An explicitly configured missing file fails startup; an absent auto-discovered file is allowed for environment-only deployments.

Dotenv values are configuration defaults: `appsettings*.json`, environment variables, and command-line configuration take precedence. Loading does not mutate process environment variables. Both Python-style names and hierarchical keys using `__` are supported in `.env`. Never commit secrets to appsettings or copy `.env` into public/static output. Restart the API after changing `.env`.

Diagnostics accept the existing Python environment names as well as hierarchical ASP.NET keys (hierarchical keys take precedence):

| ASP.NET key (`:` becomes `__` in environment variables) | Legacy environment key |
|---|---|
| `Pinecone:ApiKey` | `PINECONE_API_KEY` |
| `Pinecone:Documents:IndexName` | `PINECONE_INDEX_NAME` |
| `Supabase:Url` | `SUPABASE_URL` |
| `Supabase:AnonKey` | `SUPABASE_ANON_KEY` |
| `Supabase:ServiceRoleKey` | `SUPABASE_SERVICE_ROLE_KEY` |
| `AzureOpenAI:ApiKey` | `AZURE_OPENAI_API_KEY` |
| `AzureOpenAI:Endpoint` | `AZURE_OPENAI_ENDPOINT` |
| `AzureOpenAI:Deployment` | `AZURE_OPENAI_DEPLOYMENT` |
| `Embedding:ModelName` | `EMBED_MODEL_NAME` |

Index name defaults to `cfc-rag-chatbot`; model display name defaults to `sentence-transformers/all-MiniLM-L6-v2`. API version uses `API_VERSION`, then `ApiVersion`, then `1.0.0`.

`RuntimeOptions` and `RuntimeDiagnostics` are registered with DI independently of the full `AddPineconeService` registration. Missing credentials must not prevent this host from starting. Diagnostic routes need neither model export files nor write-capable RAG initialization.

`Diagnostics:RequestTimeoutSeconds` defaults to 5 and must be positive and at most 60 (fractional values supported). Pinecone and Supabase health checks run concurrently with independent deadlines. Vector visibility has one deadline spanning discovery, stats retrieval, and body parsing. All network requests honor request cancellation; there are no retries. The named `CFC.Diagnostics` client redacts headers and disables redirects to prevent credential forwarding.

## Compatibility and intentional safety differences

- The basic health message no longer has the scaffolding's `(C#)` suffix.
- Pinecone health calls `GET https://api.pinecone.io/indexes`; Supabase health calls `GET /rest/v1/profiles?select=id&limit=1` using the service-role key.
- Azure status remains **configuration-only**, just like Python: missing key/endpoint is `not_configured`; missing deployment is `degraded`; complete configuration is `ok`. This is not an assertion of live AI connectivity and performs no paid model call.
- Python's embedding check only imports sentence-transformers. C# checks the deployed ONNX Runtime and native library availability. It does **not** load/export/download a model or assert model-file readiness. Runtime failure is `error` with `ONNX Runtime not available`; the same `embedding_model`/`model` fields remain.
- Upstream errors are replaced with fixed safe details, never raw exception strings or response bodies. Timeout details are `<Service> health check timed out`.
- The auth-config endpoint never falls back to the service-role key. It also suppresses an accidentally configured matching service-role key, `sb_secret_` key, or JWT with `role=service_role`.
- Visibility discovers the existing index and never auto-creates one from a public GET (unlike Python's lazy initialization). Provision missing indexes separately. Discovered hosts must be HTTPS, port 443, under `.pinecone.io`, without credentials, query, fragment, or non-root path. Supabase diagnostic URLs must be HTTPS without URL credentials/query/fragment.
- Explicit zero totals take precedence over alternate fields and namespace sums. An empty namespace name becomes `default`. Namespace counts follow Python's `vectorCount` field. Fullness mirrors Python's `indexFullness or index_fullness` behavior, including null when only camelCase zero exists.
- Optional service-specific fields are omitted; Pydantic response-model nullable fields are emitted as null. External dictionary keys are no longer globally renamed.

## Verification and cutover

Run from the repository root:

```powershell
dotnet test dotnet/CFC.ChatAI.slnx --no-restore
dotnet run --project dotnet/src/CFC.ChatAI.Api
```

Contract tests in `dotnet/tests/CFC.ChatAI.Api.Tests` use an in-process ASP.NET host and stub HTTP transport: no live credentials, index mutation, or paid calls. RAG live-service/model-parity tests remain opt-in.

Before deploying `deployment/iis-web.config`:

1. Ensure the repository `.env` is readable by the .NET service account, or set `DOTENV_PATH` to its location. Deployment environment variables can override file values.
2. Verify all four routes directly on `http://127.0.0.1:5000`. With real credentials, compare Pinecone counts/namespaces and Supabase connectivity against Python. A fully configured detailed check should be `ok=true`.
3. Publish/start the .NET host and apply the four exact GET-only `DotNet_*` IIS rules above the Python catch-all. No auth/health/visibility prefix is claimed wholesale.
4. Repeat the checks through IIS, including existing Python-only routes such as `/api/admin/users`. The configuration also contains the five auth/profile rules documented in [auth/profile migration](auth-profile.md); review those operational gates before applying the complete file. Confirm query strings are preserved. On a dev VM, stopping .NET should yield 502 on migrated routes, proving proxy routing.
5. Roll back any route by setting its rule `enabled="false"`; requests then fall through to Python on port 8000. Keep Python implementations until the observation period ends.

The rules in the repository are updated; applying them to a running IIS deployment and live-service validation are separate operational steps.
