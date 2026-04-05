# Canton.Ledger.Grpc.Client

High-level gRPC client for the Canton Ledger API with integration to `Daml.Codegen.CSharp.Runtime` types.

## Key Types

| Type | Purpose |
|------|---------|
| `ILedgerClient` | Command operations: `CreateAsync`, `ExerciseAsync`, `SubmitAsync` |
| `IAdminClient` | Admin operations: `AllocatePartyAsync`, `CreateUserAsync`, `GrantUserRightsAsync` |
| `LedgerClientOptions` | Config: `GrpcAddress` (required), `UserId`, `MaxMessageSize`, `Timeout` |

## Authentication

Clients receive an `ITokenProvider` from `Canton.Ledger.Auth`. Three modes:

### 1. Client credentials (OAuth2) — auto-registered from config

```csharp
services.AddLedgerClient(
    configuration.GetSection("Canton:Ledger"),
    authConfiguration: configuration.GetSection("Canton:Auth"));
```

This calls `AddCantonAuth(authConfiguration)` internally. The `ClientCredentialsProvider` handles token acquisition and caching.

### 2. Static token — explicit registration

```csharp
services.AddCantonStaticAuth("eyJ...");
services.AddLedgerClient(configuration.GetSection("Canton:Ledger"));
```

Explicit registrations use `TryAddSingleton` — they always take precedence over auto-registration.

### 3. Unauthenticated — no auth configured

```csharp
services.AddLedgerClient(configuration.GetSection("Canton:Ledger"));
// No ITokenProvider registered — defaults to ITokenProvider.None
// Clients skip the Authorization header
```

Use for local development with unauthenticated Canton nodes.

## Usage

### Creating Contracts

```csharp
// Using generated template types from Daml.Codegen.CSharp
var asset = new Asset("Alice", "My Asset", 100m);

var contractId = await ledgerClient.CreateAsync(
    asset,
    actAs: "Alice::1234...",
    workflowId: "create-asset");
```

### Exercising Choices

```csharp
var command = ExerciseCommand.For(
    contractId,
    Asset.Transfer.Create(newOwner: "Bob::5678..."));

await ledgerClient.ExerciseAsync(
    command,
    actAs: "Alice::1234...");
```

### Party Management

```csharp
var party = await adminClient.AllocatePartyAsync("alice-hint");

var user = await adminClient.CreateUserAsync(
    userId: "alice-user",
    primaryParty: party.Party,
    rights: [new UserRight.ActAs(party.Party), new UserRight.ReadAs(party.Party)]);
```

### User Management

```csharp
await adminClient.GrantUserRightsAsync(
    "alice-user",
    [new UserRight.ReadAs("Bob::5678...")]);

var users = await adminClient.ListUsersAsync();
```

## Dependency Injection

The recommended DI lifetime is **Singleton** — gRPC clients share the underlying `GrpcChannel` lifetime.

```csharp
// Config-based (recommended)
services.AddLedgerClient(configuration.GetSection("Canton:Ledger"));
services.AddAdminClient(configuration.GetSection("Canton:Ledger"));

// With auth configuration
services.AddLedgerClient(
    configuration.GetSection("Canton:Ledger"),
    configuration.GetSection("Canton:Auth"));

// Action-based
services.AddLedgerClient(options => options.GrpcAddress = "https://localhost:5001");

// Health check — requires IAdminClient, calls GetParticipantIdAsync to verify connectivity
services.AddHealthChecks().AddLedgerClient(tags: ["grpc", "ready"]);
```

### OpenTelemetry Tracing

```csharp
tracing.AddSource(LedgerClient.ActivitySourceName);
tracing.AddSource(AdminClient.ActivitySourceName);
```

## Related Packages

- `Canton.Ledger.Auth` — Authentication providers (`ITokenProvider`)
- `Canton.Ledger.Grpc` — Low-level gRPC stubs
- `Canton.Ledger.Pqs.Client` — PQS query client
- `Daml.Codegen.CSharp.Runtime` — Runtime types for generated Daml contracts
