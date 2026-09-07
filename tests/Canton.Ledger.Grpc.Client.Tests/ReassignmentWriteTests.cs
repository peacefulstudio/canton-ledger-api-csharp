// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics;
using Canton.Ledger.Abstractions;
using Canton.Ledger.Kernel.Authentication;
using Canton.Ledger.Kernel.Telemetry;
using Daml.Runtime;
using Daml.Runtime.Data;
using Daml.Runtime.Outcomes;
using Daml.Runtime.Streams;
using AwesomeAssertions;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;
using ProtoV2 = Com.Daml.Ledger.Api.V2;
using RuntimeCommands = Daml.Runtime.Commands;
using Status = Grpc.Core.Status;
using TemplateMarker = Canton.Ledger.Testing.Helpers.TemplateMarker;

namespace Canton.Ledger.Grpc.Client.Tests;

public sealed class LedgerClientReassignmentWriteTests : IDisposable
{
    private static readonly Party Submitter = new("party::alice");
    private static readonly SynchronizerId Source = new("sync::source");
    private static readonly SynchronizerId Target = new("sync::target");

    private readonly LedgerClientOptions _options;
    private readonly GrpcChannel _channel;
    private readonly ProtoV2.CommandService.CommandServiceClient _commandService;
    private readonly ProtoV2.UpdateService.UpdateServiceClient _updateService;
    private readonly ProtoV2.StateService.StateServiceClient _stateService;
    private readonly ProtoV2.CommandSubmissionService.CommandSubmissionServiceClient _submissionService;
    private readonly ProtoV2.CommandCompletionService.CommandCompletionServiceClient _completionService;
    private readonly ITokenProvider _tokenProvider = new StaticTokenProvider("test-token");

    public LedgerClientReassignmentWriteTests()
    {
        _options = new LedgerClientOptions { GrpcAddress = "https://localhost:5001", UserId = "test-user" };
        _channel = GrpcChannel.ForAddress(_options.GrpcAddress);

        var callInvoker = Substitute.For<CallInvoker>();
        _commandService = Substitute.ForPartsOf<ProtoV2.CommandService.CommandServiceClient>(callInvoker);
        _updateService = Substitute.ForPartsOf<ProtoV2.UpdateService.UpdateServiceClient>(callInvoker);
        _stateService = Substitute.ForPartsOf<ProtoV2.StateService.StateServiceClient>(callInvoker);
        _submissionService = Substitute.ForPartsOf<ProtoV2.CommandSubmissionService.CommandSubmissionServiceClient>(callInvoker);
        _completionService = Substitute.ForPartsOf<ProtoV2.CommandCompletionService.CommandCompletionServiceClient>(callInvoker);
    }

    public void Dispose() => _channel.Dispose();

    private LedgerClient CreateClient() => new(
        _options, _channel, _commandService, _updateService, _stateService,
        _submissionService, _completionService, _tokenProvider);

    [Fact]
    public async Task SubmitReassignmentAsync_issues_an_unassign_through_the_submission_service_and_returns_the_command_id()
    {
        ProtoV2.SubmitReassignmentRequest? captured = null;
        StubSubmitReassignment(r => captured = r);

        var submission = ReassignmentSubmission
            .Of(new UnassignCommand("00contract", Source, Target), Submitter)
            .WithCommandId(new RuntimeCommands.CommandId("corr-1"));

        var commandId = await CreateClient().SubmitReassignmentAsync(submission, cancellationToken: TestContext.Current.CancellationToken);

        commandId.Value.Should().Be("corr-1");
        captured.Should().NotBeNull();
        captured!.ReassignmentCommands.CommandId.Should().Be("corr-1");
        captured.ReassignmentCommands.Commands.Should().ContainSingle()
            .Which.UnassignCommand.ContractId.Should().Be("00contract");
    }

