# Canton.Ledger.Grpc.Client

High-level gRPC client for the Canton Ledger API with integration to `Daml.Runtime` types.

## Key Types

| Type | Purpose |
|------|---------|
| `ILedgerClient` (from `Daml.Ledger.Abstractions`) | Command operations: `TryCreateAsync`, `ExerciseAsync`, `SubmitAndWaitAsync`, `TrySubmitAndWaitForTransactionAsync`, `TryExerciseForCreatedAsync`, `SubscribeAsync`, `SubscribeActiveAsync`, `GetLedgerEndAsync` |
| `LedgerClient` (concrete, gRPC) | Adds the fire-and-forget async submission surface beyond the interface: `Submit` and `CompletionStreamAsync` |
| `IAdminClient` | Admin operations: `AllocatePartyAsync`, `CreateUserAsync`, `GrantUserRightsAsync` |
| `LedgerClientOptions` | Config: `GrpcAddress` (required), `UserId`, `MaxMessageSize`, `Timeout` |

## Authentication

Clients receive an `ITokenProvider` from `Canton.Ledger.Kernel`. Four modes:

### 1. Convention-based (recommended) — `AddCantonLedger`

```csharp
services.AddCantonLedger(configuration);
```

Reads `Canton:Ledger` for client options, and registers a client-credentials `ITokenProvider` from `Canton:Auth` whenever that section has any populated value. Half-configured auth (e.g. `ClientSecret` without `ClientId`) then fails loudly at startup rather than silently falling back to unauthenticated. `Canton:Auth` keys mirror `ClientCredentialsOptions` (`ClientId`, `ClientSecret`, `Audience`, `Domain` or `TokenEndpoint`); in environment variables they appear as `Canton__Auth__ClientId` and so on.

### 2. Client credentials (OAuth2) — explicit sections

```csharp
services.AddLedgerClient(
    configuration.GetSection("Canton:Ledger"),
    authConfiguration: configuration.GetSection("Canton:Auth"));
```

This calls `AddCantonAuth(authConfiguration)` internally. The `ClientCredentialsProvider` handles token acquisition and caching.

### 3. Static token — explicit registration

```csharp
services.AddCantonStaticAuth("eyJ...");
services.AddLedgerClient(configuration.GetSection("Canton:Ledger"));
```

Explicit registrations use `TryAddSingleton`, so they take precedence over auto-registration only when the explicit auth is registered first. Register `AddCantonStaticAuth(...)` (or any other explicit `ITokenProvider`) before `AddCantonLedger(...)` or `AddLedgerClient(...)`.

### 4. Unauthenticated — no auth configured

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

var outcome = await ledgerClient.TryCreateAsync(
    asset,
    actAs: "Alice::1234...",
    workflowId: "create-asset");

// Outcome is a discriminated union: One / None / Many / DamlError / InfraError.
var contractId = outcome switch
{
    ExerciseOutcome<ContractId<Asset>>.One ok => ok.Result,
    ExerciseOutcome<ContractId<Asset>>.DamlError err => throw new InvalidOperationException(err.ErrorId),
    _ => throw new InvalidOperationException(outcome.GetType().Name),
};
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

### Async Submission + Completions

`Submit` is a true fire path: it returns once the participant accepts the commands (yielding the `command_id`), not when the transaction commits. The verdict arrives separately on `CompletionStreamAsync`, surfaced as `IAsyncEnumerable<Completion>`. The client keeps no pending-set — you correlate completions by `command_id`/`submission_id` and own your offset.

```csharp
// Capture the offset BEFORE submitting — a completion can be emitted
// before the stream is opened.
var beginOffset = await ledgerClient.GetLedgerEndAsync();

// Submit returns the effective CommandId — minted for you when the submission omits one.
CommandId commandId = await ledgerClient.Submit(submission);

await foreach (var completion in ledgerClient.CompletionStreamAsync(actAs, beginOffset, ct))
{
    if (completion.CommandId != commandId.Value) continue;
    if (completion.Status is null or { Code: 0 }) { /* accepted */ }
    break;
}
```

> `Submit` and `SubmitAndWaitAsync` report the effective `CommandId` back to you — the one you supplied, or the one minted here when you omit it. To retry safely after a transport failure, resubmit with that same `CommandId`; re-invoking with a fresh, command_id-less submission mints a *new* id and double-submits, because the participant may have accepted the first attempt before the failure surfaced.

To submit and wait for the transaction in one call instead, use `SubmitAndWaitAsync` (renamed from the former `SubmitAsync`).

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

- `Canton.Ledger.Kernel` — Transport-neutral client kernel: authentication providers (`ITokenProvider`), telemetry convention, retry pipeline
- `Canton.Ledger.Grpc` — Low-level gRPC stubs
- `Canton.Ledger.Pqs.Client` — PQS query client
- `Daml.Runtime` — Runtime types for generated Daml contracts
