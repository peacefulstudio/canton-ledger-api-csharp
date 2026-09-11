// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Canton.Ledger.Abstractions;
using Canton.Ledger.Grpc.Client.Raw;
using Canton.Ledger.Testing.Localnet;
using Com.Daml.Ledger.Api.V2;
using Daml.Codegen.Testing.Conformance;
using Daml.Codegen.Testing.Conformance.ContractKeys;
using Daml.Ledger.Abstractions;
using Daml.Runtime;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Outcomes;
using Daml.Runtime.Streams;
using Microsoft.Extensions.DependencyInjection;
using Peaceful.Canton.Localnet.Testing;
using Xunit;

namespace Canton.Ledger.Grpc.Client.Integration.Tests;

[Trait("Category", "Integration")]
public class ContractKeyRoundTripTests
{
    private static readonly TimeSpan SubscribeBudget = TimeSpan.FromSeconds(60);

    private const string SkipMessage =
        "Skipping: set CANTON_LOCALNET_A_VALIDATOR_1_GRPC_URL, _CLIENT_ID, _CLIENT_SECRET "
        + "(or the legacy un-namespaced CANTON_LOCALNET_* globals) and bring up the localnet "
        + "(canton-localnet up && canton-localnet wait-ready) to run this integration test.";

    [Fact]
    public async Task SubscribeAsync_populates_Created_Key_with_the_record_key_of_an_Account()
    {
        if (!EndpointDiscovery.IsLocalnetAvailable())
        {
            Assert.Skip(SkipMessage);
        }

        await using var fixture = LocalnetFixture.FromEnvironment();
        var bootstrap = await BootstrapAsync(fixture);
        await using var services = bootstrap.Services;
        await using var actAsRights = bootstrap.ActAsRights;
        var custodian = bootstrap.Signatory;
        var client = services.GetRequiredService<ICantonLedgerClient>();

        var label = $"account-{Guid.NewGuid():N}";
        var startOffset = await client.GetLedgerEndAsync(cancellationToken: TestContext.Current.CancellationToken);

        var createOutcome = await client.CreateAsync(
            new Account(custodian, label, 100L), TestContext.Current.CancellationToken);
        var createdCid = Assert.IsType<ExerciseOutcome<ContractId<Account>>.One>(createOutcome).Result;

        var created = await ReadCreatedAsync<Account>(client, custodian, createdCid.Value, startOffset);

        var key = created.Key;
        Assert.NotNull(key);
        Assert.IsType<DamlRecord>(key.Value);
        var contract = created.ToContract<Account, AccountKey>();
        Assert.Equal(new AccountKey(custodian, label), contract.Key.Value);
        Assert.NotNull(key.TemplateId);
        Assert.Equal(Account.TemplateId.ModuleName, key.TemplateId.ModuleName);
        Assert.Equal(Account.TemplateId.EntityName, key.TemplateId.EntityName);

        var wireKeyHash = await WireKeyHashAsync(services, custodian, createdCid.Value);
        Assert.False(string.IsNullOrWhiteSpace(wireKeyHash), "the participant sent no contract key hash");
        Assert.Equal(wireKeyHash, key.KeyHash);
        Assert.Equal(wireKeyHash, contract.Key.Hash);
    }

    [Fact]
    public async Task SubscribeAsync_populates_Created_Key_with_the_bare_Party_key_of_a_Steward()
    {
        if (!EndpointDiscovery.IsLocalnetAvailable())
        {
            Assert.Skip(SkipMessage);
        }

        await using var fixture = LocalnetFixture.FromEnvironment();
        var bootstrap = await BootstrapAsync(fixture);
        await using var services = bootstrap.Services;
        await using var actAsRights = bootstrap.ActAsRights;
        var steward = bootstrap.Signatory;
        var client = services.GetRequiredService<ICantonLedgerClient>();

        var startOffset = await client.GetLedgerEndAsync(cancellationToken: TestContext.Current.CancellationToken);

        var createOutcome = await client.CreateAsync(
            new Steward(steward, "charter"), TestContext.Current.CancellationToken);
        var createdCid = Assert.IsType<ExerciseOutcome<ContractId<Steward>>.One>(createOutcome).Result;

        var created = await ReadCreatedAsync<Steward>(client, steward, createdCid.Value, startOffset);

        var key = created.Key;
        Assert.NotNull(key);
        Assert.IsType<DamlParty>(key.Value);
        Assert.Equal(steward, Steward.Key.KeyDecoder(key.Value));
        Assert.NotNull(key.TemplateId);
        Assert.Equal(Steward.TemplateId.ModuleName, key.TemplateId.ModuleName);
        Assert.Equal(Steward.TemplateId.EntityName, key.TemplateId.EntityName);

        var wireKeyHash = await WireKeyHashAsync(services, steward, createdCid.Value);
        Assert.False(string.IsNullOrWhiteSpace(wireKeyHash), "the participant sent no contract key hash");
        Assert.Equal(wireKeyHash, key.KeyHash);
    }

