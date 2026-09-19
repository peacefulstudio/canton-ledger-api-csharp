// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Canton.Ledger.Rest.Client.Raw;
using Daml.Codegen.Testing.Conformance;
using Daml.Codegen.Testing.Conformance.ContractKeys;
using Daml.Ledger.Abstractions;
using Daml.Runtime;
using Daml.Runtime.Commands;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Outcomes;
using Daml.Runtime.Streams;
using Peaceful.Canton.Localnet.Testing;
using Xunit;

#pragma warning disable CANTONREST001

namespace Canton.Ledger.Rest.Client.Integration.Tests;

/// <summary>
/// LocalNet coverage for the REST transport's contract-key round trip: a keyed contract created over
/// REST comes back through <c>SubscribeAsync</c> carrying a key whose value decodes to the key the
/// create used, and whose <c>KeyHash</c> equals the <c>contractKeyHash</c> the participant sent for
/// the same contract, read raw through the JSON Ledger API's <c>events-by-contract-id</c> endpoint.
/// The gRPC half lives in <c>Canton.Ledger.Grpc.Client.Integration.Tests.ContractKeyRoundTripTests</c>.
/// </summary>
/// <remarks>
/// Only <c>Created</c> and <c>Assigned</c> carry a key, so the archive half of each round trip
/// correlates through the contract id captured at create. Canton 3.x does not make contract keys
/// unique, so nothing here asserts that a key identifies at most one active contract.
/// </remarks>
[Trait("Category", "Integration")]
public class RestContractKeyRoundTripTests
{
    private static readonly TimeSpan SubscribeBudget = TimeSpan.FromSeconds(60);

    [Fact]
    public async Task TryCreateAsync_reads_back_the_transaction_that_created_a_record_keyed_Account()
    {
        await using var lane = await RestConformanceLane.OpenAsync(TestContext.Current.CancellationToken);
        var custodian = await NewSignatoryAsync(lane, TestContext.Current.CancellationToken);

        var outcome = await lane.LedgerClient.TryCreateAsync(
            new Account(custodian, NewAccountLabel(), 100L),
            custodian,
            cancellationToken: TestContext.Current.CancellationToken);

        var accountCid = AssertReadBack(outcome);
        Assert.False(string.IsNullOrWhiteSpace(accountCid.Value), "the participant returned an empty contract id");
    }

    [Fact]
    public async Task TryCreateAsync_reads_back_the_transaction_that_created_a_bare_Party_keyed_Steward()
    {
        await using var lane = await RestConformanceLane.OpenAsync(TestContext.Current.CancellationToken);
        var steward = await NewSignatoryAsync(lane, TestContext.Current.CancellationToken);

        var outcome = await lane.LedgerClient.TryCreateAsync(
            new Steward(steward, "charter"), steward, cancellationToken: TestContext.Current.CancellationToken);

        var stewardCid = AssertReadBack(outcome);
        Assert.False(string.IsNullOrWhiteSpace(stewardCid.Value), "the participant returned an empty contract id");
    }

    [Fact]
    public async Task SubscribeAsync_populates_Created_Key_with_the_record_key_of_an_Account()
    {
        await using var lane = await RestConformanceLane.OpenAsync(TestContext.Current.CancellationToken);
        var custodian = await NewSignatoryAsync(lane, TestContext.Current.CancellationToken);

        var label = NewAccountLabel();
        var startOffset = await lane.LedgerClient.GetLedgerEndAsync(
            cancellationToken: TestContext.Current.CancellationToken);

        var createOutcome = await lane.LedgerClient.TryCreateAsync(
            new Account(custodian, label, 100L),
            custodian,
            cancellationToken: TestContext.Current.CancellationToken);
        var accountCid = AssertReadBack(createOutcome);
        await ArchiveAsync(lane, accountCid.ArchiveCommand(), custodian);

        var created = await ReadRoundTripAsync<Account>(lane, custodian, accountCid.Value, startOffset);

        var key = created.Key;
        Assert.NotNull(key);
        Assert.IsType<DamlRecord>(key.Value);
        var contract = created.ToContract<Account, AccountKey>();
        Assert.Equal(new AccountKey(custodian, label), contract.Key.Value);
        Assert.NotNull(key.TemplateId);
        Assert.Equal(Account.TemplateId.ModuleName, key.TemplateId.ModuleName);
        Assert.Equal(Account.TemplateId.EntityName, key.TemplateId.EntityName);

        var wireKeyHash = await WireKeyHashAsync(lane, custodian, accountCid.Value);
        Assert.False(string.IsNullOrWhiteSpace(wireKeyHash), "the participant sent no contract key hash");
        Assert.Equal(wireKeyHash, key.KeyHash);
        Assert.Equal(wireKeyHash, contract.Key.Hash);
    }

