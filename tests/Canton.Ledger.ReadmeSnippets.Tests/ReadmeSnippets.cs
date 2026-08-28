// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

// This file mirrors the fenced C# snippets in the root README.md so they compile
// against the shipped Canton.Ledger.* surface in CI. The methods are never invoked
// (they would connect to a live participant / PQS database) — compilation is the
// guard. If a README snippet drifts off the real API again, this project stops
// building and CI fails.

using Canton.Ledger.Grpc.Client;
using Canton.Ledger.Abstractions;
using Canton.Ledger.Kernel.Authentication;
using Canton.Ledger.Pqs.Client;
using Daml.Runtime.Commands;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Outcomes;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Canton.Ledger.ReadmeSnippets.Tests;

public static class ReadmeSnippets
{
    // Mirrors README.md "Ledger Client Usage".
    public static async Task LedgerClientQuickStart()
    {
        var options = new LedgerClientOptions
        {
            GrpcAddress = "https://localhost:5001",
        };

        using var ledgerClient = new LedgerClient(options, ITokenProvider.None);
        using var adminClient = new AdminClient(options, ITokenProvider.None);

        var party = await adminClient.AllocatePartyAsync("alice");
        var submitter = new Party(party.Party);

        var outcome = await ledgerClient.TryCreateAsync(
            new MyTemplate("field1", "field2"),
            submitter);

        var contractId = outcome switch
        {
            ExerciseOutcome<ContractId<MyTemplate>>.One ok => ok.Result,
            ExerciseOutcome<ContractId<MyTemplate>>.DamlError err =>
                throw new InvalidOperationException(err.ErrorId),
            _ => throw new InvalidOperationException(outcome.GetType().Name),
        };

        _ = contractId;
    }

    // Mirrors README.md "PQS Client Usage".
    public static async Task PqsClientQuickStart()
    {
        var pqsOptions = new PqsClientOptions
        {
            ConnectionString = "Host=localhost;Database=pqs;Username=pqs;Password=pqs"
        };
        var pqsClient = new PqsClient(pqsOptions);

        var agreements = await pqsClient.QueryAsync<Agreement>();

        var partyId = "party::alice";
        var filtered = await pqsClient.QueryAsync<Agreement>(
            Filter.Or(
                Filter.Field<Agreement>(a => a.Initiator, partyId),
                Filter.Field<Agreement>(a => a.Counterparty, partyId)));

        var contractId = new ContractId<Agreement>("...");
        var contract = await pqsClient.FetchByIdAsync<Agreement>(contractId);
        var exists = await pqsClient.ExistsAsync<Agreement>(contractId);

        _ = agreements;
        _ = filtered;
        _ = contract;
        _ = exists;
    }

    // Mirrors README.md "Integration with Daml Code Generation".
    public static async Task CodegenIntegration()
    {
        var options = new LedgerClientOptions { GrpcAddress = "https://localhost:5001" };
        using var ledgerClient = new LedgerClient(options, ITokenProvider.None);

        var owner = new Party("Alice::1234...");

        var asset = new Asset(owner, "My Asset", 100m);
        var createOutcome = await ledgerClient.TryCreateAsync(asset, owner);
        var contractId = createOutcome switch
        {
            ExerciseOutcome<ContractId<Asset>>.One ok => ok.Result,
            _ => throw new InvalidOperationException(createOutcome.GetType().Name),
        };

        var command = ExerciseCommand.For(
            contractId,
            new ChoiceName("Transfer"),
            new Asset.Transfer(NewOwner: "Bob::5678...").ToRecord());

        var exerciseOutcome = await ledgerClient.TryExerciseAsync<ContractId<Asset>>(command, owner);

        var pqsClient = new PqsClient(new PqsClientOptions
        {
            ConnectionString = "Host=localhost;Database=pqs;Username=pqs;Password=pqs",
        });
        var assets = await pqsClient.QueryAsync<Asset>(
            Filter.Field<Asset>(a => a.Owner, owner.Id));

        _ = exerciseOutcome;
        _ = assets;
    }

    // Mirrors README.md "Authentication".
    public static void AuthenticationRegistration()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder().Build();

        services.AddCantonAuth(configuration.GetSection("Canton:Auth"));
        services.AddCantonStaticAuth("eyJ...");
    }
}
