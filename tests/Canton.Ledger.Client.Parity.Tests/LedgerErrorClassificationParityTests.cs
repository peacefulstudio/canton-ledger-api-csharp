// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Net;
using System.Text;
using AwesomeAssertions;
using Canton.Ledger.Abstractions;
using Canton.Ledger.Grpc.Client;
using Canton.Ledger.Kernel.Streams;
using Canton.Ledger.Rest.Client;
using Daml.Runtime.Outcomes;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Google.Rpc;
using Xunit;
using GrpcCallStatus = Grpc.Core.Status;
using GrpcStatus = Google.Rpc.Status;
using Metadata = Grpc.Core.Metadata;
using RpcException = Grpc.Core.RpcException;
using StatusCode = Grpc.Core.StatusCode;

namespace Canton.Ledger.Client.Parity.Tests;

/// <summary>
/// Pins that a failure the participant reports over gRPC and the same failure reported over REST
/// reach a caller as one <see cref="DamlErrorCategory"/>. Each row of <see cref="Failures"/>
/// carries both wire encodings of a single participant failure and one expected category, and the
/// body runs <c>DamlErrorParser.Parse</c> and <c>RestErrorParser.ParseAsync</c> over them together —
/// so a row cannot exist for one transport and not the other, and a divergence in either parser's
/// reader fails here rather than only in that transport's own unit suite. Both parsers are
/// <c>internal</c> and stay that way; this project reaches them through
/// <c>InternalsVisibleTo</c> from each client, so a divergence is pinned at the parser that caused
/// it rather than only through whichever client surface happens to carry the category onwards.
/// <see cref="LedgerErrorCategorySurfaceParityTests"/> pins the other half — that what a parser
/// recovers here is still readable by a consumer holding <see cref="ICantonLedgerClient"/>.
/// </summary>
public class LedgerErrorClassificationParityTests
{
    public static TheoryData<string, DamlErrorCategory> WireCategories() => new()
    {
        { "8", DamlErrorCategory.InvalidIndependentOfSystemState },
        { "11", DamlErrorCategory.InvalidGivenCurrentSystemStateResourceMissing },
        { "ContentionOnSharedResources", DamlErrorCategory.ContentionOnSharedResources },
        { "50", DamlErrorCategory.Unknown },
        { "-1", DamlErrorCategory.Unknown },
        { "TotallyMadeUpCategory", DamlErrorCategory.Unknown },
        { "TransientServerFailure,ContentionOnSharedResources", DamlErrorCategory.Unknown },
    };

    [Theory]
    [MemberData(nameof(WireCategories))]
    public void MapCategory_classifies_a_wire_category_the_same_way_for_every_transport(
        string wireCategory, DamlErrorCategory expected)
    {
        ParsedLedgerError.MapCategory(wireCategory).Should().Be(expected);
    }

    public static TheoryData<string> FailureNames() => [.. Failures.Keys];

    [Theory]
    [MemberData(nameof(FailureNames))]
    public async Task Parse_classifies_the_same_participant_failure_identically_on_both_transports(
        string failureName)
    {
        var failure = Failures[failureName];
        using var restResponse = failure.OverRest();

        var overGrpc = DamlErrorParser.Parse(failure.OverGrpc());
        var overRest = await RestErrorParser.ParseAsync(restResponse, TestContext.Current.CancellationToken);

        overRest.GetType().Should().Be(
            overGrpc.GetType(),
            "one participant failure has to land on the same arm of ParsedLedgerError on both transports");
        overGrpc.ClassifiedCategory.Should().Be(failure.ClassifiedCategory);
        overRest.ClassifiedCategory.Should().Be(failure.ClassifiedCategory);
    }

    [Theory]
    [MemberData(nameof(FailureNames))]
    public async Task A_terminal_StreamError_carries_the_same_category_on_both_transports(string failureName)
    {
        var failure = Failures[failureName];
        var rpcException = failure.OverGrpc();
        using var restResponse = failure.OverRest();

        var overGrpc = FaultOverGrpc(rpcException);
        var overRest = FaultOverRest(
            await RestErrorParser.ParseAsync(restResponse, TestContext.Current.CancellationToken));

        overGrpc.Category.Should().Be(failure.ClassifiedCategory);
        overRest.Category.Should().Be(failure.ClassifiedCategory);
    }

    [Theory]
    [MemberData(nameof(FailureNames))]
    public async Task A_terminal_StreamError_carries_the_same_error_id_on_both_transports(string failureName)
    {
        var failure = Failures[failureName];
        var rpcException = failure.OverGrpc();
        using var restResponse = failure.OverRest();

        var overGrpc = FaultOverGrpc(rpcException);
        var overRest = FaultOverRest(
            await RestErrorParser.ParseAsync(restResponse, TestContext.Current.CancellationToken));

        overGrpc.ErrorId.Should().Be(failure.ErrorId);
        overRest.ErrorId.Should().Be(failure.ErrorId);
    }

    private static StreamFault FaultOverGrpc(RpcException rpcException)
    {
        var parsed = DamlErrorParser.Parse(rpcException);

        return StreamFault.FromTransport(
            (int)rpcException.StatusCode,
            rpcException.Message,
            parsed.ClassifiedCategory,
            parsed.ReportedErrorId,
            rpcException);
    }

