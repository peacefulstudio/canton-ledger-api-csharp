// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Canton.Ledger.Abstractions;
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

namespace Canton.Ledger.Grpc.Client.Tests;

public class DamlErrorParserTests
{
    [Theory]
    [InlineData("TransientServerFailure", DamlErrorCategory.TransientServerFailure)]
    [InlineData("ContentionOnSharedResources", DamlErrorCategory.ContentionOnSharedResources)]
    [InlineData("DeadlineExceededRequestStateUnknown", DamlErrorCategory.DeadlineExceededRequestStateUnknown)]
    [InlineData("SystemInternalAssumptionViolated", DamlErrorCategory.SystemInternalAssumptionViolated)]
    [InlineData("MaliciousOrFaultyBehaviour", DamlErrorCategory.MaliciousOrFaultyBehaviour)]
    [InlineData("AuthInterceptorInvalidAuthenticationCredentials", DamlErrorCategory.AuthInterceptorInvalidAuthenticationCredentials)]
    [InlineData("AuthorizationChecksFailed", DamlErrorCategory.AuthorizationChecksFailed)]
    [InlineData("InvalidIndependentOfSystemState", DamlErrorCategory.InvalidIndependentOfSystemState)]
    [InlineData("InvalidGivenCurrentSystemStateOther", DamlErrorCategory.InvalidGivenCurrentSystemStateOther)]
    [InlineData("InvalidGivenCurrentSystemStateResourceExists", DamlErrorCategory.InvalidGivenCurrentSystemStateResourceExists)]
    [InlineData("InvalidGivenCurrentSystemStateResourceMissing", DamlErrorCategory.InvalidGivenCurrentSystemStateResourceMissing)]
    [InlineData("InvalidGivenCurrentSystemStateSeekDifferentResource", DamlErrorCategory.InvalidGivenCurrentSystemStateSeekDifferentResource)]
    [InlineData("BackgroundProcessDegradationWarning", DamlErrorCategory.BackgroundProcessDegradationWarning)]
    [InlineData("InternalUnsupportedOperation", DamlErrorCategory.InternalUnsupportedOperation)]
    public void Parse_maps_known_category_to_enum(string raw, DamlErrorCategory expected)
    {
        var ex = MakeRpcException(
            statusCode: StatusCode.FailedPrecondition,
            errorId: "SOMETHING",
            statusMessage: "boom",
            metadata: new Dictionary<string, string> { ["category"] = raw });

        var (category, _, _, _, _) = DamlErrorParser.Parse(ex);

        category.Should().Be(expected);
    }

    [Theory]
    [InlineData("1", DamlErrorCategory.TransientServerFailure)]
    [InlineData("2", DamlErrorCategory.ContentionOnSharedResources)]
    [InlineData("3", DamlErrorCategory.DeadlineExceededRequestStateUnknown)]
    [InlineData("4", DamlErrorCategory.SystemInternalAssumptionViolated)]
    [InlineData("5", DamlErrorCategory.MaliciousOrFaultyBehaviour)]
    [InlineData("6", DamlErrorCategory.AuthInterceptorInvalidAuthenticationCredentials)]
    [InlineData("7", DamlErrorCategory.AuthorizationChecksFailed)]
    [InlineData("8", DamlErrorCategory.InvalidIndependentOfSystemState)]
    [InlineData("9", DamlErrorCategory.InvalidGivenCurrentSystemStateOther)]
    [InlineData("10", DamlErrorCategory.InvalidGivenCurrentSystemStateResourceExists)]
    [InlineData("11", DamlErrorCategory.InvalidGivenCurrentSystemStateResourceMissing)]
    [InlineData("12", DamlErrorCategory.InvalidGivenCurrentSystemStateSeekDifferentResource)]
    [InlineData("13", DamlErrorCategory.BackgroundProcessDegradationWarning)]
    [InlineData("14", DamlErrorCategory.InternalUnsupportedOperation)]
    public void Parse_maps_documented_numeric_category_ids_to_enum(string wireCategoryId, DamlErrorCategory expected)
    {
        var ex = MakeRpcException(
            statusCode: StatusCode.FailedPrecondition,
            errorId: "SOMETHING",
            statusMessage: "boom",
            metadata: new Dictionary<string, string> { ["category"] = wireCategoryId });

        var (category, _, _, _, _) = DamlErrorParser.Parse(ex);

        category.Should().Be(expected);
    }

