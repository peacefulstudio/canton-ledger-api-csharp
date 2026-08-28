# Configuration Reference

How the `Canton.Ledger.*` clients are configured: the canonical configuration sections, every bindable option with its default, how authentication registration is resolved, where to hook `PostConfigure`, and how to register the health checks.

All registration paths validate options eagerly at host startup (`ValidateDataAnnotations()` + `ValidateOnStart()`), so a misconfigured client fails when the host starts, not at the first call.

## Canonical configuration sections

`AddCantonLedger(configuration)` — the recommended one-call registration — reads two sections by convention from the root configuration:

| Section | Binds | Consumed by |
|---|---|---|
| `Canton:Ledger` | `LedgerClientOptions` | `LedgerClient` / `AdminClient` (`Canton.Ledger.Grpc.Client`) |
| `Canton:Auth` | `ClientCredentialsOptions` | `ClientCredentialsProvider` (`Canton.Ledger.Kernel`) |

The per-client overloads (`AddLedgerClient`, `AddAdminClient`, `AddPqsClient`, `AddRestLedgerClient`, `AddCantonAuth`) accept any `IConfiguration` section; their XML docs suggest these conventional names:

| Section | Binds | Registration |
|---|---|---|
| `Canton:Ledger` | `LedgerClientOptions` | `AddLedgerClient(config.GetSection("Canton:Ledger"))` |
| `Canton:Auth` | `ClientCredentialsOptions` | `AddCantonAuth(config.GetSection("Canton:Auth"))` |
| `Canton:Pqs` | `PqsClientOptions` | `AddPqsClient(config.GetSection("Canton:Pqs"))` |
| `Canton:Rest` | `RestLedgerClientOptions` | `AddRestLedgerClient(config.GetSection("Canton:Rest"))` |

A fully configured `appsettings.json`:

```json
{
  "Canton": {
    "Ledger": {
      "GrpcAddress": "https://participant.example.com:5001",
      "UserId": "my-app",
      "Timeout": "00:00:30",
      "Retry": { "Enabled": true }
    },
    "Auth": {
      "Domain": "my-tenant.eu.auth0.com",
      "ClientId": "my-client-id",
      "ClientSecret": "my-client-secret",
      "Audience": "https://canton.network/"
    },
    "Pqs": {
      "ConnectionString": "Host=localhost;Database=pqs;Username=pqs;Password=pqs"
    }
  }
}
```

### Environment variables

Standard .NET configuration binding applies: replace `:` with `__` (double underscore).

```bash
export Canton__Ledger__GrpcAddress="https://participant.example.com:5001"
export Canton__Auth__ClientId="my-client-id"
export Canton__Auth__ClientSecret="my-client-secret"
```

`TimeSpan` values bind from the invariant format, e.g. `00:00:30` for 30 seconds.

## `LedgerClientOptions` (`Canton:Ledger`)

| Key | Type | Default | Notes |
|---|---|---|---|
| `GrpcAddress` | `string` | — (required) | gRPC endpoint, e.g. `https://localhost:5001`. An `http` address opens a cleartext channel — bearer tokens are then sent readable on the wire and the client logs a warning at construction. |
| `UserId` | `string?` | `null` | User id for command submissions. |
| `MaxMessageSize` | `int` | `104857600` (100 MB) | Maximum gRPC message size in bytes. |
| `KeepAlivePingDelay` | `TimeSpan` | `00:01:00` | Interval between HTTP/2 keep-alive pings, so a silently dropped connection fails a long-running stream promptly. |
| `KeepAlivePingTimeout` | `TimeSpan` | `00:00:20` | How long a ping waits for its acknowledgement before the connection counts as dead. |
| `Timeout` | `TimeSpan?` | `00:00:30` | Per-attempt gRPC deadline. With retries enabled each attempt gets a fresh budget; the caller's `CancellationToken` is the overall ceiling. |
| `Retry:Enabled` | `bool` | `false` | Opt-in retry pipeline for unary RPCs. Only transient transport failures (`Unavailable`, `DeadlineExceeded`) are retried. |
| `Retry:MaxRetryAttempts` | `int` | `3` | Maximum retry attempts once enabled. Must be ≥ 0. |
| `Retry:Delay` | `TimeSpan` | `00:00:00.200` | Base delay between attempts (exponential backoff). Must be ≥ 0. |
| `ConfigureChannel` | `Action<GrpcChannelOptions>?` | `null` | **Code-only** — not bindable from configuration. Hook to tune or replace the built `GrpcChannelOptions` (e.g. a caller-owned `HttpMessageHandler`); runs after the SDK's defaults, so what it sets wins. Set it via the delegate overload or `PostConfigure` (below). |

Validation recurses into `Retry`, so a misconfigured retry pipeline also fails at startup.

## `ClientCredentialsOptions` (`Canton:Auth`)

OAuth2 client-credentials token acquisition, with thread-safe TTL caching and automatic refresh.

