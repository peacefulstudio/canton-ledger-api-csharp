// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Daml.Runtime.Contracts;
using Daml.Runtime.Outcomes;
using Xunit;

namespace Canton.Ledger.Testing.Helpers;

/// <summary>
/// Behavioural parity suite over a committed transaction whose payload cannot be decoded, run
/// against every transport's real client through one shared set of test bodies. It pins the
/// decision both transports must agree on: a participant that acknowledged the command but sent a
/// transaction no <see cref="TransactionResult"/> can carry is reported as
/// <see cref="ExerciseOutcome{T}.CommittedUndecodable"/> carrying the update id the response did
/// declare, so a caller reads the transaction back rather than resubmitting; and a response that
/// never got as far as declaring one reports a <c>null</c> update id. The message wording and the
/// exception type each transport's own decoder raises stay per-transport.
/// </summary>
public abstract class CommittedUndecodableParityTests
{
    /// <summary>The update id every transport's stubbed response declares.</summary>
    protected const string DeclaredUpdateId = "update-1";

    /// <summary>
    /// Submits through this transport's real client against a stubbed participant answering with a
    /// transaction declaring <see cref="DeclaredUpdateId"/> and carrying <paramref name="shape"/>.
    /// </summary>
    protected abstract Task<ExerciseOutcome<TransactionResult>> SubmitTransactionCarryingAsync(
        UndecodableWireShape shape);

    /// <summary>
    /// Submits through this transport's real client against a stubbed participant that acknowledged
    /// the command but answered without a transaction.
    /// </summary>
    protected abstract Task<ExerciseOutcome<TransactionResult>> SubmitWithoutTransactionAsync();

    [Theory]
    [InlineData(UndecodableWireShape.NegativeOffset)]
    [InlineData(UndecodableWireShape.EmptyActingParty)]
    [InlineData(UndecodableWireShape.WhitespaceCommandId)]
    public async Task A_wire_value_the_Ledger_API_could_not_have_meant_is_reported_as_CommittedUndecodable(
        UndecodableWireShape shape)
    {
        var outcome = await SubmitTransactionCarryingAsync(shape);

        var undecodable = outcome.Should().BeOfType<ExerciseOutcome<TransactionResult>.CommittedUndecodable>().Subject;
        undecodable.UpdateId.Should().Be(DeclaredUpdateId);
        undecodable.Message.Should().NotBeNullOrWhiteSpace();
        undecodable.SourceException.Should().NotBeNull();
    }

    [Fact]
    public async Task A_response_without_a_transaction_is_reported_as_CommittedUndecodable_without_an_update_id()
    {
        var outcome = await SubmitWithoutTransactionAsync();

        var undecodable = outcome.Should().BeOfType<ExerciseOutcome<TransactionResult>.CommittedUndecodable>().Subject;
        undecodable.UpdateId.Should().BeNull();
        undecodable.Message.Should().NotBeNullOrWhiteSpace();
        undecodable.SourceException.Should().NotBeNull();
    }
}
