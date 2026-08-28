# Canton.Ledger.Testing

In-memory test doubles and event/result builders for unit-testing business logic against the
Canton Ledger API client surfaces **without a live participant and without a mocking framework**.

`FakeLedgerClient` implements the neutral Canton participant surface
`Canton.Ledger.Abstractions.ICantonLedgerClient` (and therefore the transport-neutral
`Daml.Ledger.Abstractions.ILedgerClient` it extends). `FakeAdminClient`, `FakeTokenProvider`
and `FakePqsClient` implement the equally neutral `Canton.Ledger.Abstractions.IAdminClient`,
`Canton.Ledger.Abstractions.ITokenProvider` and `Canton.Ledger.Abstractions.IPqsClient`.
Every fake therefore stands in for a contract declared in one neutral package, and this package
depends only on `Canton.Ledger.Abstractions`/`Daml.Ledger.Abstractions`/`Daml.Runtime` — no
transport, no `Canton.Ledger.Kernel`, no PostgreSQL driver.

Every fake replays canned data staged ahead of time — none of them is a semantic ledger/PQS
simulator (no contract-key uniqueness, consuming-choice archival, or in-memory filter evaluation).
The one exception is `FakeLedgerClient`'s ledger end: it starts at the offset staged through
`WithLedgerEnd` and advances by one offset per committed write, so a test can read the end, write,
read it again, and get a bounded `(fromOffset, toOffset]` window that actually contains the write. The Canton participant surface (`ICantonLedgerClient` — fire-and-forget
submission, the completion stream, synchronizer/version discovery, offset/id point reads,
tree-shaped submission, traffic-cost estimation) lives
directly on `FakeLedgerClient`: seed completion events, connected synchronizers, the Ledger API
version, point-read transactions, transaction trees, and the traffic-cost estimate the same way, so tests swap Fake ⇆ REST ⇆ gRPC behind one
interface. `SubmitAsync`/`SubmitReassignmentAsync` echo the submission's command id (minting one
when omitted).

## Key types

| Type | Purpose |
|------|---------|
| `FakeLedgerClient` | Configurable in-memory `ICantonLedgerClient` (and thus `ILedgerClient`). Build it with the fluent builder from `FakeLedgerClient.Create()`. Any member, Daml type, or Canton read you did not stage throws a descriptive `NotSupportedException`. |
| `FakeLedgerClientBuilder` | `WithLedgerEnd` (the ledger end *before* any write — stage an event for a bounded window opened around the `n`th write at that offset plus `n`), `WithActiveContracts<T>`, `WithContractEvents<T>`, `WithLedgerEffects<T>`, `WithExerciseResult<TResult>`, `WithCreateResult<TTemplate>`, `WithSubmissionOutcome`, `WithTransactionTree`, `WithReassignmentResult<T>`, `WithCompletionEvents`, `WithConnectedSynchronizers`, `WithLedgerApiVersion`, `WithTrafficCostEstimate` (staging `null` replays a participant that served no estimation), `WithUpdateByOffset`, `WithUpdateById`, then `Build()`. |
| `LedgerEvents` | Factories for `AcsSnapshotEntry<T>` variants: `Created`, `Checkpoint`, `StreamError`, `Unclassified`. |
| `ContractEvents` | Factories for `ContractStreamEvent<T>` variants: `Created`, `Archived`, `Assigned`, `Unassigned`, `Exercised`, `Checkpoint`, `StreamError`, `Unclassified`. |
| `LedgerOutcomes` | Factories for `ExerciseOutcome<T>` variants: `One`, `None`, `Many`, `DamlError`, `InfraError`. |
| `LedgerResults` | Factories for `TransactionResult`, `SubmitAndWaitResult`, `Contract<T>`. |
| `FakeAdminClient` | Configurable in-memory `IAdminClient`. Build it with `FakeAdminClient.Create()`. Query-style members you did not stage throw `NotSupportedException`; the void command members (`GrantUserRightsAsync`, `RevokeUserRightsAsync`, `UploadDarAsync`, `ValidateDarAsync`) always succeed. |
| `FakeAdminClientBuilder` | `WithParticipantId`, `WithAllocatedParty`, `WithParties`, `WithUser`, `WithUsers`, `WithUserRights`, `WithKnownPackages`, `WithPackage`, `WithVettedPackages`, then `Build()`. |
| `FakePqsClient` | Configurable in-memory `IPqsClient`. Build it with `FakePqsClient.Create()`. Query results are staged per Daml type; an unstaged type throws `NotSupportedException`. |
| `FakePqsClientBuilder` | `WithQueryResults<T>`, `WithInterfaceQueryResults<TInterface, TView>`, then `Build()`. |
| `FakeTokenProvider` | In-memory `ITokenProvider`. `FakeTokenProvider.WithToken(token)` for the happy path, `FakeTokenProvider.WithFailure(exception)` to exercise auth-failure paths. No builder — both factories fully configure the fake. |

## Usage

Stage the active-contract snapshot your business logic will read, keyed by Daml type, then drive
it through the fake:

