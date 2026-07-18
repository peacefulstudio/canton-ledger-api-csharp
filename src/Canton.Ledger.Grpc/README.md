# Canton.Ledger.Grpc

Generated gRPC client stubs for the Canton Ledger API.

## Overview

This package provides low-level gRPC client stubs generated from the official Canton Ledger API protobuf definitions (Canton Ledger API version 3.4.11). It enables direct communication with Canton participant nodes.

All generated types live under the `Com.Daml.Ledger.Api.V2` namespace (admin services under `Com.Daml.Ledger.Api.V2.Admin`), matching the `csharp_namespace` set in the upstream proto files. No type lives under `Canton.Ledger.Grpc` — that is only the NuGet package and assembly name.

## Services Included

### Command Services
- `CommandService` - Submit commands and wait for results
- `CommandSubmissionService` - Async command submission
- `CommandCompletionService` - Track command completions

### Query Services
- `StateService` - Query active contracts and ledger state
- `UpdateService` - Stream ledger updates
- `EventQueryService` - Query historical events

### Admin Services
- `PartyManagementService` - Allocate and manage parties
- `UserManagementService` - Manage ledger users and rights
- `PackageManagementService` - Upload and manage Daml packages

## Usage

```csharp
using Com.Daml.Ledger.Api.V2;
using Com.Daml.Ledger.Api.V2.Admin;
using Grpc.Net.Client;

// Create a gRPC channel
var channel = GrpcChannel.ForAddress("https://localhost:5001");

// Create service clients
var commandService = new CommandService.CommandServiceClient(channel);
var partyService = new PartyManagementService.PartyManagementServiceClient(channel);

// Submit a command
var response = await commandService.SubmitAndWaitAsync(new SubmitAndWaitRequest
{
    Commands = new Commands { ... }
});
```

## Version

Generated from Canton Ledger API protos version 3.4.11 (downloaded from Maven Central at build time).

## Related Packages

- `Canton.Ledger.Grpc.Client` - High-level client with integration to `Daml.Runtime`
- `Daml.Runtime` - Runtime types for generated Daml contracts
