# Canton.Ledger.Kernel

The transport-neutral client kernel for Canton participant nodes: the `Authentication`, `Telemetry`, and `Resilience` modules — authentication (`ITokenProvider`), the OpenTelemetry `ActivitySource` naming convention, and an opt-in Polly retry pipeline. Both the gRPC client and the future JSON client consume this package as peers — neither depends on the other. `Authentication` sits at the bottom of the kernel's namespace DAG (it depends on neither of the other modules), so it can later be extracted into its own package without a breaking change.

## Key Types

| Type | Purpose |
|------|---------|
| `ITokenProvider` | Interface — `Task<string> GetTokenAsync(CancellationToken)` |
| `ITokenProvider.None` | Static singleton signaling unauthenticated access (no Authorization header) |
| `StaticTokenProvider` | Returns a fixed token string. Use for short-lived processes or testing |
| `ClientCredentialsProvider` | OAuth2 client-credentials flow with thread-safe TTL cache (`SemaphoreSlim` + `Volatile` reads/writes) |
| `ClientCredentialsOptions` | Config: `Domain`, `ClientId`, `ClientSecret`, `Audience`, `TokenEndpoint`, `SafetyMargin`, `AllowInsecureTokenEndpoint` |
| `Telemetry.LedgerActivitySource` | Shared `ActivitySource` naming convention — `NameFor<T>()`, `Create<T>()`, `StartActivity<T>()` |
| `Resilience.RetryOptions` | Config for the opt-in retry pipeline. `Enabled` defaults to `false` |
| `Resilience.RetryPipelineFactory` | Builds a Polly `ResiliencePipeline` from `RetryOptions` — `ResiliencePipeline.Empty` (a genuine no-op) when disabled |

## Usage

### Client-credentials (OAuth2) via DI

```csharp
services.AddCantonAuth(configuration.GetSection("Canton:Auth"));
```

```json
{
  "Canton": {
    "Auth": {
      "Domain": "dev-peaceful.eu.auth0.com",
      "ClientId": "my-client-id",
      "ClientSecret": "my-client-secret",
      "Audience": "https://canton.network/"
    }
  }
}
```

`Domain` accepts either a bare hostname (e.g. `dev-peaceful.eu.auth0.com`) or an absolute https URL (e.g. `https://auth.example.com`, or `https://auth.example.com/tenant-a` for per-tenant subpaths). If `Domain` is a bare hostname, it is treated as `https://{hostname}`. If it is an absolute URL, that URL is used as the base. In both cases, `/oauth/token` is appended, preserving any existing path (`https://auth.example.com/tenant-a` → `https://auth.example.com/tenant-a/oauth/token`). Userinfo, query, and fragments are rejected. `ClientCredentialsProvider` caches tokens until `expires_in - SafetyMargin` (default 30s) and concurrent callers share a single HTTP request during refresh.

Plaintext `http` endpoints (whether from `Domain` or `TokenEndpoint`) are rejected by default, because the token request POSTs the client secret in cleartext — anyone on the network path could read it. To accept the risk against a local endpoint (e.g. `http://localhost:8080` during development), set `AllowInsecureTokenEndpoint` to `true`; the provider then logs a warning at construction.

To override the token endpoint (e.g., non-standard OAuth2 servers like Keycloak's `/realms/{realm}/protocol/openid-connect/token`):

```json
{
  "Canton": {
    "Auth": {
      "TokenEndpoint": "https://custom.example.com/oauth/token",
      "ClientId": "...",
      "ClientSecret": "..."
    }
  }
}
```

### Static token via DI

```csharp
services.AddCantonStaticAuth("eyJ...");
```

### Action-based configuration

```csharp
services.AddCantonAuth(options =>
{
    options.Domain = "https://auth.example.com";
    options.ClientId = "my-client-id";
    options.ClientSecret = "my-client-secret";
    options.Audience = "https://canton.network/";
});
```

### Registration precedence

All methods use `TryAddSingleton` — the first registration wins. Register explicit providers before calling `AddLedgerClient`/`AddAdminClient` to override auto-registration.

### Unauthenticated access

When no `ITokenProvider` is registered, `AddLedgerClient`/`AddAdminClient` register `ITokenProvider.None` as a default. Clients detect this and skip the Authorization header. Use this for local development with unauthenticated Canton nodes.

### OpenTelemetry `ActivitySource` naming (`Canton.Ledger.Kernel.Telemetry`)

`LedgerActivitySource` centralizes the naming convention every client's `ActivitySource` follows, so a host registers sources the same way regardless of transport:

```csharp
public static string ActivitySourceName => LedgerActivitySource.NameFor<MyClient>();
private static readonly ActivitySource ActivitySource = LedgerActivitySource.Create<MyClient>();

using var activity = LedgerActivitySource.StartActivity<MyClient>(ActivitySource);
```

BCL `System.Diagnostics.Activity` only — no OpenTelemetry SDK dependency. `StartActivity` no-ops (returns `null`) until the host registers a listener.

### Opt-in retry pipeline (`Canton.Ledger.Kernel.Resilience`)

`RetryPipelineFactory.Create` builds a Polly `ResiliencePipeline` from `RetryOptions`:

```csharp
var pipeline = RetryPipelineFactory.Create(new RetryOptions
{
    Enabled = true,
    MaxRetryAttempts = 3,
    Delay = TimeSpan.FromMilliseconds(200)
});
```

`RetryOptions.Enabled` defaults to `false`, in which case `Create` returns `ResiliencePipeline.Empty` — a genuine no-op — so a transport that has not opted in sees no retry behavior. The pipeline is transport-neutral: it references no `Grpc.Core` or `System.Net.Http` types, and knows nothing about ledger command semantics. A retried command can double-submit unless the caller reuses a stable `command_id`/`submission_id` for ledger-side dedup — the pipeline itself confers no idempotency.

## Internals

- `IHttpClientFactory` named client `"CantonAuth"` — no `using` on the `HttpClient` (factory manages handler lifetime)
- `TimeProvider` for testable time (pass `FakeTimeProvider` in tests)
- `Volatile.Read`/`Volatile.Write` for cache fields — write order: token before expiry (matches read order)
- Validates `expires_in > 0` and `access_token` non-empty after deserialization
- Token endpoint is resolved once at construction — unresolvable options (no `TokenEndpoint`, invalid `Domain`, plaintext `http` without `AllowInsecureTokenEndpoint`) throw `InvalidOperationException` when the provider is constructed, not at the first token request

## Related Packages

- `Canton.Ledger.Grpc.Client` — gRPC client that consumes `ITokenProvider`
- `Canton.Ledger.Pqs.Client` — PQS query client
