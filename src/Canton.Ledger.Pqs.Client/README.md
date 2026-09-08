# Canton.Ledger.Pqs.Client

Type-safe query client for the Canton Participant Query Store (PQS). Provides expression-based, SQL-injection-safe queries over active Daml contracts via PostgreSQL.

## Overview

PQS exposes the ledger state as a PostgreSQL database. This package provides a strongly-typed client that queries active contracts using the generated Daml C# bindings. Filter field names are derived from expressions against those bindings, and values are always parameterized — eliminating SQL injection by construction.

The query surface itself — `IPqsClient`, the `Filter`/`PqsFilter` DSL, `PqsPage` and `InterfaceContract<TInterface, TView>` — is declared in `Canton.Ledger.Abstractions`, so code written against `IPqsClient` (and the `FakePqsClient` in `Canton.Ledger.Testing`) needs no PostgreSQL dependency. This package supplies `PqsClient`, the Npgsql-backed implementation, plus its options, health check and DI wiring.

## Installation

```bash
dotnet add package Canton.Ledger.Pqs.Client
```

## Usage

### Basic Setup

The client is entered through dependency injection: register it on an `IServiceCollection`, then
resolve `IPqsClient`. The container binds and validates `PqsClientOptions` at startup and picks up a
registered `NpgsqlDataSource` when one is present.

```csharp
using Canton.Ledger.Abstractions;
using Canton.Ledger.Pqs.Client;
using Microsoft.Extensions.DependencyInjection;

var services = new ServiceCollection();
services.AddPqsClient(options =>
    options.ConnectionString = "Host=localhost;Database=pqs;Username=pqs;Password=pqs");

await using var provider = services.BuildServiceProvider();
var pqsClient = provider.GetRequiredService<IPqsClient>();
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

`Canton.Ledger.OpenTelemetry` registers every Canton client source at once:

```csharp
tracing.AddCantonLedgerInstrumentation();
```

To register this source by hand, take the name from `Canton.Ledger.Kernel` — no reference to this package required:

```csharp
tracing.AddSource(LedgerActivitySourceNames.PqsClient);
```

### Custom JSON Serialization

`PqsClientOptions.JsonSerializerOptions` *replaces* the client's payload defaults rather than adding to
them, so start from `PqsClientOptions.CreateDefaultJsonSerializerOptions()` whenever you need a converter
of your own alongside them — a variant factory for an abstract Daml type `System.Text.Json` cannot
construct, say. It returns a fresh, mutable instance on every call:

```csharp
services.AddPqsClient(options =>
{
    options.ConnectionString = "Host=localhost;Database=pqs";

    var jsonOptions = PqsClientOptions.CreateDefaultJsonSerializerOptions();
    jsonOptions.Converters.Add(new MyVariantConverterFactory());
    options.JsonSerializerOptions = jsonOptions;
});
```

The defaults it carries are case-insensitive property matching for PQS's camelCase keys,
`JsonNumberHandling.AllowReadingFromString` for Daml `Numeric`, and a `JsonStringEnumConverter` for Daml
enums. Rebuilding them by hand and missing one fails at query time rather than at build time. To replace
them outright instead, assign a `new JsonSerializerOptions { /* ... */ }`; to keep exactly the defaults,
leave the property `null`.

## Related Packages

- `Canton.Ledger.Abstractions` - declares `IPqsClient`, `Filter`/`PqsFilter`, `PqsPage` and `InterfaceContract<TInterface, TView>`
- `Canton.Ledger.Testing` - `FakePqsClient`, an in-memory `IPqsClient` for unit tests
- `Canton.Ledger.Grpc.Client` - gRPC client for command submission
- `Daml.Runtime` - Runtime types for generated Daml contracts
- `Daml.Codegen.CSharp` - Code generator for Daml contracts