```csharp
using Canton.Ledger.Testing;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;

var owner = new Party("bob");
var asset = new DemoAsset(new Party("issuer"), owner, "GOLD", 42m);

ILedgerClient client = FakeLedgerClient.Create()
    .WithActiveContracts(
        LedgerEvents.Created(
            new ContractId<DemoAsset>("cid1"),
            asset.ToRecord(),
            LedgerOffset.At(1),
            (SynchronizerId)"sync1",
            new[] { owner }),
        LedgerEvents.Checkpoint<DemoAsset>(LedgerOffset.At(2)))
    .Build();

// SubscribeActiveAsync<DemoAsset> now replays the staged entries:
await foreach (var entry in client.SubscribeActiveAsync<DemoAsset>(owner))
{
    // ... exercise the code under test ...
}

// A member you never staged fails loudly instead of returning empty/null:
// client.TryExerciseAsync<string>(...)  ->  NotSupportedException("...WithExerciseResult<String>...")
```

Every unconfigured member and every unstaged Daml type throws a `NotSupportedException` whose
message names the builder call needed to stage it, so a test never silently exercises
unconfigured behaviour.

Seed the completion stream to drive fire-and-forget submission logic (`SubmitAsync` then observe
the completion), using the neutral `CommandAccepted`/`CommandRejected`/`Checkpoint` events:

```csharp
using Canton.Ledger.Abstractions;
using Canton.Ledger.Testing;
using Daml.Runtime.Commands;
using Daml.Runtime.Data;

var owner = new Party("bob");
var completion = new Completion(
    new CommandId("cmd-1"), Offset: 1, ActAs: [owner],
    new SynchronizerTime("sync1", DateTimeOffset.UtcNow),
    SubmissionId: null, UserId: null, DeduplicationOffset: null, DeduplicationDuration: null);

ICantonLedgerClient client = FakeLedgerClient.Create()
    .WithCompletionEvents(
        new CompletionStreamEvent.Checkpoint(0),
        new CompletionStreamEvent.CommandAccepted(completion, "update-1"))
    .Build();

await foreach (var completionEvent in client.CompletionStreamAsync(owner))
{
    // ... assert your code correlates the accepted completion by command id ...
}
```

Seed the submit-and-wait write path — the command shape every generated choice helper submits
through — with the `ExerciseOutcome<TransactionResult>` your code under test should observe.
`TrySubmitAndWaitForTransactionAsync` (both overloads) replies with the staged outcome for every
submission; stage a success or a failure to drive either branch:

```csharp
using Canton.Ledger.Testing;
using Daml.Runtime.Commands;
using Daml.Runtime.Contracts;
using Daml.Runtime.Outcomes;

ILedgerClient happy = FakeLedgerClient.Create()
    .WithSubmissionOutcome(LedgerOutcomes.One(LedgerResults.Transaction(
        "update-1",
        LedgerOffset.At(5),
        new[] { new CreatedContract("cid1", DemoAsset.TemplateId, "{}") },
        archivedContractIds: [],
        (CommandId)"cmd-1")))
    .Build();

ILedgerClient failing = FakeLedgerClient.Create()
    .WithSubmissionOutcome(LedgerOutcomes.DamlError<TransactionResult>(
        DamlErrorCategory.InvalidGivenCurrentSystemStateOther,
        "UNHANDLED_EXCEPTION", "assertion failed", new Dictionary<string, string>()))
    .Build();
```

Stage an admin client the same way:

```csharp
using Canton.Ledger.Abstractions;
using Canton.Ledger.Testing;

IAdminClient admin = FakeAdminClient.Create()
    .WithParticipantId("participant1")
    .WithUser(new UserDetails("alice", "alice::1220"))
    .Build();

var participantId = await admin.GetParticipantIdAsync();
var alice = await admin.GetUserAsync("alice"); // staged UserDetails
var bob = await admin.GetUserAsync("bob");     // null — never staged, not an error
```

Stage a PQS client's query results per Daml type:

```csharp
using Canton.Ledger.Abstractions;
using Canton.Ledger.Testing;

IPqsClient pqs = FakePqsClient.Create()
    .WithQueryResults(new Contract<Holding>(new ContractId<Holding>("cid1"), holding))
    .Build();

var holdings = await pqs.QueryAsync<Holding>();
```

And a token provider for either the happy path or an auth-failure path:

```csharp
using Canton.Ledger.Abstractions;
using Canton.Ledger.Testing;

ITokenProvider ok = FakeTokenProvider.WithToken("test-bearer-token");
ITokenProvider failing = FakeTokenProvider.WithFailure(new InvalidOperationException("token endpoint unreachable"));
```

## Zero mocking-framework dependency

None of the fakes in this package pull in Moq / NSubstitute, so it composes with any (or no)
mocking framework in your own test project. Every fake — `FakeLedgerClient`, `FakeAdminClient`,
`FakeTokenProvider` and `FakePqsClient` — depends only on `Canton.Ledger.Abstractions`,
`Daml.Ledger.Abstractions`, and `Daml.Runtime`.