    [Theory]
    [InlineData("transientserverfailure", DamlErrorCategory.TransientServerFailure)]
    [InlineData("CONTENTIONONSHAREDRESOURCES", DamlErrorCategory.ContentionOnSharedResources)]
    public void Parse_falls_back_to_case_insensitive_category_match(string raw, DamlErrorCategory expected)
    {
        var ex = MakeRpcException(
            statusCode: StatusCode.FailedPrecondition,
            errorId: "X",
            statusMessage: "x",
            metadata: new Dictionary<string, string> { ["category"] = raw });

        var (category, _, _, _, _) = DamlErrorParser.Parse(ex);

        category.Should().Be(expected);
    }

    [Fact]
    public void Parse_returns_unknown_when_category_value_is_unrecognised()
    {
        var ex = MakeRpcException(
            statusCode: StatusCode.FailedPrecondition,
            errorId: "OPAQUE",
            statusMessage: "x",
            metadata: new Dictionary<string, string> { ["category"] = "TotallyMadeUpCategory" });

        var (category, errorId, _, _, _) = DamlErrorParser.Parse(ex);

        category.Should().Be(DamlErrorCategory.Unknown);
        errorId.Should().Be("OPAQUE");
    }

    [Fact]
    public void Parse_returns_unknown_when_category_metadata_is_missing()
    {
        var ex = MakeRpcException(
            statusCode: StatusCode.FailedPrecondition,
            errorId: "NO_CATEGORY",
            statusMessage: "x",
            metadata: new Dictionary<string, string>());

        var (category, errorId, _, _, _) = DamlErrorParser.Parse(ex);

        category.Should().Be(DamlErrorCategory.Unknown);
        errorId.Should().Be("NO_CATEGORY");
    }

    [Fact]
    public void Parse_populates_error_id_from_error_info_reason()
    {
        var ex = MakeRpcException(
            statusCode: StatusCode.NotFound,
            errorId: "CONTRACT_NOT_FOUND",
            statusMessage: "not found",
            metadata: new Dictionary<string, string> { ["category"] = "InvalidGivenCurrentSystemStateResourceMissing" });

        var (_, errorId, message, _, _) = DamlErrorParser.Parse(ex);

        errorId.Should().Be("CONTRACT_NOT_FOUND");
        message.Should().Be("not found");
    }

    [Fact]
    public void Parse_passes_through_metadata_entries()
    {
        var ex = MakeRpcException(
            statusCode: StatusCode.FailedPrecondition,
            errorId: "SAMPLE_ALREADY_EXECUTED",
            statusMessage: "already executed",
            metadata: new Dictionary<string, string>
            {
                ["category"] = "InvalidGivenCurrentSystemStateOther",
                ["resource_id"] = "00abc",
                ["sequence"] = "42",
            });

        var (_, _, _, metadata, _) = DamlErrorParser.Parse(ex);

        metadata.Should().ContainKey("category");
        metadata.Should().Contain(new KeyValuePair<string, string>("resource_id", "00abc"));
        metadata.Should().Contain(new KeyValuePair<string, string>("sequence", "42"));
    }

    [Fact]
    public void Parse_falls_back_to_unknown_when_trailers_are_missing()
    {
        // No trailers → no rich error model available.
        var ex = new RpcException(new GrpcCallStatus(StatusCode.Unavailable, "service down"));

        var (category, errorId, message, metadata, _) = DamlErrorParser.Parse(ex);

        category.Should().Be(DamlErrorCategory.Unknown);
        errorId.Should().BeEmpty();
        message.Should().Be("service down");
        metadata.Should().BeEmpty();
    }

    [Fact]
    public void Parse_falls_back_to_unknown_when_trailer_payload_is_unparseable()
    {
        var trailers = new Metadata
        {
            { "grpc-status-details-bin", new byte[] { 0xff, 0xfe, 0xfd, 0xfc } },
        };
        var ex = new RpcException(new GrpcCallStatus(StatusCode.Internal, "garbled"), trailers);

        var (category, errorId, message, metadata, _) = DamlErrorParser.Parse(ex);

        category.Should().Be(DamlErrorCategory.Unknown);
        errorId.Should().BeEmpty();
        message.Should().Be("garbled");
        metadata.Should().BeEmpty();
    }

    [Fact]
    public void Parse_falls_back_to_unknown_when_status_has_no_error_info()
    {
        // Status is present but carries no ErrorInfo detail.
        var status = new GrpcStatus { Code = (int)StatusCode.Unknown, Message = "no details here" };
        var trailers = new Metadata
        {
            { "grpc-status-details-bin", status.ToByteArray() },
        };
        var ex = new RpcException(new GrpcCallStatus(StatusCode.Unknown, "no details here"), trailers);

        var (category, errorId, message, metadata, _) = DamlErrorParser.Parse(ex);

        category.Should().Be(DamlErrorCategory.Unknown);
        errorId.Should().BeEmpty();
        message.Should().Be("no details here");
        metadata.Should().BeEmpty();
    }

