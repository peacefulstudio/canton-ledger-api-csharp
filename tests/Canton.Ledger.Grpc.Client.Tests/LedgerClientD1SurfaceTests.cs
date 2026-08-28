// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Canton.Ledger.Abstractions;
using Canton.Ledger.Kernel.Authentication;
using Com.Daml.Ledger.Api.V2;
using Daml.Runtime.Data;
using AwesomeAssertions;
using Grpc.Core;
using Grpc.Net.Client;
using NSubstitute;
using Xunit;
using Google.Protobuf.WellKnownTypes;
using Interactive = Com.Daml.Ledger.Api.V2.Interactive;
using RuntimeCommands = Daml.Runtime.Commands;
using ProtoCreatedEvent = Com.Daml.Ledger.Api.V2.CreatedEvent;
using ProtoExercisedEvent = Com.Daml.Ledger.Api.V2.ExercisedEvent;
using ProtoIdentifier = Com.Daml.Ledger.Api.V2.Identifier;
using ProtoRecord = Com.Daml.Ledger.Api.V2.Record;
using Status = Grpc.Core.Status;

namespace Canton.Ledger.Grpc.Client.Tests;

public class LedgerClientD1SurfaceTests
{
    private static readonly Party ActAs = new("party::alice");

    private readonly LedgerClientOptions _options;
    private readonly GrpcChannel _channel;
    private readonly CommandService.CommandServiceClient _commandService;
    private readonly UpdateService.UpdateServiceClient _updateService;
    private readonly StateService.StateServiceClient _stateService;
    private readonly CommandSubmissionService.CommandSubmissionServiceClient _submissionService;
    private readonly CommandCompletionService.CommandCompletionServiceClient _completionService;
    private readonly VersionService.VersionServiceClient _versionService;
    private readonly Interactive.InteractiveSubmissionService.InteractiveSubmissionServiceClient _interactiveSubmissionService;
    private readonly ITokenProvider _tokenProvider = new StaticTokenProvider("test-token");

    public LedgerClientD1SurfaceTests()
    {
        _options = new LedgerClientOptions
        {
            GrpcAddress = "https://localhost:5001",
            UserId = "test-user",
        };
        _channel = GrpcChannel.ForAddress(_options.GrpcAddress);

        var callInvoker = Substitute.For<CallInvoker>();
        _commandService = Substitute.ForPartsOf<CommandService.CommandServiceClient>(callInvoker);
        _updateService = Substitute.ForPartsOf<UpdateService.UpdateServiceClient>(callInvoker);
        _stateService = Substitute.ForPartsOf<StateService.StateServiceClient>(callInvoker);
        _submissionService = Substitute.ForPartsOf<CommandSubmissionService.CommandSubmissionServiceClient>(callInvoker);
        _completionService = Substitute.ForPartsOf<CommandCompletionService.CommandCompletionServiceClient>(callInvoker);
        _versionService = Substitute.ForPartsOf<VersionService.VersionServiceClient>(callInvoker);
        _interactiveSubmissionService = Substitute
            .ForPartsOf<Interactive.InteractiveSubmissionService.InteractiveSubmissionServiceClient>(callInvoker);
    }

    private LedgerClient CreateClient() => new(
        _options,
        _channel,
        _commandService,
        _updateService,
        _stateService,
        _submissionService,
        _completionService,
        _tokenProvider,
        _versionService,
        _interactiveSubmissionService);

    [Fact]
    public async Task GetConnectedSynchronizersAsync_projects_response_entries()
    {
        var response = new GetConnectedSynchronizersResponse();
        response.ConnectedSynchronizers.Add(new GetConnectedSynchronizersResponse.Types.ConnectedSynchronizer
        {
            SynchronizerAlias = "global",
            SynchronizerId = "sync::global",
            Permission = ParticipantPermission.Submission,
        });
        StubGetConnectedSynchronizers(response);

        var client = CreateClient();
        var synchronizers = await client.GetConnectedSynchronizersAsync(cancellationToken: TestContext.Current.CancellationToken);

        var synchronizer = synchronizers.Should().ContainSingle().Subject;
        synchronizer.SynchronizerAlias.Should().Be("global");
        synchronizer.SynchronizerId.Should().Be("sync::global");
        synchronizer.Permission.Should().Be(SynchronizerPermissionLevel.Submission);
    }

