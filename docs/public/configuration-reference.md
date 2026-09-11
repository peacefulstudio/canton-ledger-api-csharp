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
      "Audience": "https://canton.network/",
      "Tls": {
        "ClientCertificatePkcs12Path": "/run/secrets/auth-client.p12",
        "ClientCertificatePkcs12Password": "change-me",
        "CertificateAuthorityBundlePemPath": "/run/secrets/auth-ca.pem"
      }
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
| `Tls:ClientCertificatePemPath` | `string?` | `null` | Path to the PEM file holding the client certificate. Doubles as the key file when `ClientCertificateKeyPemPath` is unset. One client-identity source at most: this, `ClientCertificatePkcs12Path`, or a code-only `ClientCertificate`. |
| `Tls:ClientCertificateKeyPemPath` | `string?` | `null` | Path to the client certificate's private key when it lives in a file of its own. Requires `ClientCertificatePemPath`. |
| `Tls:ClientCertificatePkcs12Path` | `string?` | `null` | Path to the PKCS#12 (`.pfx` / `.p12`) file holding the client certificate and its private key. |
| `Tls:ClientCertificatePkcs12Password` | `string?` | `null` | Password protecting `ClientCertificatePkcs12Path`; leave unset for an unprotected file. Requires `ClientCertificatePkcs12Path`. |
| `Tls:ClientCertificate` | `X509Certificate2?` | `null` | **Code-only** — not bindable from configuration. An already-loaded client certificate, which must carry a private key. Stays owned by the caller: it must outlive every client built from these options, and the caller disposes it. |
| `Tls:CertificateAuthorityBundlePemPath` | `string?` | `null` | Path to a PEM bundle of certificate authorities that **replaces** the operating-system trust store for this client rather than adding to it. One authority source at most: this or `CertificateAuthorities`. |
| `Tls:CertificateAuthorities` | `X509Certificate2Collection?` | `null` | **Code-only** — not bindable from configuration. Already-loaded authorities, replacing the OS trust store as above. Rejected at startup when empty — an empty custom trust store rejects every peer. |
| `Tls:RevocationMode` | `X509RevocationMode` | `NoCheck` | How the peer certificate's revocation status is checked: `NoCheck`, `Offline` or `Online`. It rides the chain policy built only for custom root trust, so anything other than `NoCheck` requires an authority source and is otherwise rejected at startup. |
| `ConfigureChannel` | `Action<GrpcChannelOptions>?` | `null` | **Code-only** — not bindable from configuration. Hook to tune or replace the built `GrpcChannelOptions` (e.g. a caller-owned `HttpMessageHandler`); runs after the SDK's defaults, so what it sets wins. Set it via the delegate overload or `PostConfigure` (below). |

Validation recurses into `Retry` and `Tls`, so a misconfigured retry pipeline or an incoherent set of TLS material also fails at startup.

## `ClientCredentialsOptions` (`Canton:Auth`)

OAuth2 client-credentials token acquisition, with thread-safe TTL caching and automatic refresh.

