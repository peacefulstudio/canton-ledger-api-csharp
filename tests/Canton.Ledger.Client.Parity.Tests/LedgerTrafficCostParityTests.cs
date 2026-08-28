// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Canton.Ledger.Abstractions;
using Daml.Runtime.Commands;
using Daml.Runtime.Data;
using Richtypes;
using Xunit;

namespace Canton.Ledger.Client.Parity.Tests;

/// <summary>
/// Behavioral parity suite over <see cref="ICantonLedgerClient.EstimateTrafficCostAsync"/>, run
/// through one shared set of test bodies. The concrete byte counts are the participant's and differ
/// per deployment, so what the bodies compare is the contract around them: an absent estimation is
/// reported as <see langword="null"/> rather than as a zeroed record, no component is ever negative,
/// and pricing is a read — the same submission can be priced twice and is answered the same way,
/// because nothing was submitted the first time. The per-transport unit suites cover the projection
/// of each field.
/// </summary>
/// <remarks>
/// Only the in-memory Fake lane exists so far. Both live clients implement the member and the
/// participant serves the prepare route it is priced over, so nothing structural keeps REST and gRPC
/// out; what is missing is that no test on either transport has yet called that route against a live
/// participant, so a lane added here would be asserting behaviour nobody has measured. Standing up
/// those two lanes is follow-up work rather than a transport gap.
/// </remarks>
public abstract class LedgerTrafficCostParityTests
{
    /// <summary>
    /// Opens a lane over this provider's traffic-cost estimation, together with the
    /// <see cref="Party"/> the shared bodies price a Marker creation for.
    /// </summary>
    protected abstract Task<CapabilityLane<(ICantonLedgerClient Client, Party Owner)>> OpenTrafficCostAsync(
        CancellationToken cancellationToken);

    [Fact]
    public async Task EstimateTrafficCostAsync_reports_no_negative_cost_component()
    {
        await using var lane = await OpenTrafficCostAsync(TestContext.Current.CancellationToken);
        var (client, owner) = lane.Capability;

        var estimate = await client.EstimateTrafficCostAsync(
            PricedSubmission(owner), cancellationToken: TestContext.Current.CancellationToken);

        if (estimate is null)
        {
            return;
        }

        estimate.ConfirmationRequestCost.Should().BeGreaterThanOrEqualTo(0);
        estimate.ConfirmationResponseCost.Should().BeGreaterThanOrEqualTo(0);
        estimate.TotalCost.Should().BeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public async Task EstimateTrafficCostAsync_answers_the_same_submission_the_same_way_twice()
    {
        await using var lane = await OpenTrafficCostAsync(TestContext.Current.CancellationToken);
        var (client, owner) = lane.Capability;
        var submission = PricedSubmission(owner);

        var first = await client.EstimateTrafficCostAsync(
            submission, cancellationToken: TestContext.Current.CancellationToken);
        var second = await client.EstimateTrafficCostAsync(
            submission, cancellationToken: TestContext.Current.CancellationToken);

        (second is null).Should().Be(
            first is null,
            "pricing a submission commits nothing, so repeating it neither is deduplicated nor changes whether the "
            + "participant serves an estimation");
    }

    private static CommandsSubmission PricedSubmission(Party owner) =>
        CommandsSubmission.Single(CreateCommand.For(new Marker(owner)))
            .WithActAs(owner)
            .WithCommandId(new CommandId(Guid.NewGuid().ToString()));
}
