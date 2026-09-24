// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Canton.Ledger.Abstractions;
using Daml.Runtime.Data;
using Daml.Runtime.Streams;
using Splice.ValidatorLicense;
using Xunit;

namespace Canton.Ledger.Client.Parity.Tests;

/// <summary>
/// Parity suite over the generated Splice bindings (the <c>Splice.Amulet</c> NuGet emitted by
/// daml-codegen-csharp), run against every transport that reaches a live LocalNet participant.
/// Each lane reads the contracts Splice itself created when the validator onboarded — nothing the
/// test uploads or submits — so a green row proves the generated binding decodes real Splice
/// payloads over that transport.
/// </summary>
public abstract class SpliceBindingsParityTests
{
    /// <summary>
    /// Opens a lane over this transport's <see cref="ICantonLedgerClient"/>, paired with the
    /// validator operator party that Splice onboarding issued the <see cref="ValidatorLicense"/> to.
    /// </summary>
    protected abstract Task<CapabilityLane<(ICantonLedgerClient Client, Party Operator)>>
        OpenOperatorLaneAsync(CancellationToken cancellationToken);

    [Fact]
    public async Task SubscribeActiveAsync_decodes_the_operator_ValidatorLicense_through_the_generated_Splice_binding()
    {
        await using var lane = await OpenOperatorLaneAsync(TestContext.Current.CancellationToken);
        var (client, @operator) = lane.Capability;

        var entries = new List<AcsSnapshotEntry<ValidatorLicense>>();
        await foreach (var entry in client.SubscribeActiveAsync<ValidatorLicense>(
            @operator, cancellationToken: TestContext.Current.CancellationToken))
        {
            entries.Add(entry);
        }

        var license = entries.OfType<AcsSnapshotEntry<ValidatorLicense>.Created>().Should().ContainSingle().Subject;
        @operator.Id.Should().StartWith("a-validator-1::");
        license.Payload.Validator.Should().Be(@operator);
        license.Payload.Sponsor.Should().Be(@operator);
        license.Payload.Dso.Id.Should().StartWith("DSO::");
        license.Payload.Metadata.Should().NotBeNull();
        license.Payload.LastActiveAt.Should().NotBeNull();
        entries[^1].Should().BeOfType<AcsSnapshotEntry<ValidatorLicense>.Checkpoint>();
    }
}
