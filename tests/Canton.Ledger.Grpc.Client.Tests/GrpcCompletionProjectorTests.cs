// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Canton.Ledger.Abstractions;
using AwesomeAssertions;
using Xunit;
using Duration = Google.Protobuf.WellKnownTypes.Duration;
using ProtoCompletion = Com.Daml.Ledger.Api.V2.Completion;
using ProtoSynchronizerTime = Com.Daml.Ledger.Api.V2.SynchronizerTime;
using RpcStatus = Google.Rpc.Status;
using Timestamp = Google.Protobuf.WellKnownTypes.Timestamp;

namespace Canton.Ledger.Grpc.Client.Tests;

public class GrpcCompletionProjectorTests
{
    [Fact]
    public void Project_maps_a_zero_status_completion_to_CommandAccepted_carrying_the_update_id_and_neutral_fields()
    {
        var completion = new ProtoCompletion
        {
            CommandId = "cmd-1",
            UpdateId = "update-1",
            Offset = 42L,
            Status = new RpcStatus { Code = 0 },
        };

        var result = GrpcCompletionProjector.Project(completion);

        var accepted = result.Should().BeOfType<CompletionStreamEvent.CommandAccepted>().Subject;
        accepted.UpdateId.Should().Be("update-1");
        accepted.Completion.CommandId.Value.Should().Be("cmd-1");
        accepted.Completion.Offset.Should().Be(42L);
    }

    [Fact]
    public void Project_maps_a_completion_with_no_status_set_to_CommandAccepted()
    {
        var completion = new ProtoCompletion
        {
            CommandId = "cmd-1",
            UpdateId = "update-1",
        };

        var result = GrpcCompletionProjector.Project(completion);

        var accepted = result.Should().BeOfType<CompletionStreamEvent.CommandAccepted>().Subject;
        accepted.UpdateId.Should().Be("update-1");
        accepted.Completion.CommandId.Value.Should().Be("cmd-1");
    }

    [Fact]
    public void Project_maps_a_non_zero_status_completion_to_CommandRejected_carrying_the_completion_status()
    {
        var completion = new ProtoCompletion
        {
            CommandId = "cmd-2",
            Offset = 7L,
            Status = new RpcStatus { Code = 3, Message = "INVALID_ARGUMENT" },
        };

        var result = GrpcCompletionProjector.Project(completion);

        var rejected = result.Should().BeOfType<CompletionStreamEvent.CommandRejected>().Subject;
        rejected.Status.Code.Should().Be(3);
        rejected.Status.Message.Should().Be("INVALID_ARGUMENT");
        rejected.Completion.CommandId.Value.Should().Be("cmd-2");
    }

    public static TheoryData<ProtoCompletion, long?, TimeSpan?> DeduplicationPeriods => new()
    {
        { new ProtoCompletion { CommandId = "d-offset", DeduplicationOffset = 100L }, 100L, null },
        { new ProtoCompletion { CommandId = "d-duration", DeduplicationDuration = Duration.FromTimeSpan(TimeSpan.FromSeconds(30)) }, null, TimeSpan.FromSeconds(30) },
        { new ProtoCompletion { CommandId = "d-none" }, null, null },
    };

    [Theory]
    [MemberData(nameof(DeduplicationPeriods))]
    public void Project_maps_the_deduplication_period_oneof_to_the_matching_nullable(
        ProtoCompletion completion, long? expectedOffset, TimeSpan? expectedDuration)
    {
        var result = GrpcCompletionProjector.Project(completion);

        var payload = result.Should().BeOfType<CompletionStreamEvent.CommandAccepted>().Subject.Completion;
        payload.DeduplicationOffset.Should().Be(expectedOffset);
        payload.DeduplicationDuration.Should().Be(expectedDuration);
    }

    [Fact]
    public void Project_maps_act_as_parties_synchronizer_time_and_the_optional_ids()
    {
        var recordTime = new DateTimeOffset(2026, 7, 20, 10, 30, 0, TimeSpan.Zero);
        var completion = new ProtoCompletion
        {
            CommandId = "cmd-4",
            SubmissionId = "sub-4",
            UserId = "user-4",
            SynchronizerTime = new ProtoSynchronizerTime
            {
                SynchronizerId = "sync-1",
                RecordTime = Timestamp.FromDateTimeOffset(recordTime),
            },
        };
        completion.ActAs.Add("alice");
        completion.ActAs.Add("bob");

        var payload = GrpcCompletionProjector.Project(completion)
            .Should().BeOfType<CompletionStreamEvent.CommandAccepted>().Subject.Completion;

        payload.CommandId.Value.Should().Be("cmd-4");
        payload.ActAs.Select(p => p.Id).Should().Equal("alice", "bob");
        payload.SynchronizerTime.SynchronizerId.Should().Be("sync-1");
        payload.SynchronizerTime.RecordTime.Should().Be(recordTime);
        payload.SubmissionId.Should().Be("sub-4");
        payload.UserId.Should().Be("user-4");
    }

    [Fact]
    public void Project_throws_ArgumentNullException_when_completion_is_null()
    {
        var act = () => GrpcCompletionProjector.Project(null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("completion");
    }

    [Fact]
    public void Project_throws_a_malformed_response_when_an_accepted_completion_carries_no_command_id()
    {
        var completion = new ProtoCompletion { UpdateId = "update-1", Offset = 42L, Status = new RpcStatus { Code = 0 } };

        var act = () => GrpcCompletionProjector.Project(completion);

        act.Should().Throw<InvalidOperationException>(
                "the Ledger API marks Completion.command_id required, so an empty one is a malformed response "
                + "rather than a completion carrying a command id that cannot be read")
            .WithMessage("Malformed response from ledger*command_id*")
            .WithMessage("*offset 42*");
    }

    [Fact]
    public void Project_throws_a_malformed_response_when_a_rejected_completion_carries_no_command_id()
    {
        var completion = new ProtoCompletion { Offset = 7L, Status = new RpcStatus { Code = 3, Message = "INVALID_ARGUMENT" } };

        var act = () => GrpcCompletionProjector.Project(completion);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("Malformed response from ledger*command_id*");
    }

    [Fact]
    public void Project_leaves_a_present_command_id_readable_on_the_projected_completion()
    {
        var completion = new ProtoCompletion { CommandId = "cmd-present", UpdateId = "update-1", Offset = 42L };

        var payload = GrpcCompletionProjector.Project(completion)
            .Should().BeOfType<CompletionStreamEvent.CommandAccepted>().Subject.Completion;

        var readCommandId = () => payload.CommandId.Value;
        readCommandId.Should().NotThrow().Which.Should().Be("cmd-present");
    }
}