    [Fact]
    public async Task SubmitReassignmentAsync_mints_a_command_id_when_omitted_and_returns_it()
    {
        ProtoV2.SubmitReassignmentRequest? captured = null;
        StubSubmitReassignment(r => captured = r);

        var submission = ReassignmentSubmission.Of(
            new AssignCommand("reassign-1", Source, Target), Submitter);

        var commandId = await CreateClient().SubmitReassignmentAsync(submission, cancellationToken: TestContext.Current.CancellationToken);

        captured.Should().NotBeNull();
        commandId.Value.Should().Be(captured!.ReassignmentCommands.CommandId);
        Guid.TryParse(commandId.Value, out _).Should().BeTrue();
    }

    [Fact]
    public async Task TrySubmitAndWaitForReassignmentAsync_projects_the_resulting_Unassigned_event()
    {
        ProtoV2.SubmitAndWaitForReassignmentRequest? captured = null;
        StubSubmitAndWaitForReassignment(
            Reassigned(new ProtoV2.UnassignedEvent
            {
                ContractId = "00holding",
                TemplateId = new ProtoV2.Identifier { PackageId = "tmpl-pkg", ModuleName = "Sample.Token", EntityName = "Holding" },
                Source = "sync::source",
                Target = "sync::target",
                Offset = 7L,
            }),
            r => captured = r);

        var submission = ReassignmentSubmission.Of(
            new UnassignCommand("00holding", Source, Target), Submitter);

        var outcome = await CreateClient()
            .TrySubmitAndWaitForReassignmentAsync<TemplateMarker>(submission, cancellationToken: TestContext.Current.CancellationToken);

        var unassigned = outcome.Should().BeOfType<ExerciseOutcome<ContractStreamEvent<TemplateMarker>>.One>()
            .Which.Result.Should().BeOfType<ContractStreamEvent<TemplateMarker>.Unassigned>().Subject;
        unassigned.ContractId.Value.Should().Be("00holding");
        unassigned.Source.Id.Should().Be("sync::source");
        unassigned.Target.Id.Should().Be("sync::target");
        captured!.EventFormat.Should().NotBeNull("the await path requests events so the result can be projected");
    }

    [Fact]
    public async Task TrySubmitAndWaitForReassignmentAsync_maps_a_daml_rejection_to_a_DamlError_outcome()
    {
        StubSubmitAndWaitForReassignmentFailure(LedgerClientTestFixtures.MakeDamlRpcException(
            "CONTRACT_NOT_FOUND", "gone", "InvalidGivenCurrentSystemStateResourceMissing",
            StatusCode.NotFound));

        var submission = ReassignmentSubmission.Of(
            new AssignCommand("reassign-1", Source, Target), Submitter);

        var outcome = await CreateClient()
            .TrySubmitAndWaitForReassignmentAsync<TemplateMarker>(submission, cancellationToken: TestContext.Current.CancellationToken);

        outcome.Should().BeOfType<ExerciseOutcome<ContractStreamEvent<TemplateMarker>>.DamlError>()
            .Which.ErrorId.Should().Be("CONTRACT_NOT_FOUND");
    }

    [Fact]
    public async Task TrySubmitAndWaitForReassignmentAsync_surfaces_a_reassignment_with_no_events_as_an_empty_reassignment_Unclassified()
    {
        StubSubmitAndWaitForReassignment(new ProtoV2.SubmitAndWaitForReassignmentResponse
        {
            Reassignment = new ProtoV2.Reassignment { Offset = 9L },
        });

        var submission = ReassignmentSubmission.Of(
            new UnassignCommand("00holding", Source, Target), Submitter);

        var outcome = await CreateClient()
            .TrySubmitAndWaitForReassignmentAsync<TemplateMarker>(submission, cancellationToken: TestContext.Current.CancellationToken);

        var unclassified = outcome.Should().BeOfType<ExerciseOutcome<ContractStreamEvent<TemplateMarker>>.One>()
            .Which.Result.Should().BeOfType<ContractStreamEvent<TemplateMarker>.Unclassified>().Subject;
        unclassified.Offset.Should().Be(LedgerOffset.At(9L));
        unclassified.Kind.Should().Be(UnclassifiedKind.EmptyReassignment);
    }

