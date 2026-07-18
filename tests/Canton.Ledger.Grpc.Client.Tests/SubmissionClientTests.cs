// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using Canton.Ledger.Kernel.Authentication;
using Canton.Ledger.Kernel.Resilience;
using Com.Daml.Ledger.Api.V2;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Outcomes;
using AwesomeAssertions;
using Grpc.Core;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;
using RuntimeCommands = Daml.Runtime.Commands;
using RuntimeIdentifier = Daml.Runtime.Data.Identifier;
using Status = Grpc.Core.Status;

namespace Canton.Ledger.Grpc.Client.Tests;

public class SubmissionClientTests
{
    private static readonly Party ActAs = new("party::alice");
    private static readonly RuntimeCommands.CommandId TestCommandId = new("test-cmd");

    private readonly LedgerClientOptions _options;
    private readonly CommandService.CommandServiceClient _commandService;
    private readonly CommandSubmissionService.CommandSubmissionServiceClient _commandSubmissionService;
    private readonly ITokenProvider _tokenProvider = new StaticTokenProvider("test-token");

    public SubmissionClientTests()
    {
        _options = new LedgerClientOptions
        {
            GrpcAddress = "https://localhost:5001",
            UserId = "test-user",
        };

        var callInvoker = Substitute.For<CallInvoker>();
        _commandService = Substitute.ForPartsOf<CommandService.CommandServiceClient>(callInvoker);
        _commandSubmissionService = Substitute.ForPartsOf<CommandSubmissionService.CommandSubmissionServiceClient>(callInvoker);
    }

    private SubmissionClient CreateClient(
        Func<long, RuntimeCommands.SubmitterInfo, CancellationToken, Task<TransactionResult>>? pointReadByOffset = null) =>
        new(
            new LedgerCallInvoker(_options, _tokenProvider),
            _commandService,
            _commandSubmissionService,
            _options,
            NullLogger<LedgerClient>.Instance,
            pointReadByOffset ?? ((_, _, _) => throw new InvalidOperationException("point read not expected")));

    private static RuntimeCommands.CommandsSubmission Create() =>
        RuntimeCommands.CommandsSubmission
            .Single(new RuntimeCommands.CreateCommand(
                new RuntimeIdentifier("pkg", "Module", "Template"),
                new DamlRecord(null, [])))
            .WithActAs(ActAs)
            .WithCommandId(TestCommandId);

    [Fact]
    public async Task TrySubmitAndWaitForTransactionAsync_resolves_a_retried_duplicate_through_the_injected_point_read()
    {
        _options.Retry = new RetryOptions { Enabled = true, MaxRetryAttempts = 2, Delay = TimeSpan.Zero };
        StubSubmitAndWaitForTransaction(
            Faulted<SubmitAndWaitForTransactionResponse>(Unavailable()),
            Faulted<SubmitAndWaitForTransactionResponse>(DuplicateCommand(completionOffset: 42L)));

        var resolved = TransactionResultProjector.Project(
            new SubmitAndWaitForTransactionResponse
            {
                Transaction = new Transaction { UpdateId = "u-original", Offset = 42L },
            });
        long? readOffset = null;
        var client = CreateClient((offset, _, _) =>
        {
            readOffset = offset;
            return Task.FromResult(resolved);
        });

        var outcome = await client.TrySubmitAndWaitForTransactionAsync(Create(), cancellationToken: TestContext.Current.CancellationToken);

        var success = outcome.Should().BeOfType<ExerciseOutcome<TransactionResult>.One>(
            "a duplicate on a retried attempt proves the first attempt committed").Subject;
        success.Result.UpdateId.Should().Be("u-original");
        readOffset.Should().Be(42L,
            "the completion_offset carried on the duplicate rejection is resolved via the injected point read");
    }

    [Fact]
    public async Task TrySubmitAndWaitForTransactionAsync_keeps_a_first_attempt_duplicate_without_touching_the_point_read()
    {
        _options.Retry = new RetryOptions { Enabled = true, MaxRetryAttempts = 2, Delay = TimeSpan.Zero };
        StubSubmitAndWaitForTransaction(Faulted<SubmitAndWaitForTransactionResponse>(DuplicateCommand(completionOffset: 42L)));
        var pointReadInvoked = false;
        var client = CreateClient((_, _, _) =>
        {
            pointReadInvoked = true;
            return Task.FromException<TransactionResult>(new InvalidOperationException("unexpected"));
        });

        var outcome = await client.TrySubmitAndWaitForTransactionAsync(Create(), cancellationToken: TestContext.Current.CancellationToken);

        outcome.Should().BeOfType<ExerciseOutcome<TransactionResult>.DamlError>()
            .Which.ErrorId.Should().Be("DUPLICATE_COMMAND");
        pointReadInvoked.Should().BeFalse("a first-attempt duplicate is a genuine caller error, not a lost success");
    }

    private void StubSubmitAndWaitForTransaction(params AsyncUnaryCall<SubmitAndWaitForTransactionResponse>[] calls) =>
        _commandService
            .SubmitAndWaitForTransactionAsync(
                Arg.Any<SubmitAndWaitForTransactionRequest>(),
                Arg.Any<Metadata>(),
                Arg.Any<DateTime?>(),
                Arg.Any<CancellationToken>())
            .Returns(calls[0], calls[1..]);

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
}
