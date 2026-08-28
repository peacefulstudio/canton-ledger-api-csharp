// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Canton.Ledger.Abstractions;
using Daml.Runtime.Commands;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Outcomes;
using Richtypes;
using Xunit;

namespace Canton.Ledger.Client.Parity.Tests;

/// <summary>
/// Behavioral parity suite over <see cref="ICantonLedgerClient.TrySubmitAndWaitForTransactionTreeAsync"/>,
/// run against every provider that implements it (the in-memory Fake, REST, and gRPC) through one
/// shared set of test bodies: create a Marker, and confirm the committed transaction comes back with
/// its hierarchy intact and projects to the same flattened shape regardless of transport. The bodies
/// create rather than exercise, so what they compare is the tree structure itself rather than the
/// per-transport decoding of a choice result.
/// </summary>
public abstract class LedgerTransactionTreeParityTests
{
    /// <summary>
    /// Opens a lane over this provider's tree-shaped submit path, together with the
    /// <see cref="Party"/> that owns the Marker the shared bodies create.
    /// </summary>
    protected abstract Task<CapabilityLane<(ICantonLedgerClient Client, Party Owner)>> OpenTransactionTreeAsync(
        CancellationToken cancellationToken);

    [Fact]
    public async Task TrySubmitAndWaitForTransactionTreeAsync_returns_the_created_Marker_as_a_root_event()
    {
        await using var lane = await OpenTransactionTreeAsync(TestContext.Current.CancellationToken);
        var (client, owner) = lane.Capability;

        var outcome = await client.TrySubmitAndWaitForTransactionTreeAsync(
            CommandsSubmission.Single(CreateCommand.For(new Marker(owner))),
            owner,
            cancellationToken: TestContext.Current.CancellationToken);

        var tree = outcome.Should().BeOfType<ExerciseOutcome<TransactionTree>.One>().Subject.Result;
        tree.UpdateId.Should().NotBeNullOrWhiteSpace();
        var root = tree.RootEvents.Should().ContainSingle().Subject
            .Should().BeOfType<TreeEvent.Created>().Subject;
        root.ContractId.Should().NotBeNullOrWhiteSpace();
        root.TemplateId.EntityName.Should().Be(Marker.TemplateId.EntityName);
        root.DescendantEvents().Should().BeEmpty();
    }

    [Fact]
    public async Task TrySubmitAndWaitForTransactionTreeAsync_projects_to_the_flattened_TransactionResult()
    {
        await using var lane = await OpenTransactionTreeAsync(TestContext.Current.CancellationToken);
        var (client, owner) = lane.Capability;
        var outcome = await client.TrySubmitAndWaitForTransactionTreeAsync(
            CommandsSubmission.Single(CreateCommand.For(new Marker(owner))),
            owner,
            cancellationToken: TestContext.Current.CancellationToken);
        var tree = outcome.Should().BeOfType<ExerciseOutcome<TransactionTree>.One>().Subject.Result;

        var flattened = tree.ToTransactionResult();

        flattened.UpdateId.Should().Be(tree.UpdateId);
        flattened.ArchivedContractIds.Should().BeEmpty();
        flattened.CreatedContracts.Should().ContainSingle().Which.ContractId
            .Should().Be(tree.RootEvents.OfType<TreeEvent.Created>().Single().ContractId);
    }
}
