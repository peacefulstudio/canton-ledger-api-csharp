// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using AwesomeAssertions;
using Canton.Ledger.Abstractions;
using Canton.Ledger.Grpc.Client;
using Canton.Ledger.Rest.Client;
using Daml.Ledger.Abstractions;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Outcomes;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Google.Rpc;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using GrpcStatus = Google.Rpc.Status;
using RestClientRegistration = Canton.Ledger.Rest.Client.ServiceCollectionExtensions;
using RuntimeCommands = Daml.Runtime.Commands;
using RuntimeIdentifier = Daml.Runtime.Data.Identifier;
using StatusCode = Grpc.Core.StatusCode;

namespace Canton.Ledger.Client.Parity.Tests;

/// <summary>
/// The consumer-facing half of <see cref="LedgerErrorClassificationParityTests"/>: a category the
/// parsers recover from a participant failure has to survive all the way to whoever holds an
/// <see cref="ICantonLedgerClient"/>, on both transports. Each row drives the neutral interface
/// resolved from a container, against a transport whose responses are supplied by a fault-injecting
/// <see cref="HttpMessageHandler"/> — the gRPC one through
/// <see cref="LedgerClientOptions.ConfigureChannel"/>, the REST one through the named
/// <see cref="HttpClient"/> the registration documents — so the row exercises the same delivery path
/// a deployed consumer does, without a participant and without naming an implementation type.
/// </summary>
/// <remarks>
/// The delivery path is what these rows pin, not the classification: a client that recovers the
/// category and then drops it on the way out reads exactly like one that never recovered it, and
/// only a row that reads the category back off <see cref="ExerciseOutcome{T}.InfraError"/> or
/// <see cref="LedgerOperationException"/> can tell the two apart.
/// </remarks>
public sealed class LedgerErrorCategorySurfaceParityTests
{
    private const string Grpc = "gRPC";
    private const string Rest = "REST";

    private const string ParticipantAddress = "http://127.0.0.1:1";
    private const string ParityUser = "parity-user";
    private const string OperationName = "SubmitAndWaitForTransaction";

    private const string RedactedCorrelationId = "0123456789abcdef0123456789abcdef";
    private const string RedactedMessage = "A security-sensitive error has been received";
    private const string UnclassifiableMessage = "participant unavailable";

    private static readonly Party Alice = new("party::alice");

    private static readonly RuntimeCommands.SubmitterInfo Submitter =
        new(new HashSet<Party> { Alice }, new HashSet<Party>());

    private static readonly IReadOnlyDictionary<string, ParticipantFailure> Failures =
        new Dictionary<string, ParticipantFailure>(StringComparer.Ordinal)
        {
            ["a redacted authentication failure"] = new(
                StatusCode.Unauthenticated,
                HttpStatusCode.Unauthorized,
                RedactedMessage,
                DamlErrorCategory.AuthInterceptorInvalidAuthenticationCredentials),

            ["a redacted authorization failure"] = new(
                StatusCode.PermissionDenied,
                HttpStatusCode.Forbidden,
                RedactedMessage,
                DamlErrorCategory.AuthorizationChecksFailed),

            ["a failure no evidence classifies"] = new(
                StatusCode.Unavailable,
                HttpStatusCode.ServiceUnavailable,
                UnclassifiableMessage,
                Category: null),
        };

    public static TheoryData<string, string> TransportsAndFailures()
    {
        TheoryData<string, string> rows = [];
        foreach (var transport in new[] { Grpc, Rest })
        {
            foreach (var failureName in Failures.Keys)
            {
                rows.Add(transport, failureName);
            }
        }

        return rows;
    }

    [Theory]
    [MemberData(nameof(TransportsAndFailures))]
    public async Task TrySubmitAndWaitForTransactionAsync_hands_a_consumer_the_recovered_Category_on_every_transport(
        string transport, string failureName)
    {
        var failure = Failures[failureName];
        await using var lane = Open(transport, failure);

        var outcome = await lane.Capability.TrySubmitAndWaitForTransactionAsync(
            Submission(), Submitter, cancellationToken: TestContext.Current.CancellationToken);

        var infra = outcome.Should().BeOfType<ExerciseOutcome<TransactionResult>.InfraError>().Subject;
        infra.Category.Should().Be(
            failure.Category,
            "a consumer holding ICantonLedgerClient over {0} must be able to read the classification "
            + "the transport recovered, not just the status code",
            transport);
    }