| Key | Type | Default | Notes |
|---|---|---|---|
| `ClientId` | `string` | — (required) | OAuth2 client identifier. |
| `ClientSecret` | `string` | — (required) | OAuth2 client secret. |
| `Domain` | `string?` | `null` | Identity-provider hostname (`my-tenant.eu.auth0.com`) or absolute http/https URL; `/oauth/token` is appended, preserving any existing path. At least one of `Domain` / `TokenEndpoint` must be set. Values already ending in `/oauth/token`, userinfo, query strings, and fragments are rejected. |
| `TokenEndpoint` | `Uri?` | `null` | Explicit token endpoint; **takes precedence over `Domain`** when both are set. Use for providers that don't follow the `/oauth/token` convention (e.g. Keycloak's `/realms/{realm}/protocol/openid-connect/token`). |
| `Audience` | `string?` | `null` | OAuth2 audience, e.g. `https://canton.network/`. |
| `AllowInsecureTokenEndpoint` | `bool` | `false` | Plaintext `http` token endpoints are rejected at validation time (the token request carries the client secret). Set `true` to opt in, e.g. against localhost during development; a warning is logged whenever a plaintext endpoint is used. This opt-in cannot be combined with configured `Tls`: TLS identity and trust settings require `https`. |
| `SafetyMargin` | `TimeSpan` | `00:00:30` | How far before token expiry a refresh is triggered. Must not be negative. |
| `TokenAcquisitionTimeout` | `TimeSpan` | `00:00:30` | Ceiling on a single token-acquisition HTTP request (token fetches are serialized behind one refresh lock). Must be positive. Governs only token acquisition — `LedgerClientOptions.Timeout` covers the gRPC call. |
| `Tls:ClientCertificatePemPath` | `string?` | `null` | Path to the PEM file holding the token client's certificate. Doubles as the key file when `ClientCertificateKeyPemPath` is unset. One client-identity source at most: this, `ClientCertificatePkcs12Path`, or a code-only `ClientCertificate`. |
| `Tls:ClientCertificateKeyPemPath` | `string?` | `null` | Path to the token client's private key when it lives in a file of its own. Requires `ClientCertificatePemPath`. |
| `Tls:ClientCertificatePkcs12Path` | `string?` | `null` | Path to the PKCS#12 (`.pfx` / `.p12`) file holding the token client's certificate and private key. |
| `Tls:ClientCertificatePkcs12Password` | `string?` | `null` | Password protecting `ClientCertificatePkcs12Path`; leave unset for an unprotected file. Requires `ClientCertificatePkcs12Path`. |
| `Tls:ClientCertificate` | `X509Certificate2?` | `null` | **Code-only** — not bindable from configuration. An already-loaded client certificate, which must carry a private key. Stays owned by the caller and must outlive the token client. |
| `Tls:CertificateAuthorityBundlePemPath` | `string?` | `null` | Path to a PEM bundle of certificate authorities that **replaces** the operating-system trust store for the token client. One authority source at most: this or `CertificateAuthorities`. |
| `Tls:CertificateAuthorities` | `X509Certificate2Collection?` | `null` | **Code-only** — not bindable from configuration. Already-loaded authorities replacing the OS trust store. Rejected at startup when empty. |
| `Tls:RevocationMode` | `X509RevocationMode` | `NoCheck` | How the identity provider certificate's revocation status is checked. Anything other than `NoCheck` requires an authority source. |

Authentication TLS is configured independently from ledger transport TLS. When `AddCantonAuth` installs `ClientCredentialsProvider`, configuring `LedgerClientOptions.Tls` or `RestLedgerClientOptions.Tls` without also configuring `ClientCredentialsOptions.Tls` fails at startup; pre-existing static and custom token providers are unaffected. Configured authentication TLS also requires an effective `https` token endpoint because its identity and trust material cannot apply over plaintext `http`.

## `PqsClientOptions` (`Canton:Pqs`)

| Key | Type | Default | Notes |
|---|---|---|---|
| `ConnectionString` | `string` | — (required) | PostgreSQL connection string for the PQS database. Required even when an `NpgsqlDataSource` is registered (it is still validated at startup); when a data source *is* registered in the container, connections are opened from it instead. |
| `JsonSerializerOptions` | `JsonSerializerOptions?` | `null` | **Code-only** — not bindable from configuration. Serializer options for contract payloads; `null` means the client's defaults. Set via the delegate overload or `PostConfigure` (below). Setting it *replaces* those defaults rather than adding to them, so to keep them and add a converter, seed from `PqsClientOptions.CreateDefaultJsonSerializerOptions()` — a fresh, mutable instance per call — and add to what it returns. Generated template types carry their own `ContractId<T>` converter, so a payload with a contract-id field deserializes under the defaults with nothing to register. |

## `RestLedgerClientOptions` (`Canton:Rest`)

