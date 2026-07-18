// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Canton.Ledger.Kernel.Authentication;
using Com.Daml.Ledger.Api.V2;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Outcomes;
using Daml.Runtime.Streams;
using AwesomeAssertions;
using Grpc.Core;
using Grpc.Net.Client;
using NSubstitute;
using Xunit;
using RuntimeCommands = Daml.Runtime.Commands;
using RuntimeIdentifier = Daml.Runtime.Data.Identifier;
using ProtoExercisedEvent = Com.Daml.Ledger.Api.V2.ExercisedEvent;
using ProtoIdentifier = Com.Daml.Ledger.Api.V2.Identifier;
using Status = Grpc.Core.Status;

namespace Canton.Ledger.Grpc.Client.Tests;

public class LedgerClientDeadlineTests
{
    private static readonly Party ActAs = new("party::alice");

    private readonly LedgerClientOptions _options;
    private readonly GrpcChannel _channel;
    private readonly CommandService.CommandServiceClient _commandService;
    private readonly StateService.StateServiceClient _stateService;
    private readonly ITokenProvider _tokenProvider = new StaticTokenProvider("test-token");

    public LedgerClientDeadlineTests()
    {
        _options = new LedgerClientOptions
        {
            GrpcAddress = "https://localhost:5001",
            UserId = "test-user",
            Timeout = null,
        };
        _channel = GrpcChannel.ForAddress(_options.GrpcAddress);

        var callInvoker = Substitute.For<CallInvoker>();
        _commandService = Substitute.ForPartsOf<CommandService.CommandServiceClient>(callInvoker);
        _stateService = Substitute.ForPartsOf<StateService.StateServiceClient>(callInvoker);
    }

    private LedgerClient CreateClient() => new(
        _options,
        _channel,
        _commandService,
        new UpdateService.UpdateServiceClient(_channel),
        _stateService,
        _tokenProvider);

    private static RuntimeCommands.CommandsSubmission Create() =>
        RuntimeCommands.CommandsSubmission
            .Single(new RuntimeCommands.CreateCommand(
                new RuntimeIdentifier("pkg", "Module", "Template"),
                new DamlRecord(null, [])))
            .WithActAs(ActAs)
            .WithCommandId(new RuntimeCommands.CommandId("test-cmd"));

    [Fact]
    public async Task TrySubmitAndWaitForTransactionAsync_maps_per_call_timeout_to_call_deadline()
    {
        DateTime? captured = null;
        StubSubmitAndWaitForTransaction(onDeadline: d => captured = d, OkTransaction());
        var timeout = TimeSpan.FromMinutes(7);

        var before = DateTime.UtcNow;
        var client = CreateClient();
        _ = await client.TrySubmitAndWaitForTransactionAsync(
            Create(), timeout, TestContext.Current.CancellationToken);

        captured.Should().NotBeNull();
        captured!.Value.Should().BeCloseTo(before.Add(timeout), TimeSpan.FromSeconds(30));
    }

    [Fact]
    public async Task TrySubmitAndWaitForTransactionAsync_per_call_timeout_overrides_options_default()
    {
        _options.Timeout = TimeSpan.FromSeconds(30);
        DateTime? captured = null;
        StubSubmitAndWaitForTransaction(onDeadline: d => captured = d, OkTransaction());
        var timeout = TimeSpan.FromMinutes(7);

        var before = DateTime.UtcNow;
        var client = CreateClient();
        _ = await client.TrySubmitAndWaitForTransactionAsync(
            Create(), timeout, TestContext.Current.CancellationToken);

        captured.Should().NotBeNull();
        captured!.Value.Should().BeCloseTo(before.Add(timeout), TimeSpan.FromSeconds(30));
        captured.Value.Should().BeAfter(before.AddMinutes(1),
            "the per-call timeout takes precedence over the shorter options default");
    }

