# Canton Ledger API for C#

[![License](https://img.shields.io/badge/License-Apache_2.0-blue.svg)](https://opensource.org/licenses/Apache-2.0)
[![.NET](https://img.shields.io/badge/.NET-10.0-white.svg)](https://dotnet.microsoft.com/)

C# client libraries for interacting with Canton participant nodes via the Ledger API.

## Packages

| Package | Description | NuGet |
|---------|-------------|-------|
| `Canton.Ledger.Grpc` | Generated gRPC stubs from Canton Ledger API protos | [![NuGet](https://img.shields.io/nuget/v/Canton.Ledger.Grpc.svg)](https://nuget.org/packages/Canton.Ledger.Grpc) |
| `Canton.Ledger.Grpc.Client` | High-level client with `Daml.Codegen.CSharp.Runtime` integration | [![NuGet](https://img.shields.io/nuget/v/Canton.Ledger.Grpc.Client.svg)](https://nuget.org/packages/Canton.Ledger.Grpc.Client) |

## Quick Start

### Installation

```bash
dotnet add package Canton.Ledger.Grpc.Client
```

### Basic Usage

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

## Features

### Ledger Client
- Create contracts from generated Daml template types
- Exercise choices on contracts
- Submit batched commands atomically
- Full async/await support

### Admin Client
- Allocate and manage parties
- Create and manage users
- Grant and revoke user rights

## Integration with Daml Code Generation

This package integrates seamlessly with [Daml.Codegen.CSharp](https://github.com/peacefulstudio/daml-codegen-csharp):

```csharp
// Generate C# from your Daml contracts
// daml-codegen-csharp ./my-contracts.dar -o ./Generated

// Use generated types with the ledger client
var asset = new Asset(owner: party.Party, name: "My Asset", value: 100m);
var contractId = await ledgerClient.CreateAsync(asset, actAs: party.Party);

// Exercise choices
var command = ExerciseCommand.For(contractId, Asset.Transfer.Create("Bob::..."));
await ledgerClient.ExerciseAsync(command, actAs: party.Party);
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
| `Canton.Ledger.Pqs` | Participant Query Store client | Planned |

## License

Apache 2.0 - See [LICENSE](LICENSE) for details.

## Related Projects

- [daml-codegen-csharp](https://github.com/peacefulstudio/daml-codegen-csharp) - Generate C# from Daml contracts
- [Canton](https://github.com/digital-asset/canton) - Distributed ledger platform