    [Fact]
    public async Task TrySubmitAndWaitForReassignmentAsync_records_an_undecodable_response_as_an_activity_error()
    {
        StubSubmitAndWaitForReassignment(new ProtoV2.SubmitAndWaitForReassignmentResponse());

        using var capture = ActivityCapture.Of(LedgerActivitySourceNames.GrpcLedgerClient);

        var submission = ReassignmentSubmission.Of(
            new UnassignCommand("00holding", Source, Target), Submitter);

        var outcome = await CreateClient()
            .TrySubmitAndWaitForReassignmentAsync<TemplateMarker>(submission, cancellationToken: TestContext.Current.CancellationToken);

        var infra = outcome.Should()
            .BeOfType<ExerciseOutcome<ContractStreamEvent<TemplateMarker>>.InfraError>().Subject;
        infra.StatusCode.Should().Be((int)StatusCode.Internal);
        infra.Message.Should().StartWith("Could not decode the reassignment in the ledger response");
        infra.SourceException.Should().BeOfType<NullReferenceException>();

        var activity = capture.Activities.Should()
            .ContainSingle(a => a.OperationName.EndsWith(
                nameof(ICantonLedgerClient.TrySubmitAndWaitForReassignmentAsync), StringComparison.Ordinal))
            .Subject;
        activity.Status.Should().Be(ActivityStatusCode.Error);
        activity.GetTagItem(ActivityHelper.RpcGrpcStatusCode).Should().Be((int)StatusCode.Internal);
    }

    private static ProtoV2.SubmitAndWaitForReassignmentResponse Reassigned(ProtoV2.UnassignedEvent unassigned) =>
        new()
        {
            Reassignment = new ProtoV2.Reassignment
            {
                Offset = unassigned.Offset,
                Events = { new ProtoV2.ReassignmentEvent { Unassigned = unassigned } },
            },
        };

    private void StubSubmitReassignment(Action<ProtoV2.SubmitReassignmentRequest>? capture = null) =>
        _submissionService
            .SubmitReassignmentAsync(
                Arg.Do<ProtoV2.SubmitReassignmentRequest>(r => capture?.Invoke(r)),
                Arg.Any<Metadata>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(Unary(new ProtoV2.SubmitReassignmentResponse()));

    private void StubSubmitAndWaitForReassignment(
        ProtoV2.SubmitAndWaitForReassignmentResponse response,
        Action<ProtoV2.SubmitAndWaitForReassignmentRequest>? capture = null) =>
        _commandService
            .SubmitAndWaitForReassignmentAsync(
                Arg.Do<ProtoV2.SubmitAndWaitForReassignmentRequest>(r => capture?.Invoke(r)),
                Arg.Any<Metadata>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(Unary(response));

    [Fact]
    public async Task SubmitReassignmentAsync_surfaces_caller_cancellation_as_OperationCanceledException_carrying_the_RpcException()
    {
        using var cts = new CancellationTokenSource();
        var cancelled = new RpcException(new Status(StatusCode.Cancelled, "call cancelled"));
        _submissionService
            .SubmitReassignmentAsync(
                Arg.Any<ProtoV2.SubmitReassignmentRequest>(),
                Arg.Any<Metadata>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                cts.Cancel();
                return new AsyncUnaryCall<ProtoV2.SubmitReassignmentResponse>(
                    Task.FromException<ProtoV2.SubmitReassignmentResponse>(cancelled),
                    Task.FromResult(new Metadata()),
                    () => cancelled.Status,
                    () => cancelled.Trailers ?? new Metadata(),
                    () => { });
            });

        var submission = ReassignmentSubmission.Of(new UnassignCommand("00contract", Source, Target), Submitter);

        var act = async () => await CreateClient().SubmitReassignmentAsync(submission, cancellationToken: cts.Token);

        var thrown = await act.Should().ThrowAsync<OperationCanceledException>();
        thrown.Which.InnerException.Should().BeSameAs(cancelled);
    }

    [Fact]
    public async Task SubmitAndWaitAsync_surfaces_caller_cancellation_as_OperationCanceledException_carrying_the_RpcException()
    {
        using var cts = new CancellationTokenSource();
        var cancelled = new RpcException(new Status(StatusCode.Cancelled, "call cancelled"));
        _commandService
            .SubmitAndWaitAsync(
                Arg.Any<ProtoV2.SubmitAndWaitRequest>(),
                Arg.Any<Metadata>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                cts.Cancel();
                return new AsyncUnaryCall<ProtoV2.SubmitAndWaitResponse>(
                    Task.FromException<ProtoV2.SubmitAndWaitResponse>(cancelled),
                    Task.FromResult(new Metadata()),
                    () => cancelled.Status,
                    () => cancelled.Trailers ?? new Metadata(),
                    () => { });
            });

        var submission = RuntimeCommands.CommandsSubmission
            .Single(new RuntimeCommands.CreateCommand(
                new Identifier("pkg", "Module", "Template"), new DamlRecord(null, [])))
            .WithActAs(Submitter);

        var act = async () => await CreateClient().SubmitAndWaitAsync(submission, cancellationToken: cts.Token);

        var thrown = await act.Should().ThrowAsync<OperationCanceledException>();
        thrown.Which.InnerException.Should().BeSameAs(cancelled);
    }

