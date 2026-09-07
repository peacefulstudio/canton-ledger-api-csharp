// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Net;
using System.Text;
using AwesomeAssertions;
using Canton.Ledger.Abstractions;
using Daml.Ledger.Abstractions;
using Daml.Runtime;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Outcomes;
using Microsoft.Extensions.Options;
using Xunit;
using RuntimeCommands = Daml.Runtime.Commands;
using RuntimeIdentifier = Daml.Runtime.Data.Identifier;

namespace Canton.Ledger.Rest.Client.Tests;

public sealed class RestCallEnvelopeTests : IDisposable
{
    private static readonly Party Alice = new("party::alice");
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan ReadDeadline = TimeSpan.FromMilliseconds(50);

    private readonly List<StubHttpClientFactory> _factories = [];

    private static readonly EnvelopedCall[] EnvelopedCalls =
    [
        new(
            nameof(RestLedgerClient.SubmitAndWaitAsync),
            "Server returned a successful response but no body was present for submit-and-wait.",
            "Server returned a malformed submit-and-wait response body: "),
        new(
            nameof(RestLedgerClient.TrySubmitAndWaitForTransactionAsync),
            "Server returned a successful response but no transaction was present.",
            "Server returned a malformed transaction: "),
        new(
            nameof(RestLedgerClient.TrySubmitAndWaitForReassignmentAsync),
            "Server returned a successful response but no reassignment was present.",
            "Server returned a malformed reassignment response body: "),
        new(
            nameof(RestLedgerClient.EstimateTrafficCostAsync),
            "Server returned a successful response but no prepared submission was present for the "
            + "traffic-cost estimate.",
            "Server returned a malformed traffic-cost estimate response body: "),
    ];

    public static TheoryData<string, HttpStatusCode, int, DamlErrorCategory> RedactedAuthFailures()
    {
        TheoryData<string, HttpStatusCode, int, DamlErrorCategory> rows = [];
        foreach (var call in EnvelopedCalls)
        {
            rows.Add(
                call.Operation,
                HttpStatusCode.Unauthorized,
                16,
                DamlErrorCategory.AuthInterceptorInvalidAuthenticationCredentials);
            rows.Add(
                call.Operation,
                HttpStatusCode.Forbidden,
                7,
                DamlErrorCategory.AuthorizationChecksFailed);
        }

        return rows;
    }

    public static TheoryData<string> EnvelopedOperations()
    {
        TheoryData<string> operations = [];
        foreach (var call in EnvelopedCalls)
        {
            operations.Add(call.Operation);
        }

        return operations;
    }

    public static TheoryData<string, string> MissingBodyMessages()
    {
        TheoryData<string, string> rows = [];
        foreach (var call in EnvelopedCalls)
        {
            rows.Add(call.Operation, call.MissingBodyMessage);
        }

        return rows;
    }

    public static TheoryData<string, string> MalformedBodyPrefixes()
    {
        TheoryData<string, string> rows = [];
        foreach (var call in EnvelopedCalls)
        {
            rows.Add(call.Operation, call.MalformedBodyPrefix);
        }

        return rows;
    }

    public void Dispose()
    {
        foreach (var factory in _factories)
        {
            factory.Dispose();
        }
    }

    [Theory]
    [MemberData(nameof(EnvelopedOperations))]
    public async Task Enveloped_operation_reports_a_deadline_overrun_as_a_request_timeout(string operation)
    {
        var failure = await InvokeAsync(ClientWith(TimedOutTransport()), operation);

        failure.StatusCode.Should().Be((int)HttpStatusCode.RequestTimeout);
        failure.Message.Should().Be($"Request exceeded the {Deadline} deadline.");
    }

    [Theory]
    [MemberData(nameof(EnvelopedOperations))]
    public async Task Enveloped_operation_reports_a_transport_failure_as_service_unavailable(string operation)
    {
        var transport = new RecordingHttpHandler()
            .WithTransportException(new HttpRequestException("connection refused"));

        var failure = await InvokeAsync(ClientWith(transport), operation);

        failure.StatusCode.Should().Be((int)HttpStatusCode.ServiceUnavailable);
        failure.Message.Should().Be("connection refused");
    }