    [Theory]
    [InlineData("8", DamlErrorCategory.InvalidIndependentOfSystemState)]
    [InlineData("11", DamlErrorCategory.InvalidGivenCurrentSystemStateResourceMissing)]
    [InlineData("ContentionOnSharedResources", DamlErrorCategory.ContentionOnSharedResources)]
    [InlineData("50", DamlErrorCategory.Unknown)]
    [InlineData("-1", DamlErrorCategory.Unknown)]
    [InlineData("TotallyMadeUpCategory", DamlErrorCategory.Unknown)]
    [InlineData("TransientServerFailure,ContentionOnSharedResources", DamlErrorCategory.Unknown)]
    public void Parse_classifies_the_wire_category_identically_to_the_REST_transport(
        string wireCategory, DamlErrorCategory expected)
    {
        var ex = MakeRpcException(
            statusCode: StatusCode.FailedPrecondition,
            errorId: "SOMETHING",
            statusMessage: "boom",
            metadata: new Dictionary<string, string> { ["category"] = wireCategory });

        DamlErrorParser.Parse(ex).Category.Should().Be(expected);
    }

    [Fact]
    public void Parse_carries_the_gRPC_status_code_as_the_transport_status_code()
    {
        var ex = MakeRpcException(
            statusCode: StatusCode.NotFound,
            errorId: "CONTRACT_NOT_FOUND",
            statusMessage: "not found",
            metadata: new Dictionary<string, string> { ["category"] = "11" });

        DamlErrorParser.Parse(ex).StatusCode.Should().Be((int)StatusCode.NotFound);
    }

    [Theory]
    [InlineData(StatusCode.Unauthenticated, DamlErrorCategory.AuthInterceptorInvalidAuthenticationCredentials)]
    [InlineData(StatusCode.PermissionDenied, DamlErrorCategory.AuthorizationChecksFailed)]
    public void Parse_classifies_a_redacted_security_failure_from_the_transport_status_code(
        StatusCode statusCode, DamlErrorCategory expected)
    {
        var ex = MakeRedactedRpcException(statusCode);

        var (category, errorId, message, metadata, transportStatusCode) = DamlErrorParser.Parse(ex);

        category.Should().Be(expected);
        errorId.Should().BeEmpty();
        message.Should().Be(RedactedMessage);
        metadata.Should().BeEmpty();
        transportStatusCode.Should().Be((int)statusCode);
    }

    [Theory]
    [InlineData(StatusCode.Unknown)]
    [InlineData(StatusCode.NotFound)]
    [InlineData(StatusCode.InvalidArgument)]
    [InlineData(StatusCode.Unavailable)]
    [InlineData(StatusCode.Internal)]
    public void Parse_leaves_every_other_status_code_unknown_when_the_status_carries_no_ErrorInfo(
        StatusCode statusCode)
    {
        var ex = MakeRedactedRpcException(statusCode);

        DamlErrorParser.Parse(ex).Category.Should().Be(DamlErrorCategory.Unknown);
    }

    private const string RedactedCorrelationId = "0123456789abcdef0123456789abcdef";

    private const string RedactedMessage =
        "An error occurred. Please contact the operator and inquire about the request "
        + RedactedCorrelationId + " with tid " + RedactedCorrelationId;

    private static RpcException MakeRedactedRpcException(StatusCode statusCode)
    {
        var status = new GrpcStatus
        {
            Code = (int)statusCode,
            Message = RedactedMessage,
        };
        status.Details.Add(Any.Pack(new RequestInfo { RequestId = RedactedCorrelationId }));

        var trailers = new Metadata
        {
            { "grpc-status-details-bin", status.ToByteArray() },
        };

        return new RpcException(new GrpcCallStatus(statusCode, RedactedMessage), trailers);
    }

    private static RpcException MakeRpcException(
        StatusCode statusCode,
        string errorId,
        string statusMessage,
        IReadOnlyDictionary<string, string> metadata)
    {
        var errorInfo = new ErrorInfo
        {
            Reason = errorId,
            Domain = "ledger.api",
        };
        foreach (var kvp in metadata)
        {
            errorInfo.Metadata.Add(kvp.Key, kvp.Value);
        }

        var status = new GrpcStatus
        {
            Code = (int)statusCode,
            Message = statusMessage,
        };
        status.Details.Add(Any.Pack(errorInfo));

        var trailers = new Metadata
        {
            { "grpc-status-details-bin", status.ToByteArray() },
        };

        return new RpcException(new GrpcCallStatus(statusCode, statusMessage), trailers);
    }
}
