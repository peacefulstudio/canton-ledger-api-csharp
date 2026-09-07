// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Canton.Ledger.Abstractions;
using Xunit;

namespace Canton.Ledger.Testing.Helpers;

/// <summary>
/// Behavioural parity suite over the width of each transport's point-read decode filter, run
/// against every transport's point-read wrap through one shared set of test bodies. It pins the
/// decision both transports must agree on: a failure that says the participant's body was
/// unreadable is relabelled as a malformed response naming the lookup, and everything else — a
/// bug of ours raised downstream of the wire decode, or a cancellation — propagates untouched, so
/// the wrap never blames the participant for a fault it did not cause. The exception shapes a
/// transport's own decoder can raise beyond this shared set stay per-transport.
/// </summary>
public abstract class PointReadDecodeParityTests
{
    /// <summary>The lookup description each transport's wrap is asked to name in its message.</summary>
    protected const string LookupDescription = "offset 42";

    /// <summary>The prefix both transports mark a malformed participant response with.</summary>
    protected const string MalformedResponsePrefix = "Malformed response from ledger: ";

    /// <summary>
    /// Runs this transport's point-read wrap over a transaction-shaped response with a projection
    /// that throws <paramref name="decodeFailure"/>, and returns whatever escaped the wrap.
    /// </summary>
    protected abstract Exception EscapingPointRead(Exception decodeFailure);

    /// <summary>
    /// Runs this transport's point-read wrap over a real body carrying <paramref name="shape"/>,
    /// decoded by this transport's own projector, and returns whatever escaped the wrap.
    /// </summary>
    protected abstract Exception EscapingPointReadOf(UndecodableWireShape shape);

    [Theory]
    [InlineData(UndecodableWireShape.NegativeOffset)]
    [InlineData(UndecodableWireShape.EmptyActingParty)]
    [InlineData(UndecodableWireShape.WhitespaceCommandId)]
    public void The_point_read_wrap_relabels_a_wire_value_the_Ledger_API_could_not_have_meant(
        UndecodableWireShape shape)
    {
        var escaped = EscapingPointReadOf(shape);

        escaped.Should().BeOfType<MalformedResponseException>();
        escaped.Message.Should().StartWith(
            $"{MalformedResponsePrefix}the transaction at {LookupDescription} could not be decoded: ");
    }

    [Theory]
    [MemberData(nameof(RelabelledDecodeFailures))]
    public void The_point_read_wrap_relabels_a_failure_that_says_the_body_was_unreadable(Exception decodeFailure)
    {
        var escaped = EscapingPointRead(decodeFailure);

        escaped.Should().BeOfType<MalformedResponseException>();
        escaped.Message.Should().StartWith(
            $"{MalformedResponsePrefix}the transaction at {LookupDescription} could not be decoded: ");
        escaped.InnerException.Should().BeSameAs(decodeFailure);
    }

    /// <summary>The failures every transport must relabel, whatever else its own decoder can raise.</summary>
    public static TheoryData<Exception> RelabelledDecodeFailures() =>
    [
        new MalformedResponseException(
            "CreatedEvent for contract '00aa' has no templateId, "
            + "though the Ledger API marks the field as required."),
        new MalformedTransactionTreeException("Cannot reconstruct the transaction tree: node id 1 follows node id 3."),
        new FormatException("Cannot parse wire Int64 value 'x' as a 64-bit integer."),
    ];

    [Theory]
    [MemberData(nameof(UnrelatedFailures))]
    public void The_point_read_wrap_leaves_a_failure_it_did_not_cause_untouched(Exception unrelated)
    {
        EscapingPointRead(unrelated).Should().BeSameAs(unrelated);
    }

    /// <summary>The failures no transport may blame the participant for.</summary>
    public static TheoryData<Exception> UnrelatedFailures() =>
    [
        new InvalidOperationException("Transaction contains no exercised event for choice 'Foo'."),
        new InvalidOperationException(
            $"{MalformedResponsePrefix}a message wearing the marker without being the type"),
        new ArgumentOutOfRangeException("nodeId"),
        new NotSupportedException("Unknown right kind: KindOneofCase.None"),
        new OperationCanceledException(),
    ];

    [Fact]
    public void The_point_read_wrap_leaves_a_null_dereference_in_the_projection_untouched()
    {
        var escaped = EscapingPointRead(NullDereference());

        escaped.Should().BeOfType<NullReferenceException>();
        escaped.Message.Should().NotContain(MalformedResponsePrefix);
    }

    private static NullReferenceException NullDereference()
    {
        try
        {
            _ = ((object)null!).ToString();
        }
        catch (NullReferenceException nullDereference)
        {
            return nullDereference;
        }
        throw new InvalidOperationException("Dereferencing null did not raise a NullReferenceException.");
    }
}