    [Fact]
    public async Task SubscribeAsync_populates_Created_Key_with_the_bare_Party_key_of_a_Steward()
    {
        await using var lane = await RestConformanceLane.OpenAsync(TestContext.Current.CancellationToken);
        var steward = await NewSignatoryAsync(lane, TestContext.Current.CancellationToken);

        var startOffset = await lane.LedgerClient.GetLedgerEndAsync(
            cancellationToken: TestContext.Current.CancellationToken);

        var createOutcome = await lane.LedgerClient.TryCreateAsync(
            new Steward(steward, "charter"), steward, cancellationToken: TestContext.Current.CancellationToken);
        var stewardCid = AssertReadBack(createOutcome);
        await ArchiveAsync(lane, stewardCid.ArchiveCommand(), steward);

        var created = await ReadRoundTripAsync<Steward>(lane, steward, stewardCid.Value, startOffset);

        var key = created.Key;
        Assert.NotNull(key);
        Assert.IsType<DamlParty>(key.Value);
        Assert.Equal(steward, Steward.Key.KeyDecoder(key.Value));
        Assert.NotNull(key.TemplateId);
        Assert.Equal(Steward.TemplateId.ModuleName, key.TemplateId.ModuleName);
        Assert.Equal(Steward.TemplateId.EntityName, key.TemplateId.EntityName);

        var wireKeyHash = await WireKeyHashAsync(lane, steward, stewardCid.Value);
        Assert.False(string.IsNullOrWhiteSpace(wireKeyHash), "the participant sent no contract key hash");
        Assert.Equal(wireKeyHash, key.KeyHash);
    }

    private static string NewAccountLabel() => $"account-{Guid.NewGuid():N}";

    private static ContractId<T> AssertReadBack<T>(ExerciseOutcome<ContractId<T>> outcome)
        where T : ITemplate
    {
        if (outcome is ExerciseOutcome<ContractId<T>>.InfraError rejection)
        {
            Assert.Fail(
                $"the {typeof(T).Name} create committed but its transaction did not read back: "
                + $"{rejection.StatusCode} {rejection.Message}");
        }

        return Assert.IsType<ExerciseOutcome<ContractId<T>>.One>(outcome).Result;
    }

