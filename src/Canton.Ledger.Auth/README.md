# Canton.Ledger.Auth

Authentication providers for Canton participant nodes. Supplies bearer tokens to the gRPC and PQS clients via the `ITokenProvider` interface.

## Key Types

| Type | Purpose |
|------|---------|
| `ITokenProvider` | Interface — `Task<string> GetTokenAsync(CancellationToken)` |
| `ITokenProvider.None` | Static singleton signaling unauthenticated access (no Authorization header) |
| `StaticTokenProvider` | Returns a fixed token string. Use for short-lived processes or testing |
| `ClientCredentialsProvider` | OAuth2 client-credentials flow with thread-safe TTL cache (`SemaphoreSlim` + `Volatile` reads/writes) |
| `ClientCredentialsOptions` | Config: `Domain`, `ClientId`, `ClientSecret`, `Audience`, `TokenEndpoint`, `SafetyMargin` |

## Usage

### Client-credentials (OAuth2) via DI

```csharp
services.AddCantonAuth(configuration.GetSection("Canton:Auth"));
```

```json
{
  "Canton": {
    "Auth": {
      "Domain": "https://auth.example.com",
      "ClientId": "my-client-id",
      "ClientSecret": "my-client-secret",
      "Audience": "https://canton.network/"
    }
  }
}
```

`ClientCredentialsProvider` acquires tokens from `{Domain}/oauth/token` and caches them until `expires_in - SafetyMargin` (default 30s). Concurrent callers share a single HTTP request during refresh.

To override the token endpoint (e.g., non-standard OAuth2 servers):

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

## Internals

- `IHttpClientFactory` named client `"CantonAuth"` — no `using` on the `HttpClient` (factory manages handler lifetime)
- `TimeProvider` for testable time (pass `FakeTimeProvider` in tests)
- `Volatile.Read`/`Volatile.Write` for cache fields — write order: token before expiry (matches read order)
- Validates `expires_in > 0` and `access_token` non-empty after deserialization

## Related Packages

- `Canton.Ledger.Grpc.Client` — gRPC client that consumes `ITokenProvider`
- `Canton.Ledger.Pqs.Client` — PQS query client
