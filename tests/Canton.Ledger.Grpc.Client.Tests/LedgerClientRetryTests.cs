// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using Canton.Ledger.Kernel.Authentication;
using Canton.Ledger.Kernel.Resilience;
using Com.Daml.Ledger.Api.V2;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Outcomes;
using AwesomeAssertions;
using Grpc.Core;
using Grpc.Net.Client;
using NSubstitute;
using Xunit;
using ProtoExercisedEvent = Com.Daml.Ledger.Api.V2.ExercisedEvent;
using ProtoIdentifier = Com.Daml.Ledger.Api.V2.Identifier;
using RuntimeCommands = Daml.Runtime.Commands;
using RuntimeIdentifier = Daml.Runtime.Data.Identifier;
using Status = Grpc.Core.Status;

namespace Canton.Ledger.Grpc.Client.Tests;

public class LedgerClientRetryTests
{
    private const string RetryAttemptActivityName = "LedgerClient.RetryAttempt";
    private const int InitialAttempt = 1;
    private const int RetriesWhenTransient = 1;
    private static readonly Party ActAs = new("party::alice");

    /// <summary>
    /// Encodes the ADR 0006 retry policy per status code: only transient transport failures
    /// (<see cref="StatusCode.Unavailable"/>/<see cref="StatusCode.DeadlineExceeded"/>) are retried, so they
    /// reach <c>InitialAttempt + RetriesWhenTransient</c> calls; every other code — including
    /// <see cref="StatusCode.Aborted"/> optimistic-concurrency contention, which the SDK deliberately surfaces
    /// immediately rather than backing off — stops at the single <c>InitialAttempt</c> call.
    /// </summary>
    public static TheoryData<StatusCode, int> RetryDecisionByStatusCode => new()
    {
        { StatusCode.Unavailable, InitialAttempt + RetriesWhenTransient },
        { StatusCode.DeadlineExceeded, InitialAttempt + RetriesWhenTransient },
        { StatusCode.Aborted, InitialAttempt },
        { StatusCode.InvalidArgument, InitialAttempt },
        { StatusCode.ResourceExhausted, InitialAttempt },
    };

    private readonly LedgerClientOptions _options;
    private readonly GrpcChannel _channel;
    private readonly CommandService.CommandServiceClient _commandService;
    private readonly UpdateService.UpdateServiceClient _updateService;
    private readonly StateService.StateServiceClient _stateService;
    private readonly CommandSubmissionService.CommandSubmissionServiceClient _submissionService;
    private readonly ITokenProvider _tokenProvider = new StaticTokenProvider("test-token");

