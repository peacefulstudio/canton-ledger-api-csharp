// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using AwesomeAssertions;
using Canton.Ledger.Rest.Client.Raw;
using Xunit;

namespace Canton.Ledger.Rest.Client.Tests;

public sealed class ExecuteSubmissionSerializationTests
{
    private const string OffsetPeriod = """{"DeduplicationOffset":{"value":9728}}""";
    private const string DurationPeriod = """{"DeduplicationDuration":{"value":{"seconds":30,"nanos":0}}}""";

    private static DeduplicationPeriod ByOffset() => new() { DeduplicationOffset = "9728" };

    private static DeduplicationPeriod ByDuration() => new() { DeduplicationDuration = "30s" };

    [Fact]
    public void ExecuteSubmissionRequest_nests_an_offset_period_under_deduplicationPeriod()
    {
        var request = new ExecuteSubmissionRequest { DeduplicationPeriod = ByOffset() };

        var json = JsonSerializer.Serialize(request, RestRefitSettings.SerializerOptions);

        using var document = JsonDocument.Parse(json);
        document.RootElement.GetProperty("deduplicationPeriod").GetRawText().Should().Be(OffsetPeriod);
        document.RootElement.TryGetProperty("deduplicationOffset", out _).Should()
            .BeFalse("the offset arm no longer sits as a sibling of the wrapper");
    }

    [Fact]
    public void ExecuteSubmissionRequest_nests_a_duration_period_under_deduplicationPeriod()
    {
        var request = new ExecuteSubmissionRequest { DeduplicationPeriod = ByDuration() };

        var json = JsonSerializer.Serialize(request, RestRefitSettings.SerializerOptions);

        using var document = JsonDocument.Parse(json);
        document.RootElement.GetProperty("deduplicationPeriod").GetRawText().Should().Be(DurationPeriod);
        document.RootElement.TryGetProperty("deduplicationDuration", out _).Should()
            .BeFalse("the duration arm no longer sits as a sibling of the wrapper");
    }

    [Fact]
    public void ExecuteSubmissionAndWaitRequest_nests_an_offset_period_under_deduplicationPeriod()
    {
        var request = new ExecuteSubmissionAndWaitRequest { DeduplicationPeriod = ByOffset() };

        var json = JsonSerializer.Serialize(request, RestRefitSettings.SerializerOptions);

        using var document = JsonDocument.Parse(json);
        document.RootElement.GetProperty("deduplicationPeriod").GetRawText().Should().Be(OffsetPeriod);
        document.RootElement.TryGetProperty("deduplicationOffset", out _).Should().BeFalse();
    }

    [Fact]
    public void ExecuteSubmissionAndWaitForTransactionRequest_nests_an_offset_period_under_deduplicationPeriod()
    {
        var request = new ExecuteSubmissionAndWaitForTransactionRequest { DeduplicationPeriod = ByOffset() };

        var json = JsonSerializer.Serialize(request, RestRefitSettings.SerializerOptions);

        using var document = JsonDocument.Parse(json);
        document.RootElement.GetProperty("deduplicationPeriod").GetRawText().Should().Be(OffsetPeriod);
        document.RootElement.TryGetProperty("deduplicationOffset", out _).Should().BeFalse();
    }

    [Fact]
    public void ExecuteSubmissionRequest_reads_a_served_offset_period_through_the_wrapper()
    {
        var request = JsonSerializer.Deserialize<ExecuteSubmissionRequest>(
            $$"""{"submissionId":"s-1","deduplicationPeriod":{{OffsetPeriod}}}""",
            RestRefitSettings.SerializerOptions);

        request.Should().NotBeNull();
        request.DeduplicationPeriod.Should().NotBeNull();
        request.DeduplicationPeriod.DeduplicationOffset.Should().Be("9728");
        request.DeduplicationPeriod.DeduplicationDuration.Should().BeNull();
    }

    [Fact]
    public void ExecuteSubmissionRequest_reads_a_served_duration_period_through_the_wrapper()
    {
        var request = JsonSerializer.Deserialize<ExecuteSubmissionRequest>(
            $$"""{"submissionId":"s-1","deduplicationPeriod":{{DurationPeriod}}}""",
            RestRefitSettings.SerializerOptions);

        request.Should().NotBeNull();
        request.DeduplicationPeriod.Should().NotBeNull();
        request.DeduplicationPeriod.DeduplicationDuration.Should().Be("30s");
        request.DeduplicationPeriod.DeduplicationOffset.Should().BeNull();
    }

    [Fact]
    public void ExecuteSubmissionAndWaitRequest_reads_a_served_offset_period_through_the_wrapper()
    {
        var request = JsonSerializer.Deserialize<ExecuteSubmissionAndWaitRequest>(
            $$"""{"submissionId":"s-1","deduplicationPeriod":{{OffsetPeriod}}}""",
            RestRefitSettings.SerializerOptions);

        request.Should().NotBeNull();
        request.DeduplicationPeriod.Should().NotBeNull();
        request.DeduplicationPeriod.DeduplicationOffset.Should().Be("9728");
    }

    [Fact]
    public void ExecuteSubmissionAndWaitForTransactionRequest_reads_a_served_offset_period_through_the_wrapper()
    {
        var request = JsonSerializer.Deserialize<ExecuteSubmissionAndWaitForTransactionRequest>(
            $$"""{"submissionId":"s-1","deduplicationPeriod":{{OffsetPeriod}}}""",
            RestRefitSettings.SerializerOptions);

        request.Should().NotBeNull();
        request.DeduplicationPeriod.Should().NotBeNull();
        request.DeduplicationPeriod.DeduplicationOffset.Should().Be("9728");
    }

    [Fact]
    public void ExecuteSubmissionRequest_omits_the_wrapper_when_no_period_was_asked_for()
    {
        var request = new ExecuteSubmissionRequest { SubmissionId = "s-1" };

        var json = JsonSerializer.Serialize(request, RestRefitSettings.SerializerOptions);

        using var document = JsonDocument.Parse(json);
        document.RootElement.TryGetProperty("deduplicationPeriod", out _).Should().BeFalse();
    }
}
