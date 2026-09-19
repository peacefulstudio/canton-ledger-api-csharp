# Canton.Ledger.Grpc.Client

High-level gRPC client for the Canton Ledger API with integration to `Daml.Runtime` types.

## Key Types

| Type | Purpose |
|------|---------|
| `ICantonLedgerClient` (from `Canton.Ledger.Abstractions`) | **The type to resolve from the container.** Everything on `ILedgerClient` plus the Canton-only participant operations: `SubmitAsync`, `SubmitReassignmentAsync`, `TrySubmitAndWaitForReassignmentAsync`, `TrySubmitAndWaitForTransactionTreeAsync`, `CompletionStreamAsync`, `GetConnectedSynchronizersAsync`, `GetLedgerApiVersionAsync`, `GetUpdateByOffsetAsync`, `GetUpdateByIdAsync`, `EstimateTrafficCostAsync`, and typed interface-view queries via `QueryActiveAsync<TInterface, TView>` |
| `ILedgerClient` (from `Daml.Ledger.Abstractions`) | Command operations: `TryCreateAsync`, `TryExerciseAsync`, `SubmitAndWaitAsync`, `TrySubmitAndWaitForTransactionAsync`, `SubscribeAsync`, `SubscribeActiveAsync`, `GetLedgerEndAsync` |
| `Daml.Ledger.Abstractions.Extensions` | Convenience extension methods on the client interfaces: `ThrowingExercise.ExerciseAsync` (wraps `TryExerciseAsync`, throws on non-`One` outcomes), `CreateByExercise` (`TryCreateOneByExerciseAsync`, `TryCreateManyByExerciseAsync` and their throwing forms), `SingleCommandExtensions.TrySubmitSingleAsync`, `StreamerSnapshot.SnapshotAsync` |
| `LedgerClient` (concrete, gRPC) | The implementation `AddLedgerClient` registers behind `ICantonLedgerClient` — resolve the interface rather than naming this type. Its raw-stub escape hatch is reached through `IGrpcCallInvokerFactory`, and its `ActivitySource` name through `LedgerActivitySourceNames.GrpcLedgerClient` |
| `IAdminClient` (from `Canton.Ledger.Abstractions`) | Admin operations: `AllocatePartyAsync`, `CreateUserAsync`, `GrantUserRightsAsync` |
| `LedgerClientOptions` | Config: `GrpcAddress` (required), `UserId`, `MaxMessageSize`, `Timeout`, `Retry` (opt-in retry pipeline, disabled by default), `Tls` (opt-in mTLS material, unconfigured by default) |
| `TlsOptions` (from `Canton.Ledger.Kernel.Security`) | Nested at `LedgerClientOptions.Tls` and bound from `Canton:Ledger:Tls`. Client identity as `ClientCertificatePemPath` (+ `ClientCertificateKeyPemPath`), `ClientCertificatePkcs12Path` (+ `ClientCertificatePkcs12Password`) or an already-loaded `ClientCertificate`; private-CA trust as `CertificateAuthorityBundlePemPath` or `CertificateAuthorities`, with `RevocationMode`. Left unconfigured the channel is untouched — OS trust store, no client certificate — and it applies to every gRPC registration alike |

## Authentication

Clients receive an `ITokenProvider` (from `Canton.Ledger.Abstractions`; the providers that implement it ship in `Canton.Ledger.Kernel`). Four modes:

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

`AddCantonStaticAuth(...)` may be called before or after `AddLedgerClient(...)`: it replaces only the exact unkeyed `ITokenProvider.None` fallback installed by the client. Register static auth before `AddCantonLedger(...)` when `Canton:Auth` is configured, because a selected client-credentials provider is a real provider and remains in place. Any other pre-existing unkeyed provider also wins, and keyed registrations remain independent.

### 4. Unauthenticated — no auth configured

```csharp
services.AddLedgerClient(configuration.GetSection("Canton:Ledger"));
// No ITokenProvider registered — defaults to ITokenProvider.None
// Clients skip the Authorization header
```

Use for local development with unauthenticated Canton nodes.

### Transport security

An `http://` `GrpcAddress` opens a cleartext channel, so a token-issuing `ITokenProvider` sends its bearer tokens readable — and replayable — by anyone on the network path. The client logs a warning at construction when that combination is detected. Use an `https://` address for any deployment beyond local development.

## Usage

### Creating Contracts

```csharp
// Using generated template types from Daml.Codegen.CSharp
var asset = new Asset(new Party("Alice::1234..."), 100m);

var outcome = await ledgerClient.TryCreateAsync(
    asset,
    actAs: new Party("Alice::1234..."),
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
var command = new ExerciseCommand(
    Asset.TemplateId,
    contractId,
    new ChoiceName("Transfer"),
    new Asset.Transfer(NewOwner: new Party("Bob::5678...")).ToRecord());

await ledgerClient.ExerciseAsync(
    command,
    actAs: new Party("Alice::1234..."));
```

### Async Submission + Completions