    [Theory]
    [MemberData(nameof(EnvelopedOperations))]
    public async Task Enveloped_operation_reports_a_non_success_response_with_the_participants_status(string operation)
    {
        var transport = new RecordingHttpHandler().WithResponse(
            HttpStatusCode.ServiceUnavailable,
            """{"code": 14, "message": "participant unavailable"}""");

        var failure = await InvokeAsync(ClientWith(transport), operation);

        failure.StatusCode.Should().Be((int)HttpStatusCode.ServiceUnavailable);
        failure.Message.Should().Be("participant unavailable");
    }

    [Theory]
    [MemberData(nameof(RedactedAuthFailures))]
    public async Task Enveloped_operation_carries_the_recovered_Category_when_the_participant_redacted_the_error_info(
        string operation, HttpStatusCode statusCode, int grpcCodeValue, DamlErrorCategory expected)
    {
        var transport = new RecordingHttpHandler().WithResponse(
            statusCode,
            $$"""{"code": {{grpcCodeValue}}, "message": "a security-sensitive error has been received"}""");

        var failure = await InvokeAsync(ClientWith(transport), operation);

        failure.Category.Should().Be(
            expected,
            "a thrown failure and a reported one must carry the recovered classification identically");
    }

    [Theory]
    [MemberData(nameof(EnvelopedOperations))]
    public async Task Enveloped_operation_leaves_Category_null_on_an_unclassified_failure(string operation)
    {
        var transport = new RecordingHttpHandler().WithResponse(
            HttpStatusCode.ServiceUnavailable,
            """{"code": 14, "message": "participant unavailable"}""");

        var failure = await InvokeAsync(ClientWith(transport), operation);

        failure.Category.Should().BeNull();
    }

    [Theory]
    [MemberData(nameof(MalformedBodyPrefixes))]
    public async Task Enveloped_operation_reports_an_undecodable_success_body_as_malformed(
        string operation, string expectedPrefix)
    {
        var transport = new RecordingHttpHandler().WithResponse(HttpStatusCode.OK, "{ not json");

        var failure = await InvokeAsync(ClientWith(transport), operation);

        failure.StatusCode.Should().Be(
            (int)HttpStatusCode.InternalServerError,
            "a thrown failure and a reported one must classify an undecodable body identically");
        failure.Message.Should().StartWith(expectedPrefix);
    }

    [Theory]
    [MemberData(nameof(MissingBodyMessages))]
    public async Task Enveloped_operation_reports_an_absent_success_body_as_a_missing_payload(
        string operation, string expectedMessage)
    {
        var transport = new RecordingHttpHandler().WithResponse(HttpStatusCode.OK, "null");

        var failure = await InvokeAsync(ClientWith(transport), operation);

        failure.StatusCode.Should().Be(
            (int)HttpStatusCode.InternalServerError,
            "a thrown failure and a reported one must classify an absent body identically");
        failure.Message.Should().Be(expectedMessage);
    }

    [Fact]
    public async Task Enveloped_operation_distinguishes_a_deadline_that_overran_while_reading_the_body()
    {
        var client = ClientWith(new DeadlineElapsesBeforeTheBodyIsReadHandler());

        var act = () => client.SubmitAndWaitAsync(
            Submission(), ReadDeadline, TestContext.Current.CancellationToken);

        var thrown = await act.Should().ThrowAsync<LedgerOperationException>();
        thrown.Which.StatusCode.Should().Be((int)HttpStatusCode.RequestTimeout);
        thrown.Which.Message.Should().Be(
            $"Request exceeded the {ReadDeadline} deadline while reading the response body.");
    }

    private static RecordingHttpHandler TimedOutTransport() =>
        new RecordingHttpHandler().WithTransportException(
            new TaskCanceledException("timed out", new TimeoutException()));