    [Fact]
    public async Task GetConnectedSynchronizersAsync_passes_party_and_participantId_when_provided()
    {
        GetConnectedSynchronizersRequest? captured = null;
        StubGetConnectedSynchronizers(new GetConnectedSynchronizersResponse(), r => captured = r);

        var client = CreateClient();
        await client.GetConnectedSynchronizersAsync(ActAs, "participant-1", TestContext.Current.CancellationToken);

        captured.Should().NotBeNull();
        captured!.Party.Should().Be("party::alice");
        captured.ParticipantId.Should().Be("participant-1");
    }

    [Fact]
    public async Task GetConnectedSynchronizersAsync_leaves_party_and_participantId_unset_by_default()
    {
        GetConnectedSynchronizersRequest? captured = null;
        StubGetConnectedSynchronizers(new GetConnectedSynchronizersResponse(), r => captured = r);

        var client = CreateClient();
        await client.GetConnectedSynchronizersAsync(cancellationToken: TestContext.Current.CancellationToken);

        captured.Should().NotBeNull();
        captured!.Party.Should().BeEmpty();
        captured.ParticipantId.Should().BeEmpty();
    }

    [Fact]
    public async Task GetLedgerApiVersionAsync_returns_version_string()
    {
        _versionService
            .GetLedgerApiVersionAsync(
                Arg.Any<GetLedgerApiVersionRequest>(),
                Arg.Any<Metadata>(),
                Arg.Any<DateTime?>(),
                Arg.Any<CancellationToken>())
            .Returns(new AsyncUnaryCall<GetLedgerApiVersionResponse>(
                Task.FromResult(new GetLedgerApiVersionResponse { Version = "3.5.9" }),
                Task.FromResult(new Metadata()),
                () => Status.DefaultSuccess,
                () => new Metadata(),
                () => { }));

        var client = CreateClient();
        var version = await client.GetLedgerApiVersionAsync(TestContext.Current.CancellationToken);

        version.Should().Be("3.5.9");
    }

    [Fact]
    public async Task GetUpdateByOffsetAsync_projects_transaction()
    {
        var transaction = new Transaction { UpdateId = "update-1", Offset = 42L };
        transaction.Events.Add(new Event
        {
            Created = new ProtoCreatedEvent
            {
                ContractId = "00contract1",
                TemplateId = new ProtoIdentifier { PackageId = "pkg", ModuleName = "Module", EntityName = "Template" },
                CreateArguments = new ProtoRecord(),
            },
        });
        StubGetUpdateByOffset(new GetUpdateResponse { Transaction = transaction });

        var client = CreateClient();
        var result = await client.GetUpdateByOffsetAsync(42L, ActAs, TestContext.Current.CancellationToken);

        result.UpdateId.Should().Be("update-1");
        result.CreatedContracts.Should().ContainSingle().Which.ContractId.Should().Be("00contract1");
    }

    [Fact]
    public async Task GetUpdateByOffsetAsync_surfaces_command_id_when_present()
    {
        var transaction = new Transaction { UpdateId = "update-1", Offset = 42L, CommandId = "cmd-read" };
        StubGetUpdateByOffset(new GetUpdateResponse { Transaction = transaction });

        var client = CreateClient();
        var result = await client.GetUpdateByOffsetAsync(42L, ActAs, TestContext.Current.CancellationToken);

        result.CommandId.Should().Be(new RuntimeCommands.CommandId("cmd-read"));
    }

    [Fact]
    public async Task GetUpdateByOffsetAsync_yields_default_command_id_when_transaction_carries_none()
    {
        var transaction = new Transaction { UpdateId = "update-1", Offset = 42L };
        StubGetUpdateByOffset(new GetUpdateResponse { Transaction = transaction });

        var client = CreateClient();
        var act = () => client.GetUpdateByOffsetAsync(42L, ActAs, TestContext.Current.CancellationToken);

        var result = await act.Should().NotThrowAsync(
            "a transaction the reader did not submit carries an empty command_id, which must not throw");
        result.Subject.CommandId.Should().Be(default(RuntimeCommands.CommandId));
    }