`SubmitAsync` is a true fire path: it returns once the participant accepts the commands (yielding the `command_id`), not when the transaction commits. The verdict arrives separately on `CompletionStreamAsync`, surfaced as `IAsyncEnumerable<CompletionStreamEvent>` — a small union where the verdict *is* the event type: `CommandAccepted` (the neutral `Completion` plus the resulting `UpdateId`), `CommandRejected` (the neutral `Completion` plus a `CompletionStatus` carrying the `google.rpc.Code` verdict), `Checkpoint` (the participant's offset checkpoints, so your persisted resume offset keeps advancing during quiet periods instead of falling arbitrarily far behind), and `StreamError` (a mid-stream transport fault surfaced in-band as a terminal event rather than thrown). The client keeps no pending-set — you correlate completions by `command_id`/`submission_id` and own your offset.

```csharp
var actAs = new Party("Alice::1234...");

// Capture the offset BEFORE submitting — a completion can be emitted
// before the stream is opened.
var beginOffset = await ledgerClient.GetLedgerEndAsync();
var resumeOffset = beginOffset;

// SubmitAsync returns the effective CommandId — minted for you when the submission omits one.
CommandId commandId = await ledgerClient.SubmitAsync(submission);

await foreach (var streamEvent in ledgerClient.CompletionStreamAsync(actAs, beginOffset, ct))
{
    switch (streamEvent)
    {
        case CompletionStreamEvent.Checkpoint checkpoint:
            // Persist this even when no completions arrive — it is the offset
            // to resume from without re-processing or hitting pruned data.
            resumeOffset = checkpoint.Offset;
            continue;

        case CompletionStreamEvent.CommandAccepted accepted:
            resumeOffset = accepted.Completion.Offset;
            if (accepted.Completion.CommandId.Value == commandId.Value)
            {
                // Accepted — accepted.UpdateId is the resulting update id.
                return accepted.UpdateId;
            }
            continue;

        case CompletionStreamEvent.CommandRejected rejected:
            resumeOffset = rejected.Completion.Offset;
            if (rejected.Completion.CommandId.Value == commandId.Value)
            {
                // Rejected — rejected.Status carries the google.rpc.Code and message.
                throw new InvalidOperationException(
                    $"Command {commandId.Value} rejected ({rejected.Status.Code}): {rejected.Status.Message}");
            }
            continue;

        case CompletionStreamEvent.StreamError error:
            // Mid-stream transport fault surfaced in-band — reopen from
            // resumeOffset, log, or stop. Terminal: no further events follow.
            throw new InvalidOperationException(
                $"Completion stream failed ({error.StatusCode}): {error.Message}");
    }
}
```

> `SubmitAsync` and `SubmitAndWaitAsync` report the effective `CommandId` back to you — the one you supplied, or the one minted here when you omit it. To retry safely after a transport failure, resubmit with that same `CommandId`; re-invoking with a fresh, command_id-less submission mints a *new* id and double-submits, because the participant may have accepted the first attempt before the failure surfaced.

To submit and wait for the transaction in one call instead, use `SubmitAndWaitAsync`.

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

var rights = await adminClient.ListUserRightsAsync("alice-user");

var users = await adminClient.ListUsersAsync();
```

### Raw gRPC Stubs (Escape Hatch)

For Ledger API services or overloads the typed surface does not cover, opt into
`IGrpcCallInvokerFactory` (namespace `Canton.Ledger.Grpc.Client.Raw`). Its `CreateCallInvoker()`
returns a `Grpc.Core.CallInvoker` that reuses the SDK's authentication, deadline, and retry
plumbing — no hand-built `GrpcChannel`, Bearer-header `CallOptions`, or deadlines needed:

```csharp
services.AddLedgerRawGrpc(configuration.GetSection("Canton:Ledger"));
```

```csharp
var stateService = new StateService.StateServiceClient(invokerFactory.CreateCallInvoker());
var ledgerEnd = await stateService.GetLedgerEndAsync(new GetLedgerEndRequest());
```

Bearer tokens come from the registered `ITokenProvider` on every call (`ITokenProvider.None`
sends no `authorization` header); unary calls get the configured `Timeout` as a per-attempt
deadline and run through the opt-in `Retry` pipeline, while streaming calls attach auth headers
but no default deadline and are never retried. A caller-supplied `authorization` header or
deadline in `CallOptions` wins over the SDK's.

The factory builds its own channel from the same `LedgerClientOptions` the clients use, so it
needs neither client registered and registration order does not matter — at the cost of one extra
HTTP/2 connection to the same endpoint. The container owns that channel, so the invoker stays
valid for as long as the provider does: dispose the provider, not the invoker.

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

// Opt-in raw gRPC escape hatch — registers IGrpcCallInvokerFactory only
services.AddLedgerRawGrpc(configuration.GetSection("Canton:Ledger"));

// Health check — requires IAdminClient, calls GetParticipantIdAsync to verify connectivity
services.AddHealthChecks().AddLedgerClient(tags: ["grpc", "ready"]);
```

Resolve the transport-neutral interfaces, not the concrete clients — the container owns the gRPC
channel, so nothing here is disposed by hand:

```csharp
await using var provider = services.BuildServiceProvider();

var ledgerClient = provider.GetRequiredService<ICantonLedgerClient>();
var adminClient = provider.GetRequiredService<IAdminClient>();
```

### OpenTelemetry Tracing

`Canton.Ledger.OpenTelemetry` registers every Canton client source at once:

```csharp
tracing.AddCantonLedgerInstrumentation();
```

To register these two sources by hand, take the names from `Canton.Ledger.Kernel` — no reference to this package required:

```csharp
tracing.AddSource(LedgerActivitySourceNames.GrpcLedgerClient);
tracing.AddSource(LedgerActivitySourceNames.GrpcAdminClient);
```

## Related Packages

- `Canton.Ledger.Abstractions` — Transport-neutral Canton contract layer: `ICantonLedgerClient`, `IAdminClient`, `ITokenProvider`, `IPqsClient`, the completion and reassignment families
- `Canton.Ledger.Kernel` — Transport-neutral client kernel: the `ITokenProvider` implementations, telemetry convention, retry pipeline
- `Canton.Ledger.Grpc` — Low-level gRPC stubs
- `Canton.Ledger.Pqs.Client` — PQS query client
- `Daml.Runtime` — Runtime types for generated Daml contracts
