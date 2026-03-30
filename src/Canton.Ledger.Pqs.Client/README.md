# Canton.Ledger.Pqs.Client

Type-safe query client for the Canton Participant Query Store (PQS). Provides expression-based, SQL-injection-safe queries over active Daml contracts via PostgreSQL.

## Overview

PQS exposes the ledger state as a PostgreSQL database. This package provides a strongly-typed client that queries active contracts using the generated Daml C# bindings. Filter field names are derived from expressions against those bindings, and values are always parameterized — eliminating SQL injection by construction.

## Installation

```bash
dotnet add package Canton.Ledger.Pqs.Client
```

## Usage

### Basic Setup

```csharp
using Canton.Ledger.Pqs.Client;

var options = new PqsClientOptions
{
    ConnectionString = "Host=localhost;Database=pqs;Username=pqs;Password=pqs"
};

var pqsClient = new PqsClient(options);
```

### Querying All Active Contracts

```csharp
// Using generated template types from Daml.Codegen.CSharp
var agreements = await pqsClient.QueryAsync<Agreement>();
```

### Querying with Filters

```csharp
// Single field equality
var active = await pqsClient.QueryAsync<Agreement>(
    Filter.Field<Agreement>(a => a.Status, "Active"));

// OR condition
var myAgreements = await pqsClient.QueryAsync<Agreement>(
    Filter.Or(
        Filter.Field<Agreement>(a => a.Initiator, partyId),
        Filter.Field<Agreement>(a => a.Counterparty, partyId)));

// AND condition
var myActive = await pqsClient.QueryAsync<Agreement>(
    Filter.And(
        Filter.Field<Agreement>(a => a.Status, "Active"),
        Filter.Or(
            Filter.Field<Agreement>(a => a.Initiator, partyId),
            Filter.Field<Agreement>(a => a.Counterparty, partyId))));
```

### Fetching a Single Contract

```csharp
// By filter (returns first match or null)
var contract = await pqsClient.QueryOneAsync<Agreement>(
    Filter.Field<Agreement>(a => a.Initiator, partyId));

// By contract ID
var byId = await pqsClient.FetchByIdAsync<Agreement>(contractId);

// Check existence
var exists = await pqsClient.ExistsAsync<Agreement>(contractId);
```

### Dependency Injection

The recommended DI lifetime is **Singleton** — `PqsClient` holds only configuration state and relies on Npgsql's built-in connection pooling for database connections.

```csharp
// Using the extension method (recommended)
services.AddPqsClient(configuration.GetSection("Canton:Pqs"));

// Or using an action delegate
services.AddPqsClient(options =>
{
    options.ConnectionString = "Host=localhost;Database=pqs";
});

// Health check (uses the configured connection string)
services.AddHealthChecks().AddPqsClient(tags: ["database", "ready"]);
```

### OpenTelemetry Tracing

```csharp
tracing.AddSource(PqsClient.ActivitySourceName);
```

### Custom JSON Serialization

Override the default `JsonSerializerOptions` for contract payload deserialization using the action-based overload:

```csharp
services.AddPqsClient(options =>
{
    options.ConnectionString = "Host=localhost;Database=pqs";
    options.JsonSerializerOptions = new JsonSerializerOptions { /* ... */ };
});
```

## Related Packages

- `Canton.Ledger.Grpc.Client` - gRPC client for command submission
- `Daml.Codegen.CSharp.Runtime` - Runtime types for generated Daml contracts
- `Daml.Codegen.CSharp` - Code generator for Daml contracts