    [Fact]
    public async Task GetUpdateByOffsetAsync_sends_requested_offset_and_wildcard_filter_for_submitter_parties()
    {
        GetUpdateByOffsetRequest? captured = null;
        StubGetUpdateByOffset(new GetUpdateResponse { Transaction = new Transaction() }, r => captured = r);

        var submitter = new RuntimeCommands.SubmitterInfo(
            new HashSet<Party> { (Party)"alice" },
            new HashSet<Party> { (Party)"bob" });

        var client = CreateClient();
        await client.GetUpdateByOffsetAsync(99L, submitter, TestContext.Current.CancellationToken);

        captured.Should().NotBeNull();
        captured!.Offset.Should().Be(99L);
        captured.UpdateFormat.IncludeTransactions.TransactionShape.Should().Be(TransactionShape.LedgerEffects);
        captured.UpdateFormat.IncludeTransactions.EventFormat.FiltersByParty.Keys.Should().BeEquivalentTo(["alice", "bob"]);
    }

    [Fact]
    public async Task GetUpdateByOffsetAsync_builds_true_wildcard_filter_with_empty_cumulative_for_submitter_parties()
    {
        GetUpdateByOffsetRequest? captured = null;
        StubGetUpdateByOffset(new GetUpdateResponse { Transaction = new Transaction() }, r => captured = r);

        var submitter = new RuntimeCommands.SubmitterInfo(
            new HashSet<Party> { (Party)"alice" },
            new HashSet<Party> { (Party)"bob" });

        var client = CreateClient();
        await client.GetUpdateByOffsetAsync(99L, submitter, TestContext.Current.CancellationToken);

        captured.Should().NotBeNull();
        var filtersByParty = captured!.UpdateFormat.IncludeTransactions.EventFormat.FiltersByParty;
        filtersByParty.Should().HaveCount(2);
        filtersByParty.Values.Should().OnlyContain(filters => filters.Cumulative.Count == 0);
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(-1L)]
    public async Task GetUpdateByOffsetAsync_rejects_non_positive_offset(long offset)
    {
        var client = CreateClient();
        var act = () => client.GetUpdateByOffsetAsync(offset, ActAs, TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }

    [Fact]
    public async Task GetUpdateByOffsetAsync_throws_descriptive_malformed_response_error_when_created_event_misses_template_id()
    {
        var transaction = new Transaction { UpdateId = "update-1", Offset = 42L };
        transaction.Events.Add(new Event
        {
            Created = new ProtoCreatedEvent { ContractId = "00broken", CreateArguments = new ProtoRecord() },
        });
        StubGetUpdateByOffset(new GetUpdateResponse { Transaction = transaction });

        var client = CreateClient();
        var act = () => client.GetUpdateByOffsetAsync(42L, ActAs, TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Malformed response from ledger*offset 42*template_id*");
    }

    [Fact]
    public async Task GetUpdateByOffsetAsync_names_lookup_offset_when_transaction_value_cannot_be_decoded()
    {
        var transaction = new Transaction { UpdateId = "update-1", Offset = 42L };
        transaction.Events.Add(new Event
        {
            Exercised = new ProtoExercisedEvent
            {
                ContractId = "00exer",
                TemplateId = new ProtoIdentifier { PackageId = "pkg", ModuleName = "Module", EntityName = "Template" },
                Choice = "Accept",
                ChoiceArgument = new Com.Daml.Ledger.Api.V2.Value(),
            },
        });
        StubGetUpdateByOffset(new GetUpdateResponse { Transaction = transaction });

        var client = CreateClient();
        var act = () => client.GetUpdateByOffsetAsync(42L, ActAs, TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Malformed response from ledger*offset 42*");
    }

    [Fact]
    public async Task GetUpdateByOffsetAsync_throws_when_update_is_not_a_transaction()
    {
        StubGetUpdateByOffset(new GetUpdateResponse { Reassignment = new Reassignment() });

        var client = CreateClient();
        var act = () => client.GetUpdateByOffsetAsync(1L, ActAs, TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Reassignment*");
    }

    [Fact]
    public async Task GetUpdateByIdAsync_projects_transaction()
    {
        var transaction = new Transaction { UpdateId = "update-2", Offset = 7L };
        StubGetUpdateById(new GetUpdateResponse { Transaction = transaction });

        var client = CreateClient();
        var result = await client.GetUpdateByIdAsync("update-2", ActAs, TestContext.Current.CancellationToken);

        result.UpdateId.Should().Be("update-2");
    }

    [Fact]
    public async Task GetUpdateByIdAsync_sends_requested_id()
    {
        GetUpdateByIdRequest? captured = null;
        StubGetUpdateById(new GetUpdateResponse { Transaction = new Transaction() }, r => captured = r);

        var client = CreateClient();
        await client.GetUpdateByIdAsync("update-xyz", ActAs, TestContext.Current.CancellationToken);

        captured.Should().NotBeNull();
        captured!.UpdateId.Should().Be("update-xyz");
    }

    [Fact]
    public async Task GetUpdateByIdAsync_throws_descriptive_malformed_response_error_when_exercised_event_misses_template_id()
    {
        var transaction = new Transaction { UpdateId = "update-2", Offset = 7L };
        transaction.Events.Add(new Event
        {
            Exercised = new ProtoExercisedEvent { ContractId = "00broken", Choice = "Accept" },
        });
        StubGetUpdateById(new GetUpdateResponse { Transaction = transaction });

        var client = CreateClient();
        var act = () => client.GetUpdateByIdAsync("update-2", ActAs, TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Malformed response from ledger*update-2*template_id*");
    }

    [Fact]
    public async Task GetUpdateByIdAsync_throws_when_update_is_not_a_transaction()
    {
        StubGetUpdateById(new GetUpdateResponse { TopologyTransaction = new TopologyTransaction() });

        var client = CreateClient();
        var act = () => client.GetUpdateByIdAsync("update-3", ActAs, TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*TopologyTransaction*");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task GetUpdateByIdAsync_rejects_null_or_whitespace_updateId(string? updateId)
    {
        var client = CreateClient();
        var act = () => client.GetUpdateByIdAsync(updateId!, ActAs, TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    private void StubGetConnectedSynchronizers(
        GetConnectedSynchronizersResponse response,
        Action<GetConnectedSynchronizersRequest>? capture = null)
    {
        _stateService
            .GetConnectedSynchronizersAsync(
                Arg.Do<GetConnectedSynchronizersRequest>(r => capture?.Invoke(r)),
                Arg.Any<Metadata>(),
                Arg.Any<DateTime?>(),
                Arg.Any<CancellationToken>())
            .Returns(new AsyncUnaryCall<GetConnectedSynchronizersResponse>(
                Task.FromResult(response),
                Task.FromResult(new Metadata()),
                () => Status.DefaultSuccess,
                () => new Metadata(),
                () => { }));
    }

    private void StubGetUpdateByOffset(
        GetUpdateResponse response,
        Action<GetUpdateByOffsetRequest>? capture = null)
    {
        _updateService
            .GetUpdateByOffsetAsync(
                Arg.Do<GetUpdateByOffsetRequest>(r => capture?.Invoke(r)),
                Arg.Any<Metadata>(),
                Arg.Any<DateTime?>(),
                Arg.Any<CancellationToken>())
            .Returns(new AsyncUnaryCall<GetUpdateResponse>(
                Task.FromResult(response),
                Task.FromResult(new Metadata()),
                () => Status.DefaultSuccess,
                () => new Metadata(),
                () => { }));
    }

    private void StubGetUpdateById(
        GetUpdateResponse response,
        Action<GetUpdateByIdRequest>? capture = null)
    {
        _updateService
            .GetUpdateByIdAsync(
                Arg.Do<GetUpdateByIdRequest>(r => capture?.Invoke(r)),
                Arg.Any<Metadata>(),
                Arg.Any<DateTime?>(),
                Arg.Any<CancellationToken>())
            .Returns(new AsyncUnaryCall<GetUpdateResponse>(
                Task.FromResult(response),
                Task.FromResult(new Metadata()),
                () => Status.DefaultSuccess,
                () => new Metadata(),
                () => { }));
    }

    [Fact]
    public async Task EstimateTrafficCostAsync_projects_every_cost_component_and_the_estimation_timestamp()
    {
        var estimatedAt = new DateTimeOffset(2026, 8, 15, 9, 30, 0, TimeSpan.Zero);
        StubPrepareSubmission(new Interactive.PrepareSubmissionResponse
        {
            CostEstimation = new Interactive.CostEstimation
            {
                EstimationTimestamp = Timestamp.FromDateTimeOffset(estimatedAt),
                ConfirmationRequestTrafficCostEstimation = 3000UL,
                ConfirmationResponseTrafficCostEstimation = 1096UL,
                TotalTrafficCostEstimation = 4096UL,
            },
        });

        var estimate = await CreateClient().EstimateTrafficCostAsync(
            SingleCreateSubmission(), cancellationToken: TestContext.Current.CancellationToken);

        estimate.Should().NotBeNull();
        estimate!.EstimatedAt.Should().Be(estimatedAt);
        estimate.ConfirmationRequestCost.Should().Be(3000L);
        estimate.ConfirmationResponseCost.Should().Be(1096L);
        estimate.TotalCost.Should().Be(4096L);
    }

    [Fact]
    public async Task EstimateTrafficCostAsync_returns_null_when_participant_omits_cost_estimation()
    {
        StubPrepareSubmission(new Interactive.PrepareSubmissionResponse());

        var estimate = await CreateClient().EstimateTrafficCostAsync(
            SingleCreateSubmission(), cancellationToken: TestContext.Current.CancellationToken);

        estimate.Should().BeNull();
    }

    [Fact]
    public async Task EstimateTrafficCostAsync_keeps_the_costs_when_the_participant_omits_the_timestamp()
    {
        StubPrepareSubmission(new Interactive.PrepareSubmissionResponse
        {
            CostEstimation = new Interactive.CostEstimation { TotalTrafficCostEstimation = 512UL },
        });

        var estimate = await CreateClient().EstimateTrafficCostAsync(
            SingleCreateSubmission(), cancellationToken: TestContext.Current.CancellationToken);

        estimate.Should().NotBeNull();
        estimate!.EstimatedAt.Should().BeNull();
        estimate.TotalCost.Should().Be(512L);
    }

    [Fact]
    public async Task EstimateTrafficCostAsync_throws_when_a_cost_exceeds_the_signed_range()
    {
        StubPrepareSubmission(new Interactive.PrepareSubmissionResponse
        {
            CostEstimation = new Interactive.CostEstimation
            {
                TotalTrafficCostEstimation = (ulong)long.MaxValue + 1UL,
            },
        });

        var act = () => CreateClient().EstimateTrafficCostAsync(
            SingleCreateSubmission(), cancellationToken: TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*total traffic cost of 9223372036854775808 bytes*");
    }

    [Fact]
    public async Task EstimateTrafficCostAsync_sends_the_submission_and_asks_for_cost_estimation()
    {
        Interactive.PrepareSubmissionRequest? captured = null;
        StubPrepareSubmission(new Interactive.PrepareSubmissionResponse(), r => captured = r);

        await CreateClient().EstimateTrafficCostAsync(
            SingleCreateSubmission(), cancellationToken: TestContext.Current.CancellationToken);

        captured.Should().NotBeNull();
        captured!.EstimateTrafficCost.Should().NotBeNull();
        captured.ActAs.Should().ContainSingle().Which.Should().Be("party::alice");
        captured.Commands.Should().ContainSingle();
        captured.UserId.Should().Be("test-user");
        captured.CommandId.Should().NotBeEmpty();
    }

    private static RuntimeCommands.CommandsSubmission SingleCreateSubmission() =>
        RuntimeCommands.CommandsSubmission.Single(RuntimeCommands.CreateCommand.For(new FooBar("alice")), ActAs);

    private void StubPrepareSubmission(
        Interactive.PrepareSubmissionResponse response,
        Action<Interactive.PrepareSubmissionRequest>? capture = null)
    {
        _interactiveSubmissionService
            .PrepareSubmissionAsync(
                Arg.Do<Interactive.PrepareSubmissionRequest>(r => capture?.Invoke(r)),
                Arg.Any<Metadata>(),
                Arg.Any<DateTime?>(),
                Arg.Any<CancellationToken>())
            .Returns(new AsyncUnaryCall<Interactive.PrepareSubmissionResponse>(
                Task.FromResult(response),
                Task.FromResult(new Metadata()),
                () => Status.DefaultSuccess,
                () => new Metadata(),
                () => { }));
    }
}
