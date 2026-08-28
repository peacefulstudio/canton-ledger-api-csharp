# Canton Ledger API for C#

[![License](https://img.shields.io/badge/License-Apache_2.0-blue.svg)](https://opensource.org/licenses/Apache-2.0)
[![.NET](https://img.shields.io/badge/.NET-10.0-white.svg)](https://dotnet.microsoft.com/)
[![NuGet](https://img.shields.io/nuget/v/Canton.Ledger.Grpc.Client?include_prereleases)](https://www.nuget.org/packages/Canton.Ledger.Grpc.Client/)

C# client libraries for interacting with Canton participant nodes via the Ledger API (gRPC) and Participant Query Store (PQS).

## Packages

All packages are versioned in lockstep and published to [nuget.org](https://www.nuget.org/packages?q=Canton.Ledger).

| Package | Version | Description |
|---------|---------|-------------|
| [`Canton.Ledger.Abstractions`](https://www.nuget.org/packages/Canton.Ledger.Abstractions/) | [![NuGet](https://img.shields.io/nuget/v/Canton.Ledger.Abstractions?include_prereleases)](https://www.nuget.org/packages/Canton.Ledger.Abstractions/) | Transport-neutral Canton contract layer — `ICantonLedgerClient`, `IAdminClient`, `ITokenProvider` and `IPqsClient`, plus the neutral completion, connected-synchronizer, and reassignment type families both transports implement |
| [`Canton.Ledger.Kernel`](https://www.nuget.org/packages/Canton.Ledger.Kernel/) | [![NuGet](https://img.shields.io/nuget/v/Canton.Ledger.Kernel?include_prereleases)](https://www.nuget.org/packages/Canton.Ledger.Kernel/) | Transport-neutral client kernel the clients consume as peers — token providers (`ITokenProvider`, OAuth2 client-credentials/static/unauthenticated), the OpenTelemetry `ActivitySource` naming convention, and an opt-in Polly retry pipeline |
| [`Canton.Ledger.Grpc`](https://www.nuget.org/packages/Canton.Ledger.Grpc/) | [![NuGet](https://img.shields.io/nuget/v/Canton.Ledger.Grpc?include_prereleases)](https://www.nuget.org/packages/Canton.Ledger.Grpc/) | Generated gRPC stubs from Canton Ledger API protos |
| [`Canton.Ledger.Grpc.Client`](https://www.nuget.org/packages/Canton.Ledger.Grpc.Client/) | [![NuGet](https://img.shields.io/nuget/v/Canton.Ledger.Grpc.Client?include_prereleases)](https://www.nuget.org/packages/Canton.Ledger.Grpc.Client/) | High-level client with `Daml.Runtime` integration |
| [`Canton.Ledger.Rest`](https://www.nuget.org/packages/Canton.Ledger.Rest/) | [![NuGet](https://img.shields.io/nuget/v/Canton.Ledger.Rest?include_prereleases)](https://www.nuget.org/packages/Canton.Ledger.Rest/) | Raw Refit-generated surface over the Canton JSON Ledger API — experimental (`CANTONREST001`), typically consumed through `Canton.Ledger.Rest.Client` |
| [`Canton.Ledger.Rest.Client`](https://www.nuget.org/packages/Canton.Ledger.Rest.Client/) | [![NuGet](https://img.shields.io/nuget/v/Canton.Ledger.Rest.Client?include_prereleases)](https://www.nuget.org/packages/Canton.Ledger.Rest.Client/) | HTTP (JSON Ledger API) client — a full `ILedgerClient` / `ICantonLedgerClient` implementation over the transport-neutral interfaces |
| [`Canton.Ledger.Pqs.Client`](https://www.nuget.org/packages/Canton.Ledger.Pqs.Client/) | [![NuGet](https://img.shields.io/nuget/v/Canton.Ledger.Pqs.Client?include_prereleases)](https://www.nuget.org/packages/Canton.Ledger.Pqs.Client/) | Type-safe query client for the Participant Query Store (PQS) — the Npgsql-backed `IPqsClient` implementation |
| [`Canton.Ledger.OpenTelemetry`](https://www.nuget.org/packages/Canton.Ledger.OpenTelemetry/) | [![NuGet](https://img.shields.io/nuget/v/Canton.Ledger.OpenTelemetry?include_prereleases)](https://www.nuget.org/packages/Canton.Ledger.OpenTelemetry/) | OpenTelemetry SDK integration — registers the `LedgerClient`/`AdminClient`/`RestLedgerClient`/`PqsClient` `ActivitySource`s (plus Npgsql tracing) with a single `AddCantonLedgerInstrumentation()` call |
| [`Canton.Ledger.Testing`](https://www.nuget.org/packages/Canton.Ledger.Testing/) | [![NuGet](https://img.shields.io/nuget/v/Canton.Ledger.Testing?include_prereleases)](https://www.nuget.org/packages/Canton.Ledger.Testing/) | In-memory test doubles (`FakeLedgerClient`, `FakeAdminClient`, `FakePqsClient`, `FakeTokenProvider`) and builders for unit-testing against the client surfaces without a live participant |
| [`Daml.Runtime.Grpc`](https://www.nuget.org/packages/Daml.Runtime.Grpc/) | [![NuGet](https://img.shields.io/nuget/v/Daml.Runtime.Grpc?include_prereleases)](https://www.nuget.org/packages/Daml.Runtime.Grpc/) | Bridge between proto `Value`/`Record` and `Daml.Runtime` `DamlValue`/`DamlRecord` |

## Quick Start

### Installation

```bash
# gRPC Ledger API client
dotnet add package Canton.Ledger.Grpc.Client

# PQS query client
dotnet add package Canton.Ledger.Pqs.Client
```

### Ledger Client Usage

```csharp
using Canton.Ledger.Abstractions;
using Canton.Ledger.Grpc.Client;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Outcomes;

// Configure the client (auth is supplied via an ITokenProvider, not a token string)
var options = new LedgerClientOptions
{
    GrpcAddress = "https://localhost:5001",
};

// ITokenProvider.None runs unauthenticated — for local dev with an open participant.
// For production, register a token provider (see Authentication below) or use the
// dependency-injection path, which injects the ITokenProvider for you.
using var ledgerClient = new LedgerClient(options, ITokenProvider.None);
using var adminClient = new AdminClient(options, ITokenProvider.None);

// Allocate a party
var party = await adminClient.AllocatePartyAsync("alice");
var submitter = new Party(party.Party);

// Create a contract from a generated Daml template type.
// TryCreateAsync returns ExerciseOutcome<ContractId<T>> — switch on it instead of catching.
var outcome = await ledgerClient.TryCreateAsync(
    new MyTemplate("field1", "field2"),
    submitter);

var contractId = outcome switch
{
    ExerciseOutcome<ContractId<MyTemplate>>.One ok => ok.Result,
    ExerciseOutcome<ContractId<MyTemplate>>.DamlError err => throw new InvalidOperationException(err.ErrorId),
    _ => throw new InvalidOperationException(outcome.GetType().Name),
};
```

### PQS Client Usage

```csharp
using Canton.Ledger.Abstractions;
using Canton.Ledger.Pqs.Client;
using Daml.Runtime.Contracts;

// Configure the PQS client (single-argument constructor)
var pqsOptions = new PqsClientOptions
{
    ConnectionString = "Host=localhost;Database=pqs;Username=pqs;Password=pqs"
};
var pqsClient = new PqsClient(pqsOptions);

// Query all active contracts of a template type
var agreements = await pqsClient.QueryAsync<Agreement>();

// Query with type-safe filters — field names resolve from codegen [DamlField] metadata
var partyId = "party::alice";
var filtered = await pqsClient.QueryAsync<Agreement>(
    Filter.Or(
        Filter.Field<Agreement>(a => a.Initiator, partyId),
        Filter.Field<Agreement>(a => a.Counterparty, partyId)));

// Fetch a single contract by ID
var contractId = new ContractId<Agreement>("...");
var contract = await pqsClient.FetchByIdAsync<Agreement>(contractId);

// Check if a contract exists
var exists = await pqsClient.ExistsAsync<Agreement>(contractId);
```

### Authentication

`Canton.Ledger.Kernel` ships as a dependency of `Canton.Ledger.Grpc.Client`. Register a token provider before adding the clients:

```csharp
using Canton.Ledger.Kernel.Authentication;

// OAuth2 client-credentials with automatic refresh and caching
services.AddCantonAuth(configuration.GetSection("Canton:Auth"));

// ...or a fixed token for short-lived processes
services.AddCantonStaticAuth("eyJ...");
```

```json
{
  "Canton": {
    "Auth": {
      "Domain": "my-tenant.eu.auth0.com",
      "ClientId": "my-client-id",
      "ClientSecret": "my-client-secret",
      "Audience": "https://canton.network/"
    }
  }
}
```

When no `ITokenProvider` is registered, the clients run unauthenticated (`ITokenProvider.None`) and log a warning at construction. See the [`Canton.Ledger.Kernel` README](src/Canton.Ledger.Kernel/README.md) for the full options reference, including custom token endpoints (e.g. Keycloak).

## Features

### Ledger Client (`Canton.Ledger.Grpc.Client`)
- Create contracts from generated Daml template types
- Exercise choices on contracts
- Submit batched commands atomically
- Full async/await support

### Admin Client (`Canton.Ledger.Grpc.Client`)
- Allocate and manage parties
- Create and manage users
- Grant and revoke user rights

### PQS Client (`Canton.Ledger.Pqs.Client`)
- Query active contracts by template type
- Type-safe filters using C# expressions — field names derived from generated bindings
- Parameterized SQL queries — no SQL injection by construction
- Composable `Filter.Or` / `Filter.And` combinators
- OpenTelemetry tracing via `ActivitySource`

### Client kernel (`Canton.Ledger.Kernel`)
- `Authentication`: OAuth2 client-credentials flow with thread-safe TTL token caching and automatic refresh
- Static token and unauthenticated modes behind a single `ITokenProvider` abstraction
- `IServiceCollection` integration with options validation at startup
- `Telemetry`: the shared `ActivitySource` naming convention; `Resilience`: the opt-in Polly retry pipeline

## Integration with Daml Code Generation

These packages integrate seamlessly with [Daml.Codegen.CSharp](https://github.com/peacefulstudio/daml-codegen-csharp):

```csharp
// Generate C# from your Daml contracts
// daml-codegen-csharp ./my-contracts.dar -o ./Generated

using Canton.Ledger.Abstractions;
using Canton.Ledger.Grpc.Client;
using Daml.Runtime.Commands;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Outcomes;

var options = new LedgerClientOptions { GrpcAddress = "https://localhost:5001" };
using var ledgerClient = new LedgerClient(options, ITokenProvider.None);

var owner = new Party("Alice::1234...");

// Create a contract from a generated template type
var asset = new Asset(owner, "My Asset", 100m);
var createOutcome = await ledgerClient.TryCreateAsync(asset, owner);
var contractId = createOutcome switch
{
    ExerciseOutcome<ContractId<Asset>>.One ok => ok.Result,
    _ => throw new InvalidOperationException(createOutcome.GetType().Name),
};

// Exercise a choice — ExerciseCommand.For takes the contract id, the choice name,
// and the encoded choice argument (a DamlValue, here a record via ToRecord()).
var command = ExerciseCommand.For(
    contractId,
    new ChoiceName("Transfer"),
    new Asset.Transfer(NewOwner: "Bob::5678...").ToRecord());

var exerciseOutcome = await ledgerClient.TryExerciseAsync<ContractId<Asset>>(command, owner);

// Query the same contracts via PQS
var pqsClient = new PqsClient(new PqsClientOptions
{
    ConnectionString = "Host=localhost;Database=pqs;Username=pqs;Password=pqs",
});
var assets = await pqsClient.QueryAsync<Asset>(
    Filter.Field<Asset>(a => a.Owner, owner.Id));
```

## Configuration

See the [configuration reference](docs/public/configuration-reference.md) for every client option and its default (`Canton:Ledger`, `Canton:Auth`, `Canton:Pqs`, `Canton:Rest`), environment-variable naming, the authentication precedence rules, `PostConfigure` hooks, and health-check registration.

## Architecture

See the [architecture overview](docs/public/architecture-overview.md) for how the codegen pipeline, the `Daml.Runtime` library, and the `Canton.Ledger.*` client packages fit together.

## Canton Version Compatibility

This library targets Canton Ledger API v2. The proto files are automatically downloaded from Maven Central during build.

| Library Version | Canton Version |
|-----------------|----------------|
| 0.4.1 and later | 3.5.x |
| 0.4.0 | 3.4.x |
| 0.2.x | 3.4.x |
| 0.1.x | 3.4.x |

From `0.4.1` the vendored protos and JSON Ledger API spec are pinned at Canton `3.5.9`, and the library supports Canton 3.5 only — running it against a 3.4.x participant is untested and unsupported. Any `3.5.x` patch release is fine: the vendored surface is stable within the minor.

From `0.4.x`, the `Canton.Ledger.*` package minor tracks the `Daml.Runtime` / `Daml.Ledger.Abstractions` minor line — package `0.N.x` embeds `Daml.* 0.N.x` — so the Daml runtime line is legible straight off the package version (the `0.3.x` line is skipped to realign). Patch and `-preview.N` suffixes evolve independently.

## Building from Source

```bash
# Clone the repository
git clone https://github.com/peacefulstudio/canton-ledger-api-csharp.git
cd canton-ledger-api-csharp

# Build
dotnet build

# Run tests
dotnet test

# Create NuGet packages
dotnet pack -c Release
```

## Contributing

Contributions are welcome from anyone in the Canton and Daml communities. See
[CONTRIBUTING.md](CONTRIBUTING.md) for the dev setup, the testing requirements,
the branch model, and the PR checklist.

## Project stewardship

`Canton.Ledger.*` is currently developed and maintained by **Peaceful Studio OÜ**
(Estonia). The project is licensed under Apache-2.0 with the explicit intent of
community ownership: if and when adoption warrants neutral governance, Peaceful
Studio commits to transferring this repository to a community-led organisation
under the same license terms. Contributions welcome from anywhere in the Canton
and Daml ecosystems; no CLA required.

## License

Apache-2.0. Copyright 2026 Peaceful Studio OÜ. See [LICENSE](LICENSE) and [NOTICE](NOTICE)
for details.

## Related Projects

- [daml-codegen-csharp](https://github.com/peacefulstudio/daml-codegen-csharp) - Generate C# from Daml contracts
- [Canton](https://github.com/digital-asset/canton) - Distributed ledger platform
