// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Canton.Ledger.Abstractions;
using Canton.Ledger.Kernel.Commands;
using Daml.Runtime.Commands;
using Xunit;

namespace Canton.Ledger.Kernel.Tests.Commands;

public class CompletionVerdictTests
{
    private static readonly Completion Payload = new(
        (CommandId)"cmd-1",
        Offset: 17L,
        ActAs: [],
        new SynchronizerTime("sync::fingerprint::3", default),
        SubmissionId: null,
        UserId: null,
        DeduplicationOffset: null,
        DeduplicationDuration: null);

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    public void Classify_accepts_an_absent_or_zero_status_code(int? statusCode)
    {
        var verdict = CompletionVerdict.Classify(Payload, statusCode, statusMessage: null, "update-1");

        verdict.Should().BeOfType<CompletionStreamEvent.CommandAccepted>()
            .Which.UpdateId.Should().Be("update-1");
    }

    [Theory]
    [InlineData(3)]
    [InlineData(14)]
    public void Classify_rejects_a_non_zero_status_code_carrying_its_detail(int statusCode)
    {
        var verdict = CompletionVerdict.Classify(Payload, statusCode, "the participant said no", "update-1");

        verdict.Should().BeOfType<CompletionStreamEvent.CommandRejected>()
            .Which.Status.Should().Be(new CompletionStatus(statusCode, "the participant said no"));
    }

    [Fact]
    public void Classify_carries_the_decoded_completion_onto_both_verdicts()
    {
        CompletionVerdict.Classify(Payload, statusCode: 0, statusMessage: null, "update-1")
            .Should().BeOfType<CompletionStreamEvent.CommandAccepted>()
            .Which.Completion.Should().BeSameAs(Payload);

        CompletionVerdict.Classify(Payload, statusCode: 3, statusMessage: null, "update-1")
            .Should().BeOfType<CompletionStreamEvent.CommandRejected>()
            .Which.Completion.Should().BeSameAs(Payload);
    }

    [Fact]
    public void Classify_normalises_an_absent_update_id_to_the_empty_string()
    {
        var verdict = CompletionVerdict.Classify(Payload, statusCode: null, statusMessage: null, updateId: null);

        verdict.Should().BeOfType<CompletionStreamEvent.CommandAccepted>()
            .Which.UpdateId.Should().BeEmpty();
    }

    [Fact]
    public void Classify_normalises_an_absent_status_message_to_the_empty_string()
    {
        var verdict = CompletionVerdict.Classify(Payload, statusCode: 3, statusMessage: null, "update-1");

        verdict.Should().BeOfType<CompletionStreamEvent.CommandRejected>()
            .Which.Status.Message.Should().BeEmpty();
    }
}
