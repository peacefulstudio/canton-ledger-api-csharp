// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Canton.Ledger.Abstractions;
using Canton.Ledger.Kernel.Authentication;
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

public class LedgerClientReassignmentWriteTests
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

        var commandId = await CreateClient().SubmitReassignmentAsync(submission, TestContext.Current.CancellationToken);

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

        var commandId = await CreateClient().SubmitReassignmentAsync(submission, TestContext.Current.CancellationToken);

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
        unclassified.Offset.Value.Should().Be(9L);
        unclassified.Kind.Should().Be(UnclassifiedKind.EmptyReassignment);
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
