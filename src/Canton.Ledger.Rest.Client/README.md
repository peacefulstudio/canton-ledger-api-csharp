# Canton.Ledger.Rest.Client

HTTP client for the Canton Ledger API over the JSON Ledger API (`/v2/...`). `RestLedgerClient` is a full implementation of the transport-neutral `Daml.Ledger.Abstractions.ILedgerClient` and the Canton participant surface `Canton.Ledger.Abstractions.ICantonLedgerClient`, so business logic written against those interfaces runs unchanged over HTTP or gRPC.

## Key Types

| Type | Purpose |
|------|---------|
| `RestLedgerClient` | `ILedgerClient` + `ICantonLedgerClient` over the JSON Ledger API — reads, writes, bounded streams, and the Canton-only submit / reassignment / discovery / point-read / transaction-tree / traffic-cost surface |
| `RestLedgerClientOptions` | Config: `HttpAddress` (required, e.g. `http://localhost:7575`), `UserId` (optional; the participant derives it from the caller's token when omitted), `Retry` (opt-in, disabled by default) |
| `HealthCheckBuilderExtensions.AddRestLedgerClient` | `IHealthChecksBuilder` extension probing the participant over HTTP via `GET /v2/state/ledger-end` |
| `LedgerResultTooLargeException` | Thrown when a bounded read exceeds the participant's `http-list-max-elements-limit` (HTTP `413`) |
| `MalformedTransactionTreeException` | Thrown when the node ids on a transaction's events cannot describe a tree, so no hierarchy can be reconstructed from them. Declared in `Canton.Ledger.Abstractions` and shared with the gRPC transport, so one `catch` covers both |

The raw, per-service Refit interfaces live in the `Canton.Ledger.Rest.Client.Raw` namespace behind the `CANTONREST001` experimental diagnostic; consume them only through `AddRestLedgerRawApis` when you need an endpoint the adapter does not surface.

## Registration

```csharp
// Config-based — binds RestLedgerClientOptions from the given section
services.AddRestLedgerClient(configuration.GetSection("Canton:Rest"));

// Action-based
services.AddRestLedgerClient(options => options.HttpAddress = "http://localhost:7575");
```

`AddRestLedgerClient` registers a single `RestLedgerClient` resolvable as **both** `ILedgerClient` and `ICantonLedgerClient` (backed by the same instance), so a consumer injects the full Canton surface from REST exactly as from gRPC. Authentication reuses the shared `Canton.Ledger.Abstractions.ITokenProvider` — register one (for example `AddCantonStaticAuth(...)` or client-credentials auth) and the client attaches the bearer token to every request.

Add `AddRestLedgerRawApis(...)` alongside it to also register the opt-in raw Refit surface.

## Reads, writes, and streams

- **Writes** (`TryCreateAsync`, `TryExerciseAsync`, `SubmitAndWaitAsync`, `TrySubmitAndWaitForTransactionAsync`) submit through `/v2/commands/submit-and-wait[-for-transaction]`. The `Try*` methods return a structured `ExerciseOutcome` (a `DamlError` on a structured participant error, an `InfraError` on a transport failure or per-call timeout); `SubmitAndWaitAsync` throws `LedgerOperationException`, matching the gRPC transport's throwing contract.
- **Transaction trees** — `TrySubmitAndWaitForTransactionTreeAsync` and `GetUpdateTreeByOffsetAsync` return a committed transaction with its parent/child hierarchy intact: which exercise caused which sub-creates and sub-exercises. `TrySubmitAndWaitForTransactionTreeAsync` is an `ICantonLedgerClient` member, so a consumer reaches it through the injected interface; `GetUpdateTreeByOffsetAsync` is a concrete member on `RestLedgerClient`, because its gRPC counterpart is internal. Both always ask the participant for the ledger-effects view, since hierarchy is only meaningful over creates and exercises. The participant reports that hierarchy as node ids on the ordinary event list — each exercise states the highest node id in the subtree it caused — so the tree is rebuilt from the same response the flat read decodes, not a second request. Node ids that cannot describe a tree fail loudly rather than yielding a silently wrong tree: an `InfraError` outcome on the submit path, and on the point read an `InvalidOperationException` carrying the `MalformedTransactionTreeException` as its `InnerException` — catch the base type there, not the derived one. Node-id gaps left by the participant's own party filtering are normal and tolerated, and an event whose parent exercise was filtered out attaches to the nearest enclosing exercise the parties can still see, or surfaces as a root when none remains. Project a tree back to the flattened shape with `TransactionTreeExtensions.ToTransactionResult()` rather than submitting twice — neither shape is a superset of the other on the wire.
- **Bounded reads** — `SubscribeActiveAsync` is an ACS snapshot ending in a terminal checkpoint; `SubscribeAsync` / `SubscribeLedgerEffectsAsync` are bounded offset-range reads over one blocking call.
- **Interface subscriptions carry the participant-computed view.** Subscribing an interface marker projects the matching `interfaceViews` entry's `viewValue` onto each row, not the implementing template's `createArgument`; a view the participant could not compute surfaces as `Unclassified(InterfaceViewUnavailable)` rather than an empty payload. `QueryActiveAsync<TInterface, TView>` materializes that snapshot into typed view records, exactly as over gRPC.
- **Command completions** — `CompletionStreamAsync` reads `POST /v2/commands/completions`, whose success body is a JSON array, and yields `CommandAccepted` / `CommandRejected` / `Checkpoint` per entry. One call covers one participant-bounded window rather than an endless tail, so a caller follows the stream by reopening it from the last offset it observed; `CompletionStreamLimit` and `CompletionStreamIdleTimeout` on the options bound that window (`limit` and `stream_idle_timeout_ms` on the wire), and leaving them unset defers to the participant. A non-success response ends the enumeration with a terminal `StreamError` carrying the HTTP status code — the in-band fault contract the gRPC transport honours for this method — and a success body that will not decode (malformed JSON, or a completion carrying an unparseable offset or deduplication duration) ends it the same way, with `StatusCode` `0` since the transport reported no failure. A transport failure that never reached the participant still throws.
- **Traffic-cost estimation** — `EstimateTrafficCostAsync(submission, timeout, cancellationToken)` asks the participant what a submission would consume in synchronizer traffic before committing to it, over `POST /v2/interactive-submission/prepare` with cost estimation requested. Nothing reaches the ledger — the participant interprets the commands, answers, and the prepared transaction is discarded — so the call costs about what a submission costs, but the caller's token needs only *read* rights for the `actAs` parties rather than act rights. The answer projects into the shared `Canton.Ledger.Abstractions.TrafficCostEstimate` (`EstimatedAt`, `ConfirmationRequestCost`, `ConfirmationResponseCost`, `TotalCost`, all in bytes), the same record the gRPC client returns. A participant that sends no estimation — one with traffic control disabled, for instance — yields `null` rather than a zeroed record; an estimation that is present and reports zero is a genuine zero-cost estimate. The method is an `ICantonLedgerClient` member, so a consumer prices a submission through the injected interface whichever transport is registered. Two per-transport differences: a rejected request throws `LedgerOperationException` with the participant's category and error id, where gRPC throws `RpcException`; and the cost does not reach a span, because this client's spans are emitted per HTTP request by the pipeline handler rather than per client method.
- **An open-ended live tail is not yet available over HTTP.** `SubscribeAsync` / `SubscribeLedgerEffectsAsync` with `toOffset: null` throw `NotSupportedException` — that read lands over a future WebSocket transport. The `SupportsUnboundedStreaming` capability probe reports `false` until then.
- **Errors are parsed, not passed through raw.** A participant's non-success response on any call, `GetLedgerEndAsync` included, is decoded into the participant's category, error id and message before it reaches the caller — as a `LedgerOperationException` on the throwing methods, or an `ExerciseOutcome.DamlError` / `InfraError` on the `Try*` methods.

## Retry

Off by default. Opt in to have transient transport failures retried with exponential backoff and jitter, the same `Canton.Ledger.Kernel` pipeline the gRPC client uses:

```csharp
services.AddRestLedgerClient(options =>
{
    options.HttpAddress = "http://localhost:7575";
    options.Retry = new RetryOptions { Enabled = true, MaxRetryAttempts = 3, Delay = TimeSpan.FromMilliseconds(200) };
});
```

The retry handler sits outermost in the HTTP pipeline, so every attempt resolves a fresh bearer token and emits its own client span, plus a `RestLedgerClient.RetryAttempt` span carrying `retry.attempt` and `retry.delay_ms`. So a retried attempt can replay the request body, the handler buffers request content in memory before the first attempt — enabling retry therefore costs one in-memory copy of each request body, which is worth weighing against a large DAR upload. Retried requests reuse the `command_id` fixed above the retry boundary, so ledger-side deduplication makes a resubmission idempotent — the pipeline itself confers no idempotency.

Two asymmetries against the gRPC pipeline are deliberate:

- **Only exceptions are retried** — a refused/reset/DNS-failed connection (`HttpRequestException`) and a client-side request timeout, the HTTP analogues of gRPC `Unavailable`/`DeadlineExceeded`. A participant that *answers* with `429`, `503`, or a gateway `5xx` is a response, not an exception, and is surfaced to the caller unretried.
- **No duplicate-command recovery.** The gRPC client maps a retried `DUPLICATE_COMMAND` rejection back to success by point-reading the committed transaction from the rejection's `completion_offset`; the JSON API does not serve that metadata, so a first attempt that commits while its response is lost surfaces the resubmission's `DUPLICATE_COMMAND` to the caller.

## Health checks

```csharp
services.AddHealthChecks().AddRestLedgerClient();
```

Probes the participant with `GET /v2/state/ledger-end`, which is not gated behind `participant_admin`, so a least-privilege deployment still reports healthy. The check resolves the concrete `RestLedgerClient`, so a host wiring both transports gets a check that probes the HTTP endpoint specifically rather than whichever transport won the `ILedgerClient` registration.

## Tracing

The client emits an OpenTelemetry HTTP client span per request. Subscribe to it by name:

```csharp
tracing.AddSource(RestLedgerClient.ActivitySourceName);
```

`Canton.Ledger.OpenTelemetry`'s `AddCantonLedgerInstrumentation()` registers this source alongside every other Canton client source.

## Related Packages

- `Canton.Ledger.Abstractions` — the transport-neutral Canton participant contracts this client implements
- `Canton.Ledger.Grpc.Client` — the gRPC client implementing the same surface
- `Canton.Ledger.Testing` — in-memory fakes for unit-testing against these contracts
- `Canton.Ledger.Rest` — the raw Refit surface this adapter is built on