    public LedgerClientRetryTests()
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
    }

    private LedgerClient CreateClient() => new(
        _options,
        _channel,
        _commandService,
        _updateService,
        _stateService,
        _submissionService,
        new CommandCompletionService.CommandCompletionServiceClient(_channel),
        _tokenProvider);

    private void EnableRetry(int maxAttempts = 2, TimeSpan? delay = null) =>
        _options.Retry = new RetryOptions
        {
            Enabled = true,
            MaxRetryAttempts = maxAttempts,
            Delay = delay ?? TimeSpan.Zero,
        };

    private static RuntimeCommands.CommandsSubmission Create(string? commandId = "test-cmd")
    {
        var submission = RuntimeCommands.CommandsSubmission
            .Single(new RuntimeCommands.CreateCommand(
                new RuntimeIdentifier("pkg", "Module", "Template"),
                new DamlRecord(null, [])))
            .WithActAs(ActAs);
        return commandId is null ? submission : submission.WithCommandId(new RuntimeCommands.CommandId(commandId));
    }

    [Fact]
    public async Task TrySubmitAndWaitForTransactionAsync_does_not_retry_transient_failure_when_retry_disabled()
    {
        StubSubmitAndWaitForTransaction(Faulted<SubmitAndWaitForTransactionResponse>(Unavailable()));

        var client = CreateClient();
        var outcome = await client.TrySubmitAndWaitForTransactionAsync(Create(), cancellationToken: TestContext.Current.CancellationToken);

        outcome.Should().BeOfType<ExerciseOutcome<TransactionResult>.InfraError>();
        _ = _commandService.Received(1).SubmitAndWaitForTransactionAsync(
            Arg.Any<SubmitAndWaitForTransactionRequest>(), Arg.Any<Metadata>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TrySubmitAndWaitForTransactionAsync_retries_transient_failure_up_to_max_attempts_when_enabled()
    {
        EnableRetry(maxAttempts: 2);
        StubSubmitAndWaitForTransaction(
            Faulted<SubmitAndWaitForTransactionResponse>(Unavailable()),
            Faulted<SubmitAndWaitForTransactionResponse>(Unavailable()),
            Ok(new SubmitAndWaitForTransactionResponse { Transaction = new Transaction { UpdateId = "u-1", Offset = 1L } }));

        var client = CreateClient();
        var outcome = await client.TrySubmitAndWaitForTransactionAsync(Create(), cancellationToken: TestContext.Current.CancellationToken);

        outcome.Should().BeOfType<ExerciseOutcome<TransactionResult>.One>();
        _ = _commandService.Received(3).SubmitAndWaitForTransactionAsync(
            Arg.Any<SubmitAndWaitForTransactionRequest>(), Arg.Any<Metadata>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TrySubmitAndWaitForTransactionAsync_does_not_retry_business_failure_when_enabled()
    {
        EnableRetry();
        var damlError = LedgerClientTestFixtures.MakeDamlRpcException(
            "CONTRACT_NOT_FOUND", "unknown contract", "InvalidGivenCurrentSystemStateResourceMissing");
        StubSubmitAndWaitForTransaction(Faulted<SubmitAndWaitForTransactionResponse>(damlError));

        var client = CreateClient();
        var outcome = await client.TrySubmitAndWaitForTransactionAsync(Create(), cancellationToken: TestContext.Current.CancellationToken);

        outcome.Should().BeOfType<ExerciseOutcome<TransactionResult>.DamlError>();
        _ = _commandService.Received(1).SubmitAndWaitForTransactionAsync(
            Arg.Any<SubmitAndWaitForTransactionRequest>(), Arg.Any<Metadata>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TrySubmitAndWaitForTransactionAsync_keeps_first_attempt_DUPLICATE_COMMAND_as_a_DamlError()
    {
        EnableRetry();
        StubSubmitAndWaitForTransaction(
            Faulted<SubmitAndWaitForTransactionResponse>(DuplicateCommand(completionOffset: 42L)));

        var client = CreateClient();
        var outcome = await client.TrySubmitAndWaitForTransactionAsync(Create(), cancellationToken: TestContext.Current.CancellationToken);

        outcome.Should().BeOfType<ExerciseOutcome<TransactionResult>.DamlError>()
            .Which.ErrorId.Should().Be("DUPLICATE_COMMAND",
                "a first-attempt duplicate from a caller-chosen command_id is a genuine caller error");
        _ = _commandService.Received(1).SubmitAndWaitForTransactionAsync(
            Arg.Any<SubmitAndWaitForTransactionRequest>(), Arg.Any<Metadata>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>());
        _ = _updateService.DidNotReceive().GetUpdateByOffsetAsync(
            Arg.Any<GetUpdateByOffsetRequest>(), Arg.Any<Metadata>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TrySubmitAndWaitForTransactionAsync_maps_DUPLICATE_COMMAND_on_a_retried_attempt_to_success_of_the_original_submission()
    {
        EnableRetry(maxAttempts: 2);
        StubSubmitAndWaitForTransaction(
            Faulted<SubmitAndWaitForTransactionResponse>(Unavailable()),
            Faulted<SubmitAndWaitForTransactionResponse>(DuplicateCommand(completionOffset: 42L)));
        GetUpdateByOffsetRequest? pointRead = null;
        StubGetUpdateByOffset(
            new GetUpdateResponse { Transaction = new Transaction { UpdateId = "u-original", Offset = 42L } },
            r => pointRead = r);

        var client = CreateClient();
        var outcome = await client.TrySubmitAndWaitForTransactionAsync(Create(), cancellationToken: TestContext.Current.CancellationToken);

        var success = outcome.Should().BeOfType<ExerciseOutcome<TransactionResult>.One>(
            "the first attempt committed before its response was lost, so the deduplicated resubmission proves success").Subject;
        success.Result.UpdateId.Should().Be("u-original");
        pointRead.Should().NotBeNull();
        pointRead!.Offset.Should().Be(42L, "the committed transaction sits at the completion_offset carried in the error metadata");
    }

    [Fact]
    public async Task TrySubmitAndWaitForTransactionAsync_keeps_a_retried_DUPLICATE_COMMAND_as_a_DamlError_when_completion_offset_is_missing()
    {
        EnableRetry(maxAttempts: 2);
        StubSubmitAndWaitForTransaction(
            Faulted<SubmitAndWaitForTransactionResponse>(Unavailable()),
            Faulted<SubmitAndWaitForTransactionResponse>(DuplicateCommand(completionOffset: null)));

        var client = CreateClient();
        var outcome = await client.TrySubmitAndWaitForTransactionAsync(Create(), cancellationToken: TestContext.Current.CancellationToken);

        outcome.Should().BeOfType<ExerciseOutcome<TransactionResult>.DamlError>()
            .Which.ErrorId.Should().Be("DUPLICATE_COMMAND");
        _ = _updateService.DidNotReceive().GetUpdateByOffsetAsync(
            Arg.Any<GetUpdateByOffsetRequest>(), Arg.Any<Metadata>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TrySubmitAndWaitForTransactionAsync_keeps_a_retried_DUPLICATE_COMMAND_as_a_DamlError_when_the_point_read_fails()
    {
        EnableRetry(maxAttempts: 2);
        StubSubmitAndWaitForTransaction(
            Faulted<SubmitAndWaitForTransactionResponse>(Unavailable()),
            Faulted<SubmitAndWaitForTransactionResponse>(DuplicateCommand(completionOffset: 42L)));
        _updateService
            .GetUpdateByOffsetAsync(
                Arg.Any<GetUpdateByOffsetRequest>(), Arg.Any<Metadata>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(Faulted<GetUpdateResponse>(new RpcException(new Status(StatusCode.NotFound, "no update at offset"))));

        var client = CreateClient();
        var outcome = await client.TrySubmitAndWaitForTransactionAsync(Create(), cancellationToken: TestContext.Current.CancellationToken);

        outcome.Should().BeOfType<ExerciseOutcome<TransactionResult>.DamlError>()
            .Which.ErrorId.Should().Be("DUPLICATE_COMMAND");
    }

    [Fact]
    public async Task TrySubmitAndWaitForTransactionAsync_keeps_a_retried_DUPLICATE_COMMAND_as_a_DamlError_when_the_point_read_returns_a_non_transaction_update()
    {
        EnableRetry(maxAttempts: 2);
        StubSubmitAndWaitForTransaction(
            Faulted<SubmitAndWaitForTransactionResponse>(Unavailable()),
            Faulted<SubmitAndWaitForTransactionResponse>(DuplicateCommand(completionOffset: 42L)));
        _updateService
            .GetUpdateByOffsetAsync(
                Arg.Any<GetUpdateByOffsetRequest>(), Arg.Any<Metadata>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(Ok(new GetUpdateResponse { Reassignment = new Reassignment() }));

        var client = CreateClient();
        var outcome = await client.TrySubmitAndWaitForTransactionAsync(Create(), cancellationToken: TestContext.Current.CancellationToken);

        outcome.Should().BeOfType<ExerciseOutcome<TransactionResult>.DamlError>()
            .Which.ErrorId.Should().Be("DUPLICATE_COMMAND");
    }

    [Fact]
    public async Task TrySubmitAndWaitForTransactionAsync_keeps_a_retried_DUPLICATE_COMMAND_as_a_DamlError_when_the_point_read_transaction_is_undecodable()
    {
        EnableRetry(maxAttempts: 2);
        StubSubmitAndWaitForTransaction(
            Faulted<SubmitAndWaitForTransactionResponse>(Unavailable()),
            Faulted<SubmitAndWaitForTransactionResponse>(DuplicateCommand(completionOffset: 42L)));
        var undecodable = new Transaction { UpdateId = "u-poison", Offset = 42L };
        undecodable.Events.Add(new Event
        {
            Exercised = new ProtoExercisedEvent
            {
                ContractId = "00exer",
                TemplateId = new ProtoIdentifier { PackageId = "test-pkg", ModuleName = "Sample.Foo", EntityName = "FooBar" },
                Choice = "Accept",
                ExerciseResult = LedgerClientTestFixtures.OutOfDecimalRangeNumeric(),
            },
        });
        _updateService
            .GetUpdateByOffsetAsync(
                Arg.Any<GetUpdateByOffsetRequest>(), Arg.Any<Metadata>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(Ok(new GetUpdateResponse { Transaction = undecodable }));

        var client = CreateClient();
        var outcome = await client.TrySubmitAndWaitForTransactionAsync(Create(), cancellationToken: TestContext.Current.CancellationToken);

        outcome.Should().BeOfType<ExerciseOutcome<TransactionResult>.DamlError>()
            .Which.ErrorId.Should().Be("DUPLICATE_COMMAND");
    }

    [Fact]
    public async Task TrySubmitAndWaitForTransactionAsync_does_not_retry_non_transient_status_when_enabled()
    {
        EnableRetry();
        StubSubmitAndWaitForTransaction(
            Faulted<SubmitAndWaitForTransactionResponse>(new RpcException(new Status(StatusCode.InvalidArgument, "bad request"))));

        var client = CreateClient();
        var outcome = await client.TrySubmitAndWaitForTransactionAsync(Create(), cancellationToken: TestContext.Current.CancellationToken);

        outcome.Should().BeOfType<ExerciseOutcome<TransactionResult>.InfraError>();
        _ = _commandService.Received(1).SubmitAndWaitForTransactionAsync(
            Arg.Any<SubmitAndWaitForTransactionRequest>(), Arg.Any<Metadata>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Submit_sends_the_same_command_id_on_every_attempt_and_builds_commands_once()
    {
        EnableRetry(maxAttempts: 2);
        var sentCommandIds = new List<string>();
        StubSubmit(
            onRequest: r => sentCommandIds.Add(r.Commands.CommandId),
            Faulted<SubmitResponse>(Unavailable()),
            Faulted<SubmitResponse>(Unavailable()),
            Ok(new SubmitResponse()));

        var client = CreateClient();
        var returnedCommandId = await client.SubmitAsync(Create(commandId: null), TestContext.Current.CancellationToken);

        sentCommandIds.Should().HaveCount(3, "each of the three attempts submits once");
        sentCommandIds.Should().OnlyContain(id => id == sentCommandIds[0],
            "a stable command_id is fixed above the retry boundary and reused on every attempt");
        Guid.TryParse(sentCommandIds[0], out _).Should().BeTrue(
            "an omitted command_id is minted once, not re-minted per attempt");
        returnedCommandId.Value.Should().Be(sentCommandIds[0]);
    }

    [Fact]
    public async Task GetLedgerEndAsync_retries_transient_failure_when_enabled()
    {
        EnableRetry(maxAttempts: 2);
        StubGetLedgerEnd(
            Faulted<GetLedgerEndResponse>(Unavailable()),
            Faulted<GetLedgerEndResponse>(Unavailable()),
            Ok(new GetLedgerEndResponse { Offset = 42L }));

        var client = CreateClient();
        var offset = await client.GetLedgerEndAsync(cancellationToken: TestContext.Current.CancellationToken);

        offset.Value.Should().Be(42L);
        _ = _stateService.Received(3).GetLedgerEndAsync(
            Arg.Any<GetLedgerEndRequest>(), Arg.Any<Metadata>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [MemberData(nameof(RetryDecisionByStatusCode))]
    public async Task GetLedgerEndAsync_retries_only_transient_transport_status_codes(
        StatusCode statusCode, int expectedAttempts)
    {
        EnableRetry(maxAttempts: RetriesWhenTransient);
        StubGetLedgerEndAlwaysFaults(statusCode);

        var client = CreateClient();
        var act = () => client.GetLedgerEndAsync(cancellationToken: TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<RpcException>().Where(e => e.StatusCode == statusCode);
        _ = _stateService.Received(expectedAttempts).GetLedgerEndAsync(
            Arg.Any<GetLedgerEndRequest>(), Arg.Any<Metadata>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TrySubmitAndWaitForTransactionAsync_halts_retries_when_token_cancelled()
    {
        EnableRetry(maxAttempts: 5, delay: TimeSpan.FromSeconds(30));
        using var cts = new CancellationTokenSource();
        StubSubmitAndWaitForTransaction(
            onRequest: _ => cts.Cancel(),
            onDeadline: null,
            Faulted<SubmitAndWaitForTransactionResponse>(Unavailable()));

        var client = CreateClient();
        var act = () => client.TrySubmitAndWaitForTransactionAsync(Create(), cancellationToken: cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        _ = _commandService.Received(1).SubmitAndWaitForTransactionAsync(
            Arg.Any<SubmitAndWaitForTransactionRequest>(), Arg.Any<Metadata>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TrySubmitAndWaitForTransactionAsync_grants_a_fresh_deadline_to_every_attempt()
    {
        EnableRetry(maxAttempts: 2);
        var deadlines = new List<DateTime?>();
        StubSubmitAndWaitForTransaction(
            onRequest: null,
            onDeadline: deadlines.Add,
            Faulted<SubmitAndWaitForTransactionResponse>(Unavailable()),
            Faulted<SubmitAndWaitForTransactionResponse>(Unavailable()),
            Ok(new SubmitAndWaitForTransactionResponse { Transaction = new Transaction { UpdateId = "u-1", Offset = 1L } }));

        var client = CreateClient();
        await client.TrySubmitAndWaitForTransactionAsync(Create(), cancellationToken: TestContext.Current.CancellationToken);

        deadlines.Should().HaveCount(3, "the per-attempt deadline is recomputed on every attempt");
        deadlines.Should().OnlyContain(d => d.HasValue);
        deadlines.Select(d => d!.Value).Should().BeInAscendingOrder(
            "each attempt gets a fresh now+Timeout budget rather than one shared budget");
    }

    [Fact]
    public async Task Retry_attempts_emit_spans_on_the_shared_activity_source()
    {
        EnableRetry(maxAttempts: 2);
        var uniqueDetail = $"transient-{Guid.NewGuid()}";
        StubSubmitAndWaitForTransaction(
            Faulted<SubmitAndWaitForTransactionResponse>(new RpcException(new Status(StatusCode.Unavailable, uniqueDetail))),
            Faulted<SubmitAndWaitForTransactionResponse>(new RpcException(new Status(StatusCode.Unavailable, uniqueDetail))),
            Ok(new SubmitAndWaitForTransactionResponse { Transaction = new Transaction { UpdateId = "u-1", Offset = 1L } }));

        var activities = new ConcurrentQueue<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == LedgerClient.ActivitySourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = activities.Enqueue,
        };
        ActivitySource.AddActivityListener(listener);

        var client = CreateClient();
        await client.TrySubmitAndWaitForTransactionAsync(Create(), cancellationToken: TestContext.Current.CancellationToken);

        var retrySpans = activities
            .Where(a => a.OperationName == RetryAttemptActivityName && a.StatusDescription == uniqueDetail)
            .ToList();
        retrySpans.Should().HaveCount(2, "two retries precede the third, successful attempt");
        retrySpans.Select(a => a.GetTagItem(ActivityHelper.RpcGrpcStatusCode))
            .Should().AllBeEquivalentTo((int)StatusCode.Unavailable);
    }

    private void StubSubmitAndWaitForTransaction(params AsyncUnaryCall<SubmitAndWaitForTransactionResponse>[] calls)
        => StubSubmitAndWaitForTransaction(onRequest: null, onDeadline: null, calls);

    private void StubSubmitAndWaitForTransaction(
        Action<SubmitAndWaitForTransactionRequest>? onRequest,
        Action<DateTime?>? onDeadline,
        params AsyncUnaryCall<SubmitAndWaitForTransactionResponse>[] calls)
    {
        _commandService
            .SubmitAndWaitForTransactionAsync(
                Arg.Do<SubmitAndWaitForTransactionRequest>(r => onRequest?.Invoke(r)),
                Arg.Any<Metadata>(),
                Arg.Do<DateTime?>(d => onDeadline?.Invoke(d)),
                Arg.Any<CancellationToken>())
            .Returns(calls[0], calls[1..]);
    }

    private void StubSubmit(Action<SubmitRequest> onRequest, params AsyncUnaryCall<SubmitResponse>[] calls)
    {
        _submissionService
            .SubmitAsync(
                Arg.Do<SubmitRequest>(onRequest),
                Arg.Any<Metadata>(),
                Arg.Any<DateTime?>(),
                Arg.Any<CancellationToken>())
            .Returns(calls[0], calls[1..]);
    }

    private void StubGetLedgerEnd(params AsyncUnaryCall<GetLedgerEndResponse>[] calls)
    {
        _stateService
            .GetLedgerEndAsync(
                Arg.Any<GetLedgerEndRequest>(),
                Arg.Any<Metadata>(),
                Arg.Any<DateTime?>(),
                Arg.Any<CancellationToken>())
            .Returns(calls[0], calls[1..]);
    }

    private void StubGetLedgerEndAlwaysFaults(StatusCode statusCode)
    {
        _stateService
            .GetLedgerEndAsync(
                Arg.Any<GetLedgerEndRequest>(),
                Arg.Any<Metadata>(),
                Arg.Any<DateTime?>(),
                Arg.Any<CancellationToken>())
            .Returns(_ => Faulted<GetLedgerEndResponse>(new RpcException(new Status(statusCode, "boom"))));
    }

    private void StubGetUpdateByOffset(GetUpdateResponse response, Action<GetUpdateByOffsetRequest> capture)
    {
        _updateService
            .GetUpdateByOffsetAsync(
                Arg.Do(capture),
                Arg.Any<Metadata>(),
                Arg.Any<DateTime?>(),
                Arg.Any<CancellationToken>())
            .Returns(Ok(response));
    }

    private static RpcException Unavailable() => new(new Status(StatusCode.Unavailable, "transient down"));

    private static RpcException DuplicateCommand(long? completionOffset) =>
        LedgerClientTestFixtures.MakeDamlRpcException(
            "DUPLICATE_COMMAND",
            "duplicate",
            "InvalidGivenCurrentSystemStateResourceExists",
            StatusCode.AlreadyExists,
            completionOffset is { } offset
                ? new Dictionary<string, string> { ["completion_offset"] = offset.ToString(CultureInfo.InvariantCulture) }
                : null);

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