    private static async Task ArchiveAsync(RestConformanceLane lane, ICommand archive, Party submitter)
    {
        var result = await lane.LedgerClient.SubmitAndWaitAsync(
            CommandsSubmission.Single(archive, submitter),
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.False(string.IsNullOrWhiteSpace(result.UpdateId), "the archive submission returned no update id");
    }

    private static async Task<ContractStreamEvent<T>.Created> ReadRoundTripAsync<T>(
        RestConformanceLane lane, Party reader, string contractIdValue, LedgerOffset fromExclusive)
        where T : ITemplate, IDamlRecord<T>
    {
        var endOffset = await lane.LedgerClient.GetLedgerEndAsync(
            cancellationToken: TestContext.Current.CancellationToken);

        ContractStreamEvent<T>.Created? created = null;
        ContractStreamEvent<T>.Archived? archived = null;

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cts.CancelAfter(SubscribeBudget);
        try
        {
            await foreach (var streamEvent in lane.LedgerClient.SubscribeAsync<T>(
                reader, fromExclusive, endOffset, cts.Token))
            {
                FailOnStreamFault(streamEvent, contractIdValue);

                switch (streamEvent)
                {
                    case ContractStreamEvent<T>.Created candidate
                        when candidate.ContractId.Value == contractIdValue:
                        created ??= candidate;
                        break;
                    case ContractStreamEvent<T>.Archived candidate
                        when candidate.ContractId.Value == contractIdValue:
                        archived ??= candidate;
                        break;
                }

                if (created is not null && archived is not null) return created;
            }
        }
        catch (OperationCanceledException) when (
            cts.IsCancellationRequested && !TestContext.Current.CancellationToken.IsCancellationRequested)
        {
            Assert.Fail(
                $"the {typeof(T).Name} subscribe stream over ({fromExclusive}, {endOffset}] did not deliver both a "
                + $"Created and an Archived event for {contractIdValue} within {SubscribeBudget.TotalSeconds:0}s");
        }

        Assert.Fail(
            $"the {typeof(T).Name} subscribe stream over ({fromExclusive}, {endOffset}] completed with the Created "
            + $"event {(created is null ? "missing" : "present")} and the Archived event "
            + $"{(archived is null ? "missing" : "present")} for {contractIdValue}");
        return default!;
    }

    private static void FailOnStreamFault<T>(ContractStreamEvent<T> streamEvent, string contractIdValue)
        where T : ITemplate, IDamlRecord<T>
    {
        if (streamEvent is ContractStreamEvent<T>.StreamError error)
        {
            Assert.Fail(
                $"the {typeof(T).Name} subscribe stream faulted before the round trip of {contractIdValue} "
                + $"completed: status {error.StatusCode}, category {error.Category?.ToString() ?? "none"} - "
                + error.Message);
        }

        if (streamEvent is ContractStreamEvent<T>.Unclassified unclassified)
        {
            Assert.Fail(
                $"the {typeof(T).Name} subscribe stream refused the event at offset {unclassified.Offset} as "
                + $"{unclassified.Kind} before the round trip of {contractIdValue} completed; a contract-key "
                + "decode failure lands here");
        }
    }

    private static async Task<string?> WireKeyHashAsync(
        RestConformanceLane lane, Party reader, string contractIdValue)
    {
        var request = new GetEventsByContractIdRequest
        {
            ContractId = contractIdValue,
            EventFormat = new EventFormat
            {
                Verbose = true,
                FiltersByParty = new Dictionary<string, Filters> { [reader.Id] = new Filters() },
            },
        };

        var response = await lane.Api<IEventQueryServiceApi>().GetEventsByContractId(
            request, TestContext.Current.CancellationToken);

        var createdEvent = response.Created?.CreatedEvent;
        if (createdEvent is null)
        {
            Assert.Fail(
                $"the events-by-contract-id endpoint returned no created event for {contractIdValue} as read by "
                + $"{reader.Id}, so there is no wire contract key hash to compare the projected one against");
            return null;
        }

        return string.IsNullOrEmpty(createdEvent.ContractKeyHash) ? null : createdEvent.ContractKeyHash;
    }

    private static async Task<Party> NewSignatoryAsync(RestConformanceLane lane, CancellationToken cancellationToken)
    {
        var darPath = await ContractKeysDarPathAsync(cancellationToken);
        try
        {
            var darOutcome = await lane.Fixture.UploadDarAsync(darPath, cancellationToken);
            Assert.True(
                darOutcome is DarUploadOutcome.Uploaded or DarUploadOutcome.AlreadyKnown,
                $"Unexpected DAR upload outcome: {darOutcome}");
        }
        finally
        {
            File.Delete(darPath);
        }

        var party = await lane.Fixture.AllocatePartyAsync("rest-contract-key", cancellationToken: cancellationToken);
        await lane.GrantActAsAsync(party.PartyId, cancellationToken);
        return new Party(party.PartyId);
    }

    private static async Task<string> ContractKeysDarPathAsync(CancellationToken cancellationToken)
    {
        var path = Path.Combine(Path.GetTempPath(), $"contractkeys-{Guid.NewGuid():N}.dar");
        await using var dar = ConformanceCorpus.OpenDar(ConformancePackage.ContractKeys);
        await using var file = File.Create(path);
        try
        {
            await dar.CopyToAsync(file, cancellationToken);
        }
        catch
        {
            await file.DisposeAsync();
            File.Delete(path);
            throw;
        }

        return path;
    }
}
