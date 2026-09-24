# Canton.Ledger.OpenTelemetry

Opt-in OpenTelemetry wiring for the Canton Ledger API clients. The gRPC (`Canton.Ledger.Grpc.Client`), JSON (`Canton.Ledger.Rest.Client`), and PQS (`Canton.Ledger.Pqs.Client`) clients emit only BCL `System.Diagnostics.Activity` spans and take no OpenTelemetry dependency — this package is the only assembly in the repo that references the OpenTelemetry SDK, and a consumer who never references it pays no OpenTelemetry cost at all.

The source names come from `Canton.Ledger.Kernel`, which is this package's only project dependency: enabling tracing never drags a concrete client assembly — or its transport stack — into a host that does not use it.

## Key Types

| Type | Purpose |
|------|---------|
| `OpenTelemetry.Trace.CantonLedgerTracerProviderBuilderExtensions.AddCantonLedgerInstrumentation()` | `TracerProviderBuilder` extension registering the `LedgerClient`/`AdminClient`/`RestLedgerClient`/`PqsClient` `ActivitySource`s plus Npgsql's own instrumentation |

## Usage

```csharp
using OpenTelemetry.Trace;

using var tracerProvider = Sdk.CreateTracerProviderBuilder()
    .AddCantonLedgerInstrumentation()
    .AddOtlpExporter()
    .Build();
```

`AddCantonLedgerInstrumentation()` is equivalent to:

```csharp
builder
    .AddSource([.. LedgerActivitySourceNames.All])
    .AddNpgsql();
```

W3C trace-context propagation needs no extra code: the BCL `SocketsHttpHandler` injects `traceparent`/`tracestate` on every outgoing HTTP request once a sampled `Activity` is current, and `Grpc.Net.Client` rides `HttpClient`, so gRPC calls carry it too. Enabling tracing on the sources above is what makes the client spans recorded (and therefore propagated); PostgreSQL is a trace leaf — Npgsql emits a span but propagates no context further downstream.

## Attributes

Client spans carry OpenTelemetry semantic-convention attributes (`rpc.system`, `rpc.service`, `rpc.method`, `http.request.method`, `http.response.status_code`, `url.full`, `server.address`, `server.port`, `error.type`) plus three custom buckets: `daml.*` for Daml-LF source concepts (`daml.template_id`, `daml.choice`, `daml.contract_id`, `daml.package_id`) and `canton.*` for Ledger-API/operational concepts (`canton.offset`, `canton.from_offset`, `canton.submitter.act_as`/`read_as`, `canton.party_id`, `canton.participant_id`, `canton.party_id_hint`, `canton.user_id`, `canton.submission_id`, `canton.update_id`, `canton.traffic_cost_bytes`, `canton.pqs.result_count`), and `retry.*` on the retry pipeline's own spans (`retry.attempt`, `retry.delay_ms`).

The party and contract identifiers among them — `canton.submitter.act_as`, `canton.submitter.read_as`, `canton.party_id`, `canton.party_id_hint` and `daml.contract_id` — are **off by default**: anyone with read access to a tracing backend would otherwise see who transacted on which contract. A host whose backend is trusted with that data opts in on the gRPC client's options:

```csharp
services.AddLedgerClient(options =>
{
    options.GrpcAddress = "https://participant.example:5001";
    options.EmitPartyAndContractSpanTags = true;
});
```

or `Canton:Ledger:EmitPartyAndContractSpanTags` set to `true` in configuration. The JSON client's `url.full` never carries the request's query string or user info, whatever the setting, since a query such as `?party=` names a party.

Every one of those custom names is a constant on `Canton.Ledger.Kernel.Telemetry.LedgerActivityTagNames`, with `All` over the whole set — name one from there rather than retyping it into a dashboard query, a sampling rule or a redaction filter.

Telemetry shape is pre-1.0 and may change in any preview release.
