# Canton.Ledger.Abstractions

The transport-neutral Canton contract layer: the Canton-participant surface types that both the gRPC client (`Canton.Ledger.Grpc.Client`) and the HTTP client (`Canton.Ledger.Rest.Client`) implement, and that the `Canton.Ledger.Testing` fakes stand in for — including `ITokenProvider`, the authentication contract both transports resolve bearer tokens through. It also declares the Participant Query Store read surface `IPqsClient` and its signature types, so `FakePqsClient` is as neutral as the other fakes; `Canton.Ledger.Pqs.Client` supplies the PostgreSQL-backed implementation. It parallels upstream `Daml.Ledger.Abstractions` — Canton contracts extend the Daml-neutral ones — and its dependency direction is `Canton.Ledger.Abstractions` → `Daml.Ledger.Abstractions` + `Daml.Runtime` only. It drags in neither transport nor a database driver: no `Google.Protobuf`, no `Grpc.*`, no `Canton.Ledger.Grpc`, no `Npgsql`. This keeps the shared contract assembly usable by a REST-only consumer without pulling in the gRPC stack.

## Key Types

| Type | Purpose |
|------|---------|
| `ICantonLedgerClient` | The Canton participant client surface — everything on `ILedgerClient` plus the Canton-only operations (fire-and-forget submit, the command completion stream, connected-synchronizer and Ledger API version discovery, offset/id point reads, tree-shaped submission, traffic-cost estimation) |
| `CompletionStreamEvent` | A command-completion stream event — `CommandAccepted`/`CommandRejected`/`Checkpoint`/`StreamError`, with the verdict modelled as the event type |
| `Completion` | The transport-neutral command-completion payload (command id, offset, act-as parties, synchronizer time, submission/user ids, deduplication period) |
| `CompletionStatus` | The `google.rpc.Code` verdict of a rejected command (`Code`, `Message`) |
| `SynchronizerTime` | The synchronizer id and record time a completion was sequenced at |
| `ConnectedSynchronizer` | A synchronizer the participant is connected to (`SynchronizerAlias`, `SynchronizerId`, `Permission`) |
| `TrafficCostEstimate` | What a participant estimates a submission would consume in synchronizer traffic, in bytes (`EstimatedAt`, `ConfirmationRequestCost`, `ConfirmationResponseCost`, `TotalCost`) — the one shape both transports' `EstimateTrafficCostAsync` project into |
| `SynchronizerPermissionLevel` | The permission a participant holds on a connected synchronizer (`Unspecified`/`Submission`/`Confirmation`/`Observation`/`Unrecognized`) |
| `IReassignmentCommand` | Marker for a single reassignment command — an `UnassignCommand` or an `AssignCommand` |
| `UnassignCommand` | Unassigns a contract from its source synchronizer, naming both endpoints |
| `AssignCommand` | Completes a reassignment on the target synchronizer, referencing the unassigned event's `reassignment_id` |
| `ReassignmentSubmission` | A reassignment submission — the command to submit on behalf of a submitter, with optional command/workflow/submission ids; construct with `Of` and refine with the `With…` members |
| `IAdminClient` | The Canton participant administration surface — party allocation and lookup, user and user-right management, package listing/download/vetting, DAR upload and validation |
| `PartyDetails` | A party known to the participant (`Party`, `IsLocal`) |
| `UserDetails` | A user on the participant (`UserId`, `PrimaryParty`) |
| `UserRight` | A right granted to a user — `ActAs`, `ReadAs`, `ParticipantAdmin`, `IdentityProviderAdmin`, `ReadAsAnyParty`, `ExecuteAs`, `ExecuteAsAnyParty` |
| `PackageDetails` | A Daml-LF package known to the participant (`PackageId`, `Name`, `Version`, `PackageSize`, `KnownSince`) |
| `PackageArchive` | A downloaded package archive — the `daml_lf` payload with its hash and `HashFunction` |
| `HashFunction` | The hash function a `PackageArchive.Hash` was computed with (`Sha256`/`Unrecognized`) |
| `VettedPackage` | A package vetted on a participant and synchronizer |
| `PackageIdResolver` / `Identifier.ForPackageName` | Package-name addressing: `ForPackageName` builds the `#<package-name>:<module>:<entity>` identifier Canton resolves per request, and `PackageIdResolver` caches the package id of a name's highest known version over any `IAdminClient` |
| `ParsedLedgerError` | A participant error decoded from the `google.rpc.Status` payload (category, error id, message, `ErrorInfo` metadata, transport status code), in the one shape both clients produce; `MapCategory` is the single classifier turning the wire `category` into a `DamlErrorCategory` |
| `MalformedTransactionTreeException` | Thrown when the node ids on a transaction's events cannot describe a tree, so no `TransactionTree` can be reconstructed from them — the one exception both transports' tree projections raise, so a consumer catches it once regardless of transport |
| `ITokenProvider` | The bearer-token source every transport authenticates through — `Task<string> GetTokenAsync(CancellationToken)` |
| `ITokenProvider.None` | Static singleton signalling unauthenticated access; the clients detect it and send no Authorization header |
| `IPqsClient` | The Participant Query Store read surface — active-contract queries by template or interface, filtered, paged, by contract id, and existence checks |
| `PqsFilter` | A filter condition on a PQS query. Opaque by design: it declares no public members and its cases are internal, so a filter can only be built through `Filter` |
| `Filter` | Builds `PqsFilter`s from strongly-typed expressions — `Filter.Field<T>(t => t.Prop, value)`, composed with `Filter.And`/`Filter.Or`. Field names come from codegen `[DamlField]` metadata, never from user input |
| `PqsPage` | A bounded page of query results (`Limit`, `Offset`), applied as `LIMIT`/`OFFSET` on the query itself |
| `InterfaceContract<TInterface, TView>` | An active contract observed through a Daml interface — its interface-typed `ContractId` paired with the participant-computed view |

## Related Packages

- `Canton.Ledger.Grpc.Client` — gRPC client implementing the Canton participant surface
- `Canton.Ledger.Rest.Client` — HTTP (JSON Ledger API) client
- `Canton.Ledger.Pqs.Client` — PostgreSQL-backed `IPqsClient` implementation for the Participant Query Store
- `Canton.Ledger.Testing` — in-memory fakes for unit-testing against these contracts