    private static StreamFault FaultOverRest(ParsedLedgerError parsed) =>
        StreamFault.FromTransport(
            parsed.StatusCode,
            parsed.Message,
            parsed.ClassifiedCategory,
            parsed.ReportedErrorId,
            sourceException: null);

    private sealed record TransportFailure(
        Func<RpcException> OverGrpc,
        Func<HttpResponseMessage> OverRest,
        DamlErrorCategory Category,
        string? ErrorId = null)
    {
        internal DamlErrorCategory? ClassifiedCategory =>
            Category is DamlErrorCategory.Unknown ? null : Category;
    }

    private const string RedactedCorrelationId = "0123456789abcdef0123456789abcdef";

    private const string RedactedMessage = "A security-sensitive error has been received";

    private static readonly IReadOnlyDictionary<string, TransportFailure> Failures =
        new Dictionary<string, TransportFailure>(StringComparer.Ordinal)
        {
            ["a typed failure carrying a recognised wire category"] = new(
                () => TypedFailure(StatusCode.NotFound, "CONTRACT_NOT_FOUND", "not found", "11"),
                () => JsonResponse(
                    HttpStatusCode.NotFound,
                    """
                    {
                      "code": 5,
                      "message": "not found",
                      "details": [
                        {
                          "@type": "type.googleapis.com/google.rpc.ErrorInfo",
                          "reason": "CONTRACT_NOT_FOUND",
                          "metadata": {"category": "11"}
                        }
                      ]
                    }
                    """),
                DamlErrorCategory.InvalidGivenCurrentSystemStateResourceMissing,
                "CONTRACT_NOT_FOUND"),

            ["a typed failure carrying an unrecognised wire category"] = new(
                () => TypedFailure(StatusCode.AlreadyExists, "SOMETHING_NEW", "boom", "50"),
                () => JsonResponse(
                    HttpStatusCode.Conflict,
                    """
                    {
                      "code": 6,
                      "message": "boom",
                      "details": [
                        {
                          "@type": "type.googleapis.com/google.rpc.ErrorInfo",
                          "reason": "SOMETHING_NEW",
                          "metadata": {"category": "50"}
                        }
                      ]
                    }
                    """),
                DamlErrorCategory.Unknown,
                "SOMETHING_NEW"),

            ["a failure whose status carries no error detail"] = new(
                () => DetaillessFailure(StatusCode.Internal, "internal error"),
                () => JsonResponse(
                    HttpStatusCode.InternalServerError,
                    """{"code": 13, "message": "internal error", "details": []}"""),
                DamlErrorCategory.Unknown),

            ["a failure whose payload cannot be decoded"] = new(
                () => new RpcException(new GrpcCallStatus(StatusCode.Unavailable, "unavailable")),
                () => JsonResponse(HttpStatusCode.ServiceUnavailable, "not json at all"),
                DamlErrorCategory.Unknown),

            ["a redacted authentication failure"] = new(
                () => RedactedFailure(StatusCode.Unauthenticated),
                () => JsonResponse(HttpStatusCode.Unauthorized, RedactedBody(grpcCodeValue: 16)),
                DamlErrorCategory.AuthInterceptorInvalidAuthenticationCredentials),

            ["a redacted authorization failure"] = new(
                () => RedactedFailure(StatusCode.PermissionDenied),
                () => JsonResponse(HttpStatusCode.Forbidden, RedactedBody(grpcCodeValue: 7)),
                DamlErrorCategory.AuthorizationChecksFailed),
        };

    private static string RedactedBody(int grpcCodeValue) =>
        $$"""
        {
          "code": "NA",
          "cause": "{{RedactedMessage}}",
          "correlationId": "{{RedactedCorrelationId}}",
          "traceId": "{{RedactedCorrelationId}}",
          "context": {},
          "resources": [],
          "errorCategory": -1,
          "grpcCodeValue": {{grpcCodeValue}},
          "retryInfo": null,
          "definiteAnswer": null
        }
        """;

    private static HttpResponseMessage JsonResponse(HttpStatusCode statusCode, string body) =>
        new(statusCode) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static RpcException TypedFailure(
        StatusCode statusCode, string errorId, string message, string wireCategory)
    {
        var errorInfo = new ErrorInfo { Reason = errorId, Domain = "ledger.api" };
        errorInfo.Metadata.Add("category", wireCategory);

        var status = new GrpcStatus { Code = (int)statusCode, Message = message };
        status.Details.Add(Any.Pack(errorInfo));

        return WithTrailers(statusCode, message, status);
    }

    private static RpcException DetaillessFailure(StatusCode statusCode, string message) =>
        WithTrailers(statusCode, message, new GrpcStatus { Code = (int)statusCode, Message = message });

    private static RpcException RedactedFailure(StatusCode statusCode)
    {
        var status = new GrpcStatus { Code = (int)statusCode, Message = RedactedMessage };
        status.Details.Add(Any.Pack(new RequestInfo { RequestId = RedactedCorrelationId }));

        return WithTrailers(statusCode, RedactedMessage, status);
    }

    private static RpcException WithTrailers(StatusCode statusCode, string message, GrpcStatus status) =>
        new(
            new GrpcCallStatus(statusCode, message),
            new Metadata { { "grpc-status-details-bin", status.ToByteArray() } });
}