    [Theory]
    [MemberData(nameof(TransportsAndFailures))]
    public async Task OneOrThrowAsync_carries_the_recovered_Category_onto_LedgerOperationException_on_every_transport(
        string transport, string failureName)
    {
        var failure = Failures[failureName];
        await using var lane = Open(transport, failure);

        var act = async () => await lane.Capability
            .TrySubmitAndWaitForTransactionAsync(
                Submission(), Submitter, cancellationToken: TestContext.Current.CancellationToken)
            .OneOrThrowAsync(OperationName);

        var thrown = await act.Should().ThrowAsync<LedgerOperationException>();
        thrown.Which.Category.Should().Be(
            failure.Category,
            "the throwing surface over {0} must carry the same classification the outcome surface does",
            transport);
    }

    private static RuntimeCommands.CommandsSubmission Submission() =>
        RuntimeCommands.CommandsSubmission
            .Single(new RuntimeCommands.CreateCommand(
                new RuntimeIdentifier("test-pkg", "Sample.Foo", "FooBar"),
                new DamlRecord(null, [])))
            .WithCommandId(new RuntimeCommands.CommandId("parity-category-cmd"));

    private static CapabilityLane<ICantonLedgerClient> Open(string transport, ParticipantFailure failure)
    {
        var services = Register(new ServiceCollection(), transport, failure).BuildServiceProvider();
        return new CapabilityLane<ICantonLedgerClient>(
            services.GetRequiredService<ICantonLedgerClient>(), services.DisposeAsync);
    }

    private static IServiceCollection Register(
        IServiceCollection services, string transport, ParticipantFailure failure) => transport switch
    {
        Grpc => services.AddLedgerClient(options =>
        {
            options.GrpcAddress = ParticipantAddress;
            options.UserId = ParityUser;
            options.ConfigureChannel = channel =>
                channel.HttpHandler = new FaultingTransport(failure.OverGrpc);
        }),
        Rest => RegisterRest(services, failure),
        _ => throw new ArgumentOutOfRangeException(nameof(transport), transport, "Unknown transport."),
    };

    private static IServiceCollection RegisterRest(IServiceCollection services, ParticipantFailure failure)
    {
        services.AddRestLedgerClient(options => options.HttpAddress = ParticipantAddress);
        services.AddHttpClient(RestClientRegistration.HttpClientName)
            .ConfigurePrimaryHttpMessageHandler(() => new FaultingTransport(failure.OverRest));
        return services;
    }

    private sealed record ParticipantFailure(
        StatusCode GrpcStatusCode,
        HttpStatusCode HttpStatusCode,
        string Message,
        DamlErrorCategory? Category)
    {
        public HttpResponseMessage OverGrpc()
        {
            var response = new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Version = HttpVersion.Version20,
                Content = new ByteArrayContent([]),
            };
            response.Content.Headers.ContentType = new MediaTypeHeaderValue("application/grpc");
            response.Headers.TryAddWithoutValidation(
                "grpc-status", ((int)GrpcStatusCode).ToString(CultureInfo.InvariantCulture));
            response.Headers.TryAddWithoutValidation("grpc-message", Message);

            if (Category is not null)
            {
                response.Headers.TryAddWithoutValidation(
                    "grpc-status-details-bin", Convert.ToBase64String(RedactedStatus().ToByteArray()));
            }

            return response;
        }

        public HttpResponseMessage OverRest() =>
            Category is not null
                ? new(HttpStatusCode)
                {
                    Content = new StringContent(
                        $$"""{"code": "NA", "cause": "{{Message}}", "correlationId": "{{RedactedCorrelationId}}", "traceId": "{{RedactedCorrelationId}}", "context": {}, "resources": [], "errorCategory": -1, "grpcCodeValue": {{(int)GrpcStatusCode}}, "retryInfo": null, "definiteAnswer": null}""",
                        Encoding.UTF8,
                        "application/json"),
                }
                : new(HttpStatusCode)
                {
                    Content = new StringContent(
                        $$"""{"code": {{(int)GrpcStatusCode}}, "message": "{{Message}}"}""",
                        Encoding.UTF8,
                        "application/json"),
                };

        private GrpcStatus RedactedStatus()
        {
            var status = new GrpcStatus { Code = (int)GrpcStatusCode, Message = Message };
            status.Details.Add(Any.Pack(new RequestInfo { RequestId = RedactedCorrelationId }));
            return status;
        }
    }

    private sealed class FaultingTransport(Func<HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(respond());
    }
}