    private static async Task<(ServiceProvider Services, ActAsRightsLease ActAsRights, Party Signatory)>
        BootstrapAsync(LocalnetFixture fixture)
    {
        var services = LocalnetLedgerServices.ForValidator(fixture, fixture.ValidatorUserId);
        try
        {
            await services.GetRequiredService<IAdminClient>().UploadDarAsync(
                await ContractKeysDarAsync(TestContext.Current.CancellationToken),
                cancellationToken: TestContext.Current.CancellationToken);

            var party = await fixture.AllocatePartyAsync(
                "cdg", cancellationToken: TestContext.Current.CancellationToken);
            var actAsRights = ActAsRightsLease.ForValidator(fixture);
            try
            {
                await actAsRights.GrantAsync(party.PartyId, TestContext.Current.CancellationToken);
                return (services, actAsRights, new Party(party.PartyId));
            }
            catch
            {
                await actAsRights.DisposeAsync();
                throw;
            }
        }
        catch
        {
            await services.DisposeAsync();
            throw;
        }
    }

    private static async Task<byte[]> ContractKeysDarAsync(CancellationToken cancellationToken)
    {
        await using var dar = ConformanceCorpus.OpenDar(ConformancePackage.ContractKeys);
        using var buffer = new MemoryStream();
        await dar.CopyToAsync(buffer, cancellationToken);
        return buffer.ToArray();
    }

    private static async Task<ContractStreamEvent<T>.Created> ReadCreatedAsync<T>(
        ICantonLedgerClient client,
        Party reader,
        string contractIdValue,
        LedgerOffset fromExclusive)
        where T : ITemplate, IDamlRecord<T>
    {
        var endOffset = await client.GetLedgerEndAsync(cancellationToken: TestContext.Current.CancellationToken);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cts.CancelAfter(SubscribeBudget);
        try
        {
            await foreach (var streamEvent in client.SubscribeAsync<T>(reader, fromExclusive, endOffset, cts.Token))
            {
                if (streamEvent is ContractStreamEvent<T>.Created created
                    && created.ContractId.Value == contractIdValue)
                {
                    return created;
                }

                FailOnStreamFault(streamEvent, contractIdValue);
            }
        }
        catch (OperationCanceledException) when (
            cts.IsCancellationRequested && !TestContext.Current.CancellationToken.IsCancellationRequested)
        {
            Assert.Fail(
                $"the {typeof(T).Name} subscribe stream over ({fromExclusive}, {endOffset}] did not deliver a "
                + $"Created event for {contractIdValue} within {SubscribeBudget.TotalSeconds:0}s");
        }

        Assert.Fail(
            $"the {typeof(T).Name} subscribe stream over ({fromExclusive}, {endOffset}] completed without a "
            + $"Created event for {contractIdValue}");
        return default;
    }

    private static void FailOnStreamFault<T>(ContractStreamEvent<T> streamEvent, string contractIdValue)
        where T : ITemplate, IDamlRecord<T>
    {
        if (streamEvent is ContractStreamEvent<T>.StreamError error)
        {
            Assert.Fail(
                $"the {typeof(T).Name} subscribe stream faulted before {contractIdValue} arrived: status "
                + $"{error.StatusCode}, category {error.Category?.ToString() ?? "none"} - {error.Message}");
        }

        if (streamEvent is ContractStreamEvent<T>.Unclassified unclassified)
        {
            Assert.Fail(
                $"the {typeof(T).Name} subscribe stream refused the event at offset {unclassified.Offset} as "
                + $"{unclassified.Kind} before {contractIdValue} arrived; a contract-key decode failure lands here");
        }
    }

    private static async Task<string?> WireKeyHashAsync(
        IServiceProvider services, Party reader, string contractIdValue)
    {
        var eventFormat = new EventFormat { Verbose = true };
        eventFormat.FiltersByParty.Add(reader.Id, new Filters());

        var invoker = services.GetRequiredService<IGrpcCallInvokerFactory>().CreateCallInvoker();
        var response = await new EventQueryService.EventQueryServiceClient(invoker).GetEventsByContractIdAsync(
            new GetEventsByContractIdRequest { ContractId = contractIdValue, EventFormat = eventFormat },
            cancellationToken: TestContext.Current.CancellationToken);

        var createdEvent = response.Created?.CreatedEvent;
        if (createdEvent is null)
        {
            Assert.Fail(
                $"EventQueryService returned no created event for {contractIdValue} as read by {reader.Id}, so "
                + "there is no wire contract key hash to compare the projected one against");
            return null;
        }

        var wireHash = createdEvent.ContractKeyHash;
        return wireHash.IsEmpty ? null : Convert.ToBase64String(wireHash.Span);
    }
}