    private static async Task<CallFailure> InvokeAsync(RestLedgerClient client, string operation)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        return operation switch
        {
            nameof(RestLedgerClient.SubmitAndWaitAsync) => await ThrownAsync(
                () => client.SubmitAndWaitAsync(Submission(), Deadline, cancellationToken)),
            nameof(RestLedgerClient.TrySubmitAndWaitForTransactionAsync) => Reported(
                await client.TrySubmitAndWaitForTransactionAsync(Submission(), Deadline, cancellationToken)),
            nameof(RestLedgerClient.TrySubmitAndWaitForReassignmentAsync) => Reported(
                await client.TrySubmitAndWaitForReassignmentAsync<TestTemplate>(
                    Reassignment(), Deadline, cancellationToken)),
            nameof(RestLedgerClient.EstimateTrafficCostAsync) => await ThrownAsync(
                () => client.EstimateTrafficCostAsync(Submission(), Deadline, cancellationToken)),
            _ => throw new InvalidOperationException($"The theory names an operation it cannot invoke: {operation}"),
        };
    }

    private static async Task<CallFailure> ThrownAsync<TResult>(Func<Task<TResult>> operation)
    {
        var thrown = await operation.Should().ThrowAsync<LedgerOperationException>();
        return new CallFailure(thrown.Which.StatusCode, thrown.Which.Message, thrown.Which.Category);
    }

    private static CallFailure Reported<TResult>(ExerciseOutcome<TResult> outcome)
    {
        var infraError = outcome.Should().BeOfType<ExerciseOutcome<TResult>.InfraError>().Subject;
        return new CallFailure(infraError.StatusCode, infraError.Message, infraError.Category);
    }

    private static RuntimeCommands.CommandsSubmission Submission() =>
        RuntimeCommands.CommandsSubmission
            .Single(RuntimeCommands.CreateCommand.For(new TestTemplate()))
            .WithActAs(Alice);

    private static ReassignmentSubmission Reassignment() =>
        ReassignmentSubmission.Of(
            new UnassignCommand("00cid", new SynchronizerId("sync-a"), new SynchronizerId("sync-b")), Alice);

    private RestLedgerClient ClientWith(HttpMessageHandler transport)
    {
        var factory = new StubHttpClientFactory(transport);
        _factories.Add(factory);
        return new RestLedgerClient(
            factory, Options.Create(new RestLedgerClientOptions { HttpAddress = "http://localhost:7575" }));
    }

    private readonly record struct CallFailure(int? StatusCode, string Message, DamlErrorCategory? Category);

    private sealed record EnvelopedCall(
        string Operation, string MissingBodyMessage, string MalformedBodyPrefix);

    private sealed class DeadlineElapsesBeforeTheBodyIsReadHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = new StringContent("{}", Encoding.UTF8, "application/json");
            await body.LoadIntoBufferAsync(CancellationToken.None).ConfigureAwait(false);

            var deadlineElapsed = new TaskCompletionSource();
            await using var _ = cancellationToken
                .Register(() => deadlineElapsed.TrySetResult())
                .ConfigureAwait(false);
            await deadlineElapsed.Task.ConfigureAwait(false);

            return new HttpResponseMessage(HttpStatusCode.OK) { Content = body };
        }
    }

    private sealed record TestTemplate : ITemplate, IDamlRecord<TestTemplate>
    {
        public static RuntimeIdentifier TemplateId { get; } = new("pkg", "Module", "Template");
        public static string PackageId => "pkg";
        public static string PackageName => "pkg-name";
        public static Version PackageVersion { get; } = new(0, 1, 0);
        public static DamlTypeDescriptor DamlTypeId { get; } = new(TemplateId, DamlTypeKind.Template, PackageName);
        public DamlRecord ToRecord() => new(TemplateId, [new DamlField("owner", Alice.ToDamlValue())]);

        public static TestTemplate FromRecord(DamlRecord record) =>
            new();
    }
}
