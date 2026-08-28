// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Canton.Ledger.Abstractions;
using Daml.Ledger.Abstractions;
using Daml.Runtime;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Streams;
using Xunit;

namespace Canton.Ledger.Testing.Tests;

public class FakeLedgerClientInterfaceViewTests
{
    private static readonly Party Owner = new("bob");

    [Fact]
    public async Task QueryActiveAsync_decodes_each_staged_interface_view_into_the_view_record()
    {
        ICantonLedgerClient client = FakeLedgerClient.Create()
            .WithActiveContracts(
                LedgerEvents.Created(
                    new ContractId<IDemoHoldingView>("cid1"),
                    new DemoHoldingView(42.5m).ToRecord(),
                    LedgerOffset.At(1),
                    (SynchronizerId)"sync1",
                    [Owner]),
                LedgerEvents.Created(
                    new ContractId<IDemoHoldingView>("cid2"),
                    new DemoHoldingView(7m).ToRecord(),
                    LedgerOffset.At(2),
                    (SynchronizerId)"sync1",
                    [Owner]),
                LedgerEvents.Checkpoint<IDemoHoldingView>(LedgerOffset.At(2)))
            .Build();

        var holdings = await client.QueryActiveAsync<IDemoHoldingView, DemoHoldingView>(
            Owner, cancellationToken: TestContext.Current.CancellationToken);

        holdings.Should().HaveCount(2);
        holdings[0].Id.Value.Should().Be("cid1");
        holdings[0].View.Amount.Should().Be(42.5m);
        holdings[1].Id.Value.Should().Be("cid2");
        holdings[1].View.Amount.Should().Be(7m);
    }

    [Fact]
    public async Task QueryActiveAsync_returns_an_empty_list_for_a_checkpoint_only_snapshot()
    {
        ICantonLedgerClient client = FakeLedgerClient.Create()
            .WithActiveContracts(LedgerEvents.Checkpoint<IDemoHoldingView>(LedgerOffset.At(9)))
            .Build();

        var holdings = await client.QueryActiveAsync<IDemoHoldingView, DemoHoldingView>(
            Owner, cancellationToken: TestContext.Current.CancellationToken);

        holdings.Should().BeEmpty();
    }

    [Fact]
    public async Task QueryActiveAsync_throws_LedgerOperationException_when_the_snapshot_has_no_terminal_checkpoint()
    {
        ICantonLedgerClient client = FakeLedgerClient.Create()
            .WithActiveContracts(
                LedgerEvents.Created(
                    new ContractId<IDemoHoldingView>("cid1"),
                    new DemoHoldingView(1m).ToRecord(),
                    LedgerOffset.At(1),
                    (SynchronizerId)"sync1",
                    [Owner]))
            .Build();

        var querying = async () => await client.QueryActiveAsync<IDemoHoldingView, DemoHoldingView>(
            Owner, cancellationToken: TestContext.Current.CancellationToken);

        await querying.Should().ThrowAsync<LedgerOperationException>()
            .WithMessage("*without its terminal checkpoint*");
    }

    [Fact]
    public async Task QueryActiveAsync_throws_LedgerOperationException_carrying_the_status_code_when_the_snapshot_faults()
    {
        ICantonLedgerClient client = FakeLedgerClient.Create()
            .WithActiveContracts(LedgerEvents.StreamError<IDemoHoldingView>(14, "snapshot aborted mid-stream"))
            .Build();

        var querying = async () => await client.QueryActiveAsync<IDemoHoldingView, DemoHoldingView>(
            Owner, cancellationToken: TestContext.Current.CancellationToken);

        (await querying.Should().ThrowAsync<LedgerOperationException>())
            .Which.StatusCode.Should().Be(14);
    }

    [Fact]
    public async Task QueryActiveAsync_throws_LedgerOperationException_when_a_staged_view_does_not_decode()
    {
        ICantonLedgerClient client = FakeLedgerClient.Create()
            .WithActiveContracts(
                LedgerEvents.Created(
                    new ContractId<IDemoHoldingView>("cid1"),
                    DamlRecord.Create(DamlField.Create("quantity", new DamlNumeric(1m))),
                    LedgerOffset.At(1),
                    (SynchronizerId)"sync1",
                    [Owner]),
                LedgerEvents.Checkpoint<IDemoHoldingView>(LedgerOffset.At(1)))
            .Build();

        var querying = async () => await client.QueryActiveAsync<IDemoHoldingView, DemoHoldingView>(
            Owner, cancellationToken: TestContext.Current.CancellationToken);

        await querying.Should().ThrowAsync<LedgerOperationException>()
            .WithMessage($"*did not decode into {nameof(DemoHoldingView)}*");
    }

    [Fact]
    public async Task QueryActiveAsync_wraps_any_decode_failure_a_view_factory_raises_including_KeyNotFoundException()
    {
        ICantonLedgerClient client = FakeLedgerClient.Create()
            .WithActiveContracts(
                LedgerEvents.Created(
                    new ContractId<IKeyedHoldingView>("cid1"),
                    DamlRecord.Create(DamlField.Create("quantity", new DamlNumeric(1m))),
                    LedgerOffset.At(1),
                    (SynchronizerId)"sync1",
                    [Owner]),
                LedgerEvents.Checkpoint<IKeyedHoldingView>(LedgerOffset.At(1)))
            .Build();

        var querying = async () => await client.QueryActiveAsync<IKeyedHoldingView, KeyedHoldingView>(
            Owner, cancellationToken: TestContext.Current.CancellationToken);

        await querying.Should().ThrowAsync<LedgerOperationException>()
            .WithMessage($"*did not decode into {nameof(KeyedHoldingView)}*")
            .WithInnerException<LedgerOperationException, KeyNotFoundException>();
    }
}