    [Fact]
    public async Task TrySubmitAndWaitForReassignmentAsync_surfaces_caller_cancellation_as_OperationCanceledException_carrying_the_RpcException()
    {
        using var cts = new CancellationTokenSource();
        var cancelled = new RpcException(new Status(StatusCode.Cancelled, "call cancelled"));
        _commandService
            .SubmitAndWaitForReassignmentAsync(
                Arg.Any<ProtoV2.SubmitAndWaitForReassignmentRequest>(),
                Arg.Any<Metadata>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                cts.Cancel();
                return new AsyncUnaryCall<ProtoV2.SubmitAndWaitForReassignmentResponse>(
                    Task.FromException<ProtoV2.SubmitAndWaitForReassignmentResponse>(cancelled),
                    Task.FromResult(new Metadata()),
                    () => cancelled.Status,
                    () => cancelled.Trailers ?? new Metadata(),
                    () => { });
            });

        var submission = ReassignmentSubmission.Of(new UnassignCommand("00contract", Source, Target), Submitter);

        var act = async () => await CreateClient()
            .TrySubmitAndWaitForReassignmentAsync<TemplateMarker>(submission, cancellationToken: cts.Token);

        var thrown = await act.Should().ThrowAsync<OperationCanceledException>();
        thrown.Which.InnerException.Should().BeSameAs(cancelled);
    }

    private void StubSubmitAndWaitForReassignmentFailure(RpcException exception) =>
        _commandService
            .SubmitAndWaitForReassignmentAsync(
                Arg.Any<ProtoV2.SubmitAndWaitForReassignmentRequest>(),
                Arg.Any<Metadata>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(new AsyncUnaryCall<ProtoV2.SubmitAndWaitForReassignmentResponse>(
                Task.FromException<ProtoV2.SubmitAndWaitForReassignmentResponse>(exception),
                Task.FromResult(new Metadata()),
                () => exception.Status,
                () => exception.Trailers ?? new Metadata(),
                () => { }));

    private static AsyncUnaryCall<T> Unary<T>(T response) =>
        new(
            Task.FromResult(response),
            Task.FromResult(new Metadata()),
            () => Status.DefaultSuccess,
            () => new Metadata(),
            () => { });
}