| Key | Type | Default | Notes |
|---|---|---|---|
| `ClientId` | `string` | — (required) | OAuth2 client identifier. |
| `ClientSecret` | `string` | — (required) | OAuth2 client secret. |
| `Domain` | `string?` | `null` | Identity-provider hostname (`my-tenant.eu.auth0.com`) or absolute http/https URL; `/oauth/token` is appended, preserving any existing path. At least one of `Domain` / `TokenEndpoint` must be set. Values already ending in `/oauth/token`, userinfo, query strings, and fragments are rejected. |
| `TokenEndpoint` | `Uri?` | `null` | Explicit token endpoint; **takes precedence over `Domain`** when both are set. Use for providers that don't follow the `/oauth/token` convention (e.g. Keycloak's `/realms/{realm}/protocol/openid-connect/token`). |
| `Audience` | `string?` | `null` | OAuth2 audience, e.g. `https://canton.network/`. |
| `AllowInsecureTokenEndpoint` | `bool` | `false` | Plaintext `http` token endpoints are rejected at validation time (the token request carries the client secret). Set `true` to opt in, e.g. against localhost during development; a warning is logged whenever a plaintext endpoint is used. |
| `SafetyMargin` | `TimeSpan` | `00:00:30` | How far before token expiry a refresh is triggered. Must not be negative. |
| `TokenAcquisitionTimeout` | `TimeSpan` | `00:00:30` | Ceiling on a single token-acquisition HTTP request (token fetches are serialized behind one refresh lock). Must be positive. Governs only token acquisition — `LedgerClientOptions.Timeout` covers the gRPC call. |

## `PqsClientOptions` (`Canton:Pqs`)

| Key | Type | Default | Notes |
|---|---|---|---|
| `ConnectionString` | `string` | — (required) | PostgreSQL connection string for the PQS database. Required even when an `NpgsqlDataSource` is registered (it is still validated at startup); when a data source *is* registered in the container, connections are opened from it instead. |
| `JsonSerializerOptions` | `JsonSerializerOptions?` | `null` | **Code-only** — not bindable from configuration. Serializer options for contract payloads; `null` means the client's defaults. Set via the delegate overload or `PostConfigure` (below). |

## `RestLedgerClientOptions` (`Canton:Rest`)

| Key | Type | Default | Notes |
|---|---|---|---|
| `HttpAddress` | `string` | — (required) | JSON Ledger API base address, e.g. `http://localhost:7575`. |
| `UserId` | `string?` | `null` | User id for command submissions; when omitted the participant derives it from the access token. |

## Authentication registration and precedence

All auth registrations use try-add semantics on `ITokenProvider`, which yields a strict precedence order:

1. **An explicitly registered `ITokenProvider` always wins.** Whether registered directly, via `AddCantonStaticAuth("eyJ...")`, or via `AddCantonAuth(...)` — the first registration sticks; later auth registrations keep it. This also means `AddCantonLedger` skips `Canton:Auth` binding entirely when a provider is already present, so leftover auth config cannot fail startup once an explicit provider has been chosen.
2. **`AddCantonLedger` binds `Canton:Auth` when the section has any populated value.** A client-credentials provider is registered and its options validated at startup — half-configured auth (say, `ClientSecret` set but `ClientId` missing) fails loudly instead of silently falling back to unauthenticated.
3. **No provider, no auth config: unauthenticated.** The registration paths fall back to `ITokenProvider.None`; the clients run without credentials and log a warning at construction.

Within client-credentials options, `TokenEndpoint` takes precedence over the `Domain`-derived endpoint when both are set.

```csharp
// Convention-based: Canton:Ledger + Canton:Auth, ITokenProvider resolved per the rules above.
services.AddCantonLedger(configuration);

// Explicit provider — wins over Canton:Auth even if that section is populated.
services.AddCantonStaticAuth("eyJ...");
services.AddCantonLedger(configuration);
```

## `PostConfigure` hooks

The two code-only properties cannot come from `appsettings.json`. When you register from configuration but need them set, add a `PostConfigure` — it runs after configuration binding and before startup validation reads the final value:

```csharp
services.AddPqsClient(configuration.GetSection("Canton:Pqs"));
services.PostConfigure<PqsClientOptions>(options =>
{
    var json = new JsonSerializerOptions(PqsClient.DefaultJsonSerializerOptions);
    json.Converters.Add(new MyContractIdJsonConverterFactory());
    options.JsonSerializerOptions = json;
});

services.AddLedgerClient(configuration.GetSection("Canton:Ledger"));
services.PostConfigure<LedgerClientOptions>(options =>
    options.ConfigureChannel = channel => channel.HttpHandler = myPooledHandler);
```

(When you configure in code anyway, the `Action<TOptions>` overloads — `AddLedgerClient(o => ...)`, `AddPqsClient(o => ...)` — do the same without a separate hook.)

## Health checks

Both client packages ship an `IHealthChecksBuilder` extension; each takes optional `name`, `failureStatus`, `tags`, and `timeout` parameters.

| Extension | Default name | Probe | Requires |
|---|---|---|---|
| `AddLedgerClient()` (`Canton.Ledger.Grpc.Client`) | `canton-ledger` | Queries the ledger end. Not gated behind participant-admin rights, so a healthy least-privilege deployment reports healthy. | `ILedgerClient` registered |
| `AddPqsClient()` (`Canton.Ledger.Pqs.Client`) | `pqs` | Opens a connection from the configured `ConnectionString` and runs `SELECT 1`. | `PqsClientOptions` registered |

```csharp
services.AddCantonLedger(configuration);
services.AddPqsClient(configuration.GetSection("Canton:Pqs"));

services.AddHealthChecks()
    .AddLedgerClient(tags: ["ready"])
    .AddPqsClient(tags: ["ready"]);
```

## See also

- [Architecture overview](architecture-overview.md) — how the codegen pipeline, `Daml.Runtime`, and the client packages fit together.
- The per-package READMEs under `src/` — shipped inside each NuGet package — for the API surface of each client.
