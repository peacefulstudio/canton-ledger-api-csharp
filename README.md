# Canton Ledger API for C#

[![License](https://img.shields.io/badge/License-Apache_2.0-blue.svg)](https://opensource.org/licenses/Apache-2.0)
[![.NET](https://img.shields.io/badge/.NET-10.0-white.svg)](https://dotnet.microsoft.com/)
<!-- Static badge — repo is private, so shields.io's `github/v/release/...` dynamic endpoint can't read it. Bump on each release. -->
[![Release](https://img.shields.io/badge/Release-v0.1.4-blue.svg)](https://github.com/peacefulstudio/canton-ledger-api-csharp/releases/latest)

C# client libraries for interacting with Canton participant nodes via the Ledger API (gRPC) and Participant Query Store (PQS).

## Packages

| Package | Description |
|---------|-------------|
| [`Canton.Ledger.Grpc`](https://github.com/peacefulstudio/canton-ledger-api-csharp/pkgs/nuget/Canton.Ledger.Grpc) | Generated gRPC stubs from Canton Ledger API protos |
| [`Canton.Ledger.Grpc.Client`](https://github.com/peacefulstudio/canton-ledger-api-csharp/pkgs/nuget/Canton.Ledger.Grpc.Client) | High-level client with `Daml.Runtime` integration |
| [`Canton.Ledger.Pqs.Client`](https://github.com/peacefulstudio/canton-ledger-api-csharp/pkgs/nuget/Canton.Ledger.Pqs.Client) | Type-safe query client for the Participant Query Store (PQS) |

Packages are published to [GitHub Packages](https://github.com/orgs/peacefulstudio/packages?repo_name=canton-ledger-api-csharp).

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
using Canton.Ledger.Grpc.Client;

// Configure the client
var options = new LedgerClientOptions
{
    GrpcAddress = "https://localhost:5001",
    AccessToken = "your-jwt-token"  // Optional
};

// Create clients
using var ledgerClient = new LedgerClient(options);
using var adminClient = new AdminClient(options);

// Allocate a party
var party = await adminClient.AllocatePartyAsync("alice");
Console.WriteLine($"Party: {party.Party}");

// Create a contract (using generated types from Daml.Codegen.CSharp)
var contractId = await ledgerClient.CreateAsync(
    new MyTemplate("field1", "field2"),
    actAs: party.Party);
```

### PQS Client Usage

```csharp
using Canton.Ledger.Pqs.Client;

// Configure the PQS client
var pqsOptions = new PqsClientOptions
{
    ConnectionString = "Host=localhost;Database=pqs;Username=pqs;Password=pqs"
};
var pqsClient = new PqsClient(pqsOptions, logger);

// Query all active contracts of a template type
var agreements = await pqsClient.QueryAsync<Agreement>();

// Query with type-safe filters
var filtered = await pqsClient.QueryAsync<Agreement>(
    Filter.Or(
        Filter.Field<Agreement>(a => a.Initiator, partyId),
        Filter.Field<Agreement>(a => a.Counterparty, partyId)));

// Fetch a single contract by ID
var contract = await pqsClient.FetchByIdAsync<Agreement>(contractId);

// Check if a contract exists
var exists = await pqsClient.ExistsAsync<Agreement>(contractId);
```

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

## Integration with Daml Code Generation

These packages integrate seamlessly with [Daml.Codegen.CSharp](https://github.com/peacefulstudio/daml-codegen-csharp):

```csharp
// Generate C# from your Daml contracts
// daml-codegen-csharp ./my-contracts.dar -o ./Generated

// Use generated types with the ledger client
var asset = new Asset(owner: party.Party, name: "My Asset", value: 100m);
var contractId = await ledgerClient.CreateAsync(asset, actAs: party.Party);

// Exercise choices
var command = ExerciseCommand.For(contractId, Asset.Transfer.Create("Bob::..."));
await ledgerClient.ExerciseAsync(command, actAs: party.Party);

// Query the same contracts via PQS
var assets = await pqsClient.QueryAsync<Asset>(
    Filter.Field<Asset>(a => a.Owner, party.Party));
```

## Canton Version Compatibility

This library targets Canton Ledger API v2. The proto files are automatically downloaded from Maven Central during build.

| Library Version | Canton Version |
|-----------------|----------------|
| 0.1.x | 3.4.x |

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

## Future Packages

| Package | Description | Status |
|---------|-------------|--------|
| `Canton.Ledger.Rest` | REST/JSON API client | Planned |

## License

Apache 2.0 - See [LICENSE](LICENSE) for details.

## Related Projects

- [daml-codegen-csharp](https://github.com/peacefulstudio/daml-codegen-csharp) - Generate C# from Daml contracts
- [Canton](https://github.com/digital-asset/canton) - Distributed ledger platform
