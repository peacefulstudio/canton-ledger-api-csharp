# Canton.Ledger.Grpc.Client

High-level gRPC client for the Canton Ledger API with integration to `Daml.Codegen.CSharp.Runtime` types.

## Overview

This package provides a strongly-typed, easy-to-use client for interacting with Canton participant nodes. It wraps the low-level gRPC stubs from `Canton.Ledger.Grpc` and integrates with the Daml code generation runtime.

## Installation

```bash
dotnet add package Canton.Ledger.Grpc.Client
```

## Usage

### Basic Setup

```csharp
using Canton.Ledger.Grpc.Client;

var options = new LedgerClientOptions
{
    GrpcAddress = "https://localhost:5001",
    AccessToken = "your-jwt-token"  // Optional
};

using var ledgerClient = new LedgerClient(options);
using var adminClient = new AdminClient(options);
```

### Creating Contracts

```csharp
// Using generated template types from Daml.Codegen.CSharp
var asset = new Asset("Alice", "My Asset", 100m);

var contractId = await ledgerClient.CreateAsync(
    asset,
    actAs: "Alice::1234...",
    workflowId: "create-asset");
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

### Party Management

```csharp
// Allocate a new party
var party = await adminClient.AllocatePartyAsync("alice-hint");
Console.WriteLine($"Allocated party: {party.Party}");

// Create a user with rights
var user = await adminClient.CreateUserAsync(
    userId: "alice-user",
    primaryParty: party.Party,
    rights: new[]
    {
        new UserRight.ActAs(party.Party),
        new UserRight.ReadAs(party.Party)
    });
```

### User Management

```csharp
// Grant additional rights
await adminClient.GrantUserRightsAsync(
    "alice-user",
    new[] { new UserRight.ReadAs("Bob::5678...") });

// List all users
var users = await adminClient.ListUsersAsync();
```

## Dependency Injection

The recommended DI lifetime is **Singleton** — gRPC clients share the underlying `GrpcChannel` lifetime.

```csharp
// Using extension methods (recommended)
services.AddLedgerClient(configuration.GetSection("Canton:Ledger"));
services.AddAdminClient(configuration.GetSection("Canton:Ledger"));

// Or using action delegates
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

- `Canton.Ledger.Grpc` - Low-level gRPC stubs
- `Daml.Codegen.CSharp.Runtime` - Runtime types for generated Daml contracts
- `Daml.Codegen.CSharp` - Code generator for Daml contracts
