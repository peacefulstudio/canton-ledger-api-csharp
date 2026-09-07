// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using System.Text.Json;
using AwesomeAssertions;
using Canton.Ledger.Abstractions;
using Canton.Ledger.Kernel.Wire;
using Xunit;

namespace Canton.Ledger.Kernel.Tests.Wire;

public class MalformedResponseTests
{
    [Fact]
    public void MissingRequiredField_names_the_field_and_the_Ledger_API_requirement()
    {
        MalformedResponse.MissingRequiredField("CreatedEvent for contract '00abc' has no templateId")
            .Message.Should().Be(
                "Malformed response from ledger: CreatedEvent for contract '00abc' has no templateId, "
                + "though the Ledger API marks the field as required.");
    }

    [Fact]
    public void WithDetail_carries_the_detail_behind_the_shared_prefix()
    {
        MalformedResponse.WithDetail("List contains a null element.")
            .Message.Should().Be("Malformed response from ledger: List contains a null element.");
    }

    [Fact]
    public void WithDetail_keeps_the_originating_failure_as_the_inner_exception()
    {
        var cause = new InvalidOperationException("Record field 'amount' has no Value set.");

        MalformedResponse.WithDetail(cause.Message, cause)
            .InnerException.Should().BeSameAs(cause);
    }

    [Fact]
    public void WithDetail_carries_the_detail_apart_from_the_message_so_a_quoting_message_can_reuse_it()
    {
        MalformedResponse.WithDetail("List contains a null element.")
            .Detail.Should().Be("List contains a null element.");
    }

    [Fact]
    public void An_InvalidOperationException_wearing_the_prefix_is_not_a_malformed_response()
    {
        var impostor = new InvalidOperationException("Malformed response from ledger: written by hand.");

        MalformedResponse.IsWireDecodeFailure(impostor).Should().BeFalse();
    }

    [Theory]
    [MemberData(nameof(WireDecodeFailures))]
    public void IsWireDecodeFailure_admits_the_shapes_a_malformed_body_raises_and_nothing_else(
        Exception exception, bool expected)
    {
        MalformedResponse.IsWireDecodeFailure(exception).Should().Be(expected);
    }

    public static TheoryData<Exception, bool> WireDecodeFailures() => new()
    {
        { new FormatException("Cannot parse wire Int64 value 'x' as a 64-bit integer."), true },
        { new MalformedTransactionTreeException("Cannot reconstruct the transaction tree: ."), true },
        { MalformedResponse.MissingRequiredField("a field is absent"), true },
        { MalformedResponse.WithDetail("a value is undecodable"), true },
        { new ArgumentException("a caller passed something wrong"), false },
        { new NotSupportedException("an unsupported wire shape"), false },
        { new InvalidOperationException("a downstream bug of ours"), false },
        { new OperationCanceledException(), false },
        { new JsonException("the body is not JSON"), false },
    };

    [Fact]
    public void CouldNotDecodeTransaction_splices_a_prefixed_detail_in_without_repeating_the_prefix()
    {
        var cause = MalformedResponse.MissingRequiredField("CreatedEvent for contract '00abc' has no templateId");

        var thrown = MalformedResponse.CouldNotDecodeTransaction("offset 42", cause);

        thrown.Message.Should().Be(
            "Malformed response from ledger: the transaction at offset 42 could not be decoded: "
            + "CreatedEvent for contract '00abc' has no templateId, though the Ledger API marks the field as required.");
        thrown.InnerException.Should().BeSameAs(cause);
    }

    [Fact]
    public void CouldNotDecodeTransaction_carries_an_unprefixed_detail_through_verbatim()
    {
        var cause = new MalformedTransactionTreeException(
            "Cannot reconstruct the transaction tree: node id 1 follows node id 3.");

        MalformedResponse.CouldNotDecodeTransaction("id update-7", cause).Message.Should().Be(
            "Malformed response from ledger: the transaction at id update-7 could not be decoded: "
            + "Cannot reconstruct the transaction tree: node id 1 follows node id 3.");
    }

    [Fact]
    public void Decoding_returns_what_the_decode_produced_when_the_wire_value_is_readable()
    {
        MalformedResponse.Decoding("7", wire => int.Parse(wire, CultureInfo.InvariantCulture)).Should().Be(7);
    }

    [Theory]
    [MemberData(nameof(UndecodableWireValues))]
    public void Decoding_relabels_a_failure_raised_while_reading_the_wire_value(Exception undecodable)
    {
        var thrown = Record.Exception(() => MalformedResponse.Decoding<int, int>(0, _ => throw undecodable));

        thrown.Should().BeOfType<MalformedResponseException>();
        thrown!.Message.Should().Be($"Malformed response from ledger: {undecodable.Message}");
        thrown.InnerException.Should().BeSameAs(undecodable);
    }

    /// <summary>
    /// The shapes the pinned runtime raises when a legal wire value carries an illegal payload — a
    /// negative offset, a party that is empty or all whitespace, a date or timestamp outside the
    /// Daml-LF range, a proto sum case the converter does not recognise.
    /// </summary>
    public static TheoryData<Exception> UndecodableWireValues() =>
    [
        new ArgumentOutOfRangeException("value", -1L, "value ('-1') must be a non-negative value."),
        new ArgumentException("The value cannot be an empty string or composed entirely of whitespace.", "id"),
        new NotSupportedException("Proto Value case None is not supported"),
        new InvalidOperationException("Record field 'amount' has no Value set."),
    ];

    [Theory]
    [MemberData(nameof(FailuresDecodingLeavesAlone))]
    public void Decoding_leaves_a_failure_that_already_names_its_own_class_untouched(Exception failure)
    {
        Record.Exception(() => MalformedResponse.Decoding<int, int>(0, _ => throw failure)).Should().BeSameAs(failure);
    }

    public static TheoryData<Exception> FailuresDecodingLeavesAlone() =>
    [
        MalformedResponse.WithDetail("a value is undecodable"),
        new MalformedTransactionTreeException("Cannot reconstruct the transaction tree: ."),
        new FormatException("Cannot parse wire Int64 value 'x' as a 64-bit integer."),
        new OperationCanceledException(),
        new KeyNotFoundException("a lookup of ours found nothing"),
    ];
}