    [Fact]
    public async Task TrySubmitAndWaitForTransactionAsync_falls_back_to_options_timeout_when_per_call_timeout_null()
    {
        _options.Timeout = TimeSpan.FromSeconds(30);
        DateTime? captured = null;
        StubSubmitAndWaitForTransaction(onDeadline: d => captured = d, OkTransaction());

        var before = DateTime.UtcNow;
        var client = CreateClient();
        _ = await client.TrySubmitAndWaitForTransactionAsync(
            Create(), timeout: null, TestContext.Current.CancellationToken);

        captured.Should().NotBeNull();
        captured!.Value.Should().BeCloseTo(before.AddSeconds(30), TimeSpan.FromSeconds(15));
    }

    [Fact]
    public async Task TrySubmitAndWaitForTransactionAsync_deadline_overrun_surfaces_as_InfraError()
    {
        StubSubmitAndWaitForTransaction(onDeadline: null, Faulted<SubmitAndWaitForTransactionResponse>(DeadlineExceeded()));

        var client = CreateClient();
        var outcome = await client.TrySubmitAndWaitForTransactionAsync(
            Create(), TimeSpan.FromMilliseconds(1), TestContext.Current.CancellationToken);

        var infra = outcome.Should().BeOfType<ExerciseOutcome<TransactionResult>.InfraError>().Subject;
        infra.StatusCode.Should().Be((int)StatusCode.DeadlineExceeded);
    }