| Key | Type | Default | Notes |
|---|---|---|---|
| `HttpAddress` | `string` | — (required) | JSON Ledger API base address, e.g. `http://localhost:7575`. |
| `UserId` | `string?` | `null` | User id for command submissions; when omitted the participant derives it from the access token. |
| `StreamWindowLimit` | `long` | `200` | Cap on the entries one window of a looped read returns, sent as the `limit` query parameter on every window the pagination loop opens. Matches the participant's own `http-list-max-elements-limit` default; a participant configured below it answers the first window with `413`. Must be positive. |
| `StreamWindowIdleTimeout` | `TimeSpan` | `00:00:02` | How long the participant holds one window open once no further entry arrives, sent as `stream_idle_timeout_ms` and rounded down to whole milliseconds. Must be positive, and raising it past the participant's own request timeout ends the stream instead of holding the window longer. |
| `Retry:Enabled` | `bool` | `false` | Opt-in retry pipeline for HTTP requests. Only transient transport failures are retried — a refused, reset or DNS-failed connection and a client-side request timeout. A participant answering with a status code (429, 503, a gateway 5xx) is a response rather than an exception and is **not** retried. Enabling it buffers each request body in memory so it can be replayed. |
| `Retry:MaxRetryAttempts` | `int` | `3` | Maximum retry attempts once enabled. Must be ≥ 0. |
| `Retry:Delay` | `TimeSpan` | `00:00:00.200` | Base delay between attempts (exponential backoff). Must be ≥ 0. |
| `Tls:ClientCertificatePemPath` | `string?` | `null` | Path to the PEM file holding the client certificate. Doubles as the key file when `ClientCertificateKeyPemPath` is unset. One client-identity source at most: this, `ClientCertificatePkcs12Path`, or a code-only `ClientCertificate`. |
| `Tls:ClientCertificateKeyPemPath` | `string?` | `null` | Path to the client certificate's private key when it lives in a file of its own. Requires `ClientCertificatePemPath`. |
| `Tls:ClientCertificatePkcs12Path` | `string?` | `null` | Path to the PKCS#12 (`.pfx` / `.p12`) file holding the client certificate and its private key. |
| `Tls:ClientCertificatePkcs12Password` | `string?` | `null` | Password protecting `ClientCertificatePkcs12Path`; leave unset for an unprotected file. Requires `ClientCertificatePkcs12Path`. |
| `Tls:ClientCertificate` | `X509Certificate2?` | `null` | **Code-only** — not bindable from configuration. An already-loaded client certificate, which must carry a private key. Stays owned by the caller: it must outlive every client built from these options, and the caller disposes it. |
| `Tls:CertificateAuthorityBundlePemPath` | `string?` | `null` | Path to a PEM bundle of certificate authorities that **replaces** the operating-system trust store for this client rather than adding to it. One authority source at most: this or `CertificateAuthorities`. |
| `Tls:CertificateAuthorities` | `X509Certificate2Collection?` | `null` | **Code-only** — not bindable from configuration. Already-loaded authorities, replacing the OS trust store as above. Rejected at startup when empty — an empty custom trust store rejects every peer. |
| `Tls:RevocationMode` | `X509RevocationMode` | `NoCheck` | How the peer certificate's revocation status is checked: `NoCheck`, `Offline` or `Online`. It rides the chain policy built only for custom root trust, so anything other than `NoCheck` requires an authority source and is otherwise rejected at startup. |

## Authentication registration and precedence

Only unkeyed `ITokenProvider` registrations participate in the default selection; keyed providers remain independent.

1. **A pre-existing non-fallback unkeyed provider wins.** `AddCantonStaticAuth("eyJ...")` uses try-add semantics after removing only the exact unauthenticated fallback, and `AddCantonLedger` skips `Canton:Auth` binding entirely when an explicit provider is already present, so leftover auth config cannot fail startup once a provider has been chosen.
2. **`AddCantonLedger` binds `Canton:Auth` when the section has any populated value.** A client-credentials provider is registered and its options validated at startup — half-configured auth (say, `ClientSecret` set but `ClientId` missing) fails loudly instead of silently falling back to unauthenticated.
3. **`AddCantonAuth` and `AddCantonStaticAuth` replace only the exact unkeyed `ITokenProvider.None` singleton instance.** This lets either explicit auth mode replace the unauthenticated fallback installed by a client registration while preserving any pre-existing non-`None` unkeyed provider descriptor without resolving or constructing it.
4. **No provider, no auth config: unauthenticated.** The registration paths fall back to `ITokenProvider.None`; the clients run without credentials and log a warning at construction.

Within client-credentials options, `TokenEndpoint` takes precedence over the `Domain`-derived endpoint when both are set.

```csharp
// Convention-based: Canton:Ledger + Canton:Auth, ITokenProvider resolved per the rules above.
services.AddCantonLedger(configuration);

// Explicit provider — wins over Canton:Auth even if that section is populated.
services.AddCantonStaticAuth("eyJ...");
services.AddCantonLedger(configuration);
```

## `PostConfigure` hooks

The code-only properties above cannot come from `appsettings.json`. When you register from configuration but need them set, add a `PostConfigure` — it runs after configuration binding and before startup validation reads the final value:

```csharp
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
