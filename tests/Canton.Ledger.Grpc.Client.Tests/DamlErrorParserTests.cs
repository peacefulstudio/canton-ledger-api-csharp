// Copyright (c) 2026 Peaceful Studio OÜ. All rights reserved.

using Daml.Runtime.Contracts;
using Daml.Runtime.Outcomes;
using FluentAssertions;
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

        var (category, _, _, _) = DamlErrorParser.Parse(ex);

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

        var (category, _, _, _) = DamlErrorParser.Parse(ex);

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

        var (category, errorId, _, _) = DamlErrorParser.Parse(ex);

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

        var (category, errorId, _, _) = DamlErrorParser.Parse(ex);

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

        var (_, errorId, message, _) = DamlErrorParser.Parse(ex);

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

        var (_, _, _, metadata) = DamlErrorParser.Parse(ex);

        metadata.Should().ContainKey("category");
        metadata.Should().Contain(new KeyValuePair<string, string>("resource_id", "00abc"));
        metadata.Should().Contain(new KeyValuePair<string, string>("sequence", "42"));
    }

    [Fact]
    public void Parse_falls_back_to_unknown_when_trailers_are_missing()
    {
        // No trailers → no rich error model available.
        var ex = new RpcException(new GrpcCallStatus(StatusCode.Unavailable, "service down"));

        var (category, errorId, message, metadata) = DamlErrorParser.Parse(ex);

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

        var (category, errorId, message, metadata) = DamlErrorParser.Parse(ex);

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

        var (category, errorId, message, metadata) = DamlErrorParser.Parse(ex);

        category.Should().Be(DamlErrorCategory.Unknown);
        errorId.Should().BeEmpty();
        message.Should().Be("no details here");
        metadata.Should().BeEmpty();
    }

    [Fact]
    public void MapCategory_returns_unknown_for_null_or_whitespace()
    {
        DamlErrorParser.MapCategory(null).Should().Be(DamlErrorCategory.Unknown);
        DamlErrorParser.MapCategory("").Should().Be(DamlErrorCategory.Unknown);
        DamlErrorParser.MapCategory("   ").Should().Be(DamlErrorCategory.Unknown);
    }

    [Fact]
    public void ToDamlError_builds_outcome()
    {
        var ex = MakeRpcException(
            statusCode: StatusCode.FailedPrecondition,
            errorId: "INCONSISTENT",
            statusMessage: "stale",
            metadata: new Dictionary<string, string> { ["category"] = "ContentionOnSharedResources" });

        var outcome = DamlErrorParser.ToDamlError<TransactionResult>(ex);

        outcome.Category.Should().Be(DamlErrorCategory.ContentionOnSharedResources);
        outcome.ErrorId.Should().Be("INCONSISTENT");
        outcome.Message.Should().Be("stale");
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