    [Fact]
    public async Task TrySubmitAndWaitForTransactionAsync_caller_cancellation_throws_OperationCanceledException()
    {
        StubSubmitAndWaitForTransaction(onDeadline: null, Faulted<SubmitAndWaitForTransactionResponse>(new RpcException(new Status(StatusCode.Cancelled, "cancelled"))));
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var client = CreateClient();
        var act = () => client.TrySubmitAndWaitForTransactionAsync(Create(), timeout: null, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>(
            "caller cancellation is not a transport failure and must never be mapped to InfraError");
    }

    [Fact]
    public async Task TryExerciseAsync_maps_per_call_timeout_to_call_deadline()
    {
        DateTime? captured = null;
        StubSubmitAndWaitForTransaction(onDeadline: d => captured = d, OkExercisedTransaction());
        var timeout = TimeSpan.FromMinutes(7);

        var before = DateTime.UtcNow;
        var client = CreateClient();
        _ = await client.TryExerciseAsync<object>(
            Exercise(), ActAs, timeout: timeout, cancellationToken: TestContext.Current.CancellationToken);

        captured.Should().NotBeNull();
        captured!.Value.Should().BeCloseTo(before.Add(timeout), TimeSpan.FromSeconds(30));
    }

    [Fact]
    public async Task SubmitAndWaitAsync_maps_per_call_timeout_to_call_deadline()
    {
        DateTime? captured = null;
        _commandService
            .SubmitAndWaitAsync(
                Arg.Any<SubmitAndWaitRequest>(),
                Arg.Any<Metadata>(),
                Arg.Do<DateTime?>(d => captured = d),
                Arg.Any<CancellationToken>())
            .Returns(Ok(new SubmitAndWaitResponse { UpdateId = "u-1", CompletionOffset = 1L }));
        var timeout = TimeSpan.FromMinutes(7);

        var before = DateTime.UtcNow;
        var client = CreateClient();
        _ = await client.SubmitAndWaitAsync(Create(), timeout, TestContext.Current.CancellationToken);

        captured.Should().NotBeNull();
        captured!.Value.Should().BeCloseTo(before.Add(timeout), TimeSpan.FromSeconds(30));
    }

    [Fact]
    public async Task GetLedgerEndAsync_maps_per_call_timeout_to_call_deadline()
    {
        DateTime? captured = null;
        _stateService
            .GetLedgerEndAsync(
                Arg.Any<GetLedgerEndRequest>(),
                Arg.Any<Metadata>(),
                Arg.Do<DateTime?>(d => captured = d),
                Arg.Any<CancellationToken>())
            .Returns(Ok(new GetLedgerEndResponse { Offset = 9L }));
        var timeout = TimeSpan.FromMinutes(7);

        var before = DateTime.UtcNow;
        var client = CreateClient();
        _ = await client.GetLedgerEndAsync(timeout, TestContext.Current.CancellationToken);

        captured.Should().NotBeNull();
        captured!.Value.Should().BeCloseTo(before.Add(timeout), TimeSpan.FromSeconds(30));
    }

    [Fact]
    public async Task TrySubmitAndWaitForTransactionAsync_server_Cancelled_maps_to_InfraError_when_caller_not_cancelled()
    {
        StubSubmitAndWaitForTransaction(onDeadline: null,
            Faulted<SubmitAndWaitForTransactionResponse>(new RpcException(new Status(StatusCode.Cancelled, "server cancelled"))));

        var client = CreateClient();
        var outcome = await client.TrySubmitAndWaitForTransactionAsync(
            Create(), timeout: null, TestContext.Current.CancellationToken);

        outcome.Should().BeOfType<ExerciseOutcome<TransactionResult>.InfraError>()
            .Which.StatusCode.Should().Be((int)StatusCode.Cancelled);
    }

    [Fact]
    public async Task TryCreateAsync_maps_per_call_timeout_to_call_deadline()
    {
        DateTime? captured = null;
        StubSubmitAndWaitForTransaction(onDeadline: d => captured = d, OkTransaction());
        var timeout = TimeSpan.FromMinutes(7);

        var before = DateTime.UtcNow;
        var client = CreateClient();
        _ = await client.TryCreateAsync(
            new LedgerClientTests.TestTemplate("owner"),
            submitter: ActAs,
            timeout: timeout,
            cancellationToken: TestContext.Current.CancellationToken);

        captured.Should().NotBeNull();
        captured!.Value.Should().BeCloseTo(before.Add(timeout), TimeSpan.FromSeconds(30));
    }

    [Fact]
    public async Task TryExerciseForCreatedAsync_maps_per_call_timeout_to_call_deadline()
    {
        DateTime? captured = null;
        StubSubmitAndWaitForTransaction(onDeadline: d => captured = d, OkTransaction());
        var timeout = TimeSpan.FromMinutes(7);

        var before = DateTime.UtcNow;
        var client = CreateClient();
        _ = await client.TryExerciseForCreatedAsync<LedgerClientTests.TestTemplate>(
            Exercise(), ActAs, timeout: timeout, cancellationToken: TestContext.Current.CancellationToken);

        captured.Should().NotBeNull();
        captured!.Value.Should().BeCloseTo(before.Add(timeout), TimeSpan.FromSeconds(30));
    }

    [Fact]
    public async Task TrySubmitAndWaitForReassignmentAsync_maps_per_call_timeout_to_call_deadline()
    {
        DateTime? captured = null;
        _commandService
            .SubmitAndWaitForReassignmentAsync(
                Arg.Any<SubmitAndWaitForReassignmentRequest>(),
                Arg.Any<Metadata>(),
                Arg.Do<DateTime?>(d => captured = d),
                Arg.Any<CancellationToken>())
            .Returns(Ok(new SubmitAndWaitForReassignmentResponse
            {
                Reassignment = new Reassignment { Offset = 1L },
            }));
        var timeout = TimeSpan.FromMinutes(7);

        var before = DateTime.UtcNow;
        var client = CreateClient();
        _ = await client.TrySubmitAndWaitForReassignmentAsync<LedgerClientTests.TestTemplate>(
            Reassign(), timeout, TestContext.Current.CancellationToken);

        captured.Should().NotBeNull();
        captured!.Value.Should().BeCloseTo(before.Add(timeout), TimeSpan.FromSeconds(30));
    }

    [Fact]
    public async Task TrySubmitAndWaitForReassignmentAsync_caller_cancellation_throws_OperationCanceledException()
    {
        StubSubmitAndWaitForReassignment(
            Faulted<SubmitAndWaitForReassignmentResponse>(new RpcException(new Status(StatusCode.Cancelled, "cancelled"))));
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var client = CreateClient();
        var act = () => client.TrySubmitAndWaitForReassignmentAsync<LedgerClientTests.TestTemplate>(
            Reassign(), timeout: null, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>(
            "caller cancellation is not a transport failure and must never be mapped to InfraError");
    }

    [Fact]
    public async Task TrySubmitAndWaitForReassignmentAsync_server_Cancelled_maps_to_InfraError_when_caller_not_cancelled()
    {
        StubSubmitAndWaitForReassignment(
            Faulted<SubmitAndWaitForReassignmentResponse>(new RpcException(new Status(StatusCode.Cancelled, "server cancelled"))));

        var client = CreateClient();
        var outcome = await client.TrySubmitAndWaitForReassignmentAsync<LedgerClientTests.TestTemplate>(
            Reassign(), timeout: null, TestContext.Current.CancellationToken);

        outcome.Should().BeOfType<ExerciseOutcome<ContractStreamEvent<LedgerClientTests.TestTemplate>>.InfraError>()
            .Which.StatusCode.Should().Be((int)StatusCode.Cancelled);
    }

    private void StubSubmitAndWaitForReassignment(AsyncUnaryCall<SubmitAndWaitForReassignmentResponse> call) =>
        _commandService
            .SubmitAndWaitForReassignmentAsync(
                Arg.Any<SubmitAndWaitForReassignmentRequest>(),
                Arg.Any<Metadata>(),
                Arg.Any<DateTime?>(),
                Arg.Any<CancellationToken>())
            .Returns(call);

    private void StubSubmitAndWaitForTransaction(
        Action<DateTime?>? onDeadline,
        AsyncUnaryCall<SubmitAndWaitForTransactionResponse> call)
    {
        _commandService
            .SubmitAndWaitForTransactionAsync(
                Arg.Any<SubmitAndWaitForTransactionRequest>(),
                Arg.Any<Metadata>(),
                Arg.Do<DateTime?>(d => onDeadline?.Invoke(d)),
                Arg.Any<CancellationToken>())
            .Returns(call);
    }

    private static RuntimeCommands.ExerciseCommand Exercise() =>
        new(
            new RuntimeIdentifier("pkg", "Module", "Template"),
            new ContractId<LedgerClientTests.TestTemplate>("00contract123"),
            new RuntimeCommands.ChoiceName("Archive"),
            DamlUnit.Instance);

    private static ReassignmentSubmission Reassign() =>
        ReassignmentSubmission.Of(
            new UnassignCommand(
                "00contract123",
                new SynchronizerId("sync::source"),
                new SynchronizerId("sync::target")),
            ActAs);

    private static AsyncUnaryCall<SubmitAndWaitForTransactionResponse> OkTransaction() =>
        Ok(new SubmitAndWaitForTransactionResponse
        {
            Transaction = new Transaction { UpdateId = "u-1", Offset = 1L },
        });

    private static AsyncUnaryCall<SubmitAndWaitForTransactionResponse> OkExercisedTransaction()
    {
        var transaction = new Transaction { UpdateId = "u-1", Offset = 1L };
        transaction.Events.Add(new Event
        {
            Exercised = new ProtoExercisedEvent
            {
                ContractId = "00contract123",
                TemplateId = new ProtoIdentifier { PackageId = "pkg", ModuleName = "Module", EntityName = "Template" },
                Choice = "Archive",
            }
        });
        return Ok(new SubmitAndWaitForTransactionResponse { Transaction = transaction });
    }

    private static RpcException DeadlineExceeded() =>
        new(new Status(StatusCode.DeadlineExceeded, "deadline exceeded"));

    private static AsyncUnaryCall<T> Faulted<T>(RpcException exception) =>
        new(
            Task.FromException<T>(exception),
            Task.FromResult(new Metadata()),
            () => exception.Status,
            () => exception.Trailers ?? new Metadata(),
            () => { });

    private static AsyncUnaryCall<T> Ok<T>(T value) =>
        new(
            Task.FromResult(value),
            Task.FromResult(new Metadata()),
            () => Status.DefaultSuccess,
            () => new Metadata(),
            () => { });
}
