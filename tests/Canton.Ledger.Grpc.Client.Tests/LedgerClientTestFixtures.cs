// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Com.Daml.Ledger.Api.V2;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Google.Rpc;
using Grpc.Core;
using NSubstitute;
using GrpcStatus = Google.Rpc.Status;
using ProtoValue = Com.Daml.Ledger.Api.V2.Value;
using RuntimeIdentifier = Daml.Runtime.Data.Identifier;
using Status = Grpc.Core.Status;

namespace Canton.Ledger.Grpc.Client.Tests;

internal static class LedgerClientTestFixtures
{
    internal const int LedgerNumericMaxDigits = 38;

    internal static ProtoValue OutOfDecimalRangeNumeric() =>
        new() { Numeric = new string('9', LedgerNumericMaxDigits + 1) };

    internal const string OwnerParty = "alice::ns1";

    internal static Com.Daml.Ledger.Api.V2.Record OwnerArguments() => new()
    {
        Fields =
        {
            new RecordField { Label = "owner", Value = new ProtoValue { Party = OwnerParty } },
        },
    };

    internal static Com.Daml.Ledger.Api.V2.Record OwnerArgumentsWith(string label, ProtoValue value)
    {
        var arguments = OwnerArguments();
        arguments.Fields.Add(new RecordField { Label = label, Value = value });
        return arguments;
    }

    internal static void StubCommandServiceSuccess(
        CommandService.CommandServiceClient commandService,
        SubmitAndWaitForTransactionResponse response,
        Action<SubmitAndWaitForTransactionRequest>? onRequest = null)
    {
        commandService
            .SubmitAndWaitForTransactionAsync(
                Arg.Do<SubmitAndWaitForTransactionRequest>(request => onRequest?.Invoke(request)),
                Arg.Any<Metadata>(),
                Arg.Any<DateTime?>(),
                Arg.Any<CancellationToken>())
            .Returns(new AsyncUnaryCall<SubmitAndWaitForTransactionResponse>(
                Task.FromResult(response),
                Task.FromResult(new Metadata()),
                () => Status.DefaultSuccess,
                () => new Metadata(),
                () => { }));
    }

    internal static void StubCommandServiceFailure(
        CommandService.CommandServiceClient commandService,
        RpcException exception)
    {
        commandService
            .SubmitAndWaitForTransactionAsync(
                Arg.Any<SubmitAndWaitForTransactionRequest>(),
                Arg.Any<Metadata>(),
                Arg.Any<DateTime?>(),
                Arg.Any<CancellationToken>())
            .Returns(new AsyncUnaryCall<SubmitAndWaitForTransactionResponse>(
                Task.FromException<SubmitAndWaitForTransactionResponse>(exception),
                Task.FromResult(new Metadata()),
                () => exception.Status,
                () => exception.Trailers ?? new Metadata(),
                () => { }));
    }

    internal const string RedactedMessage =
        "An error occurred. Please contact the operator and inquire about the request";

    internal static RpcException MakeRedactedRpcException(StatusCode statusCode)
    {
        var status = new GrpcStatus { Code = (int)statusCode, Message = RedactedMessage };
        status.Details.Add(Any.Pack(new RequestInfo { RequestId = "0123456789abcdef0123456789abcdef" }));
        var trailers = new Metadata { { "grpc-status-details-bin", status.ToByteArray() } };
        return new RpcException(new Status(statusCode, RedactedMessage), trailers);
    }

    internal static RpcException MakeDamlRpcException(
        string errorId,
        string message,
        string category,
        StatusCode statusCode = StatusCode.FailedPrecondition,
        IReadOnlyDictionary<string, string>? extraMetadata = null)
    {
        var info = new ErrorInfo { Reason = errorId, Domain = "ledger.api" };
        info.Metadata.Add("category", category);
        foreach (var entry in extraMetadata ?? new Dictionary<string, string>())
        {
            info.Metadata.Add(entry.Key, entry.Value);
        }
        var status = new GrpcStatus { Code = (int)statusCode, Message = message };
        status.Details.Add(Any.Pack(info));
        var trailers = new Metadata { { "grpc-status-details-bin", status.ToByteArray() } };
        return new RpcException(new Status(statusCode, message), trailers);
    }
}

internal sealed record FooBar(string Owner) : ITemplate, IDamlRecord<FooBar>
{
    public static RuntimeIdentifier TemplateId { get; } = new("test-pkg", "Sample.Foo", "FooBar");
    public static string PackageId => "test-pkg";
    public static string PackageName => "test-package";
    public static Version PackageVersion { get; } = new(0, 1, 0);
    public static DamlTypeDescriptor DamlTypeId { get; } = new(TemplateId, DamlTypeKind.Template, PackageName);

    public DamlRecord ToRecord() => DamlRecord.Create(
        DamlField.Create("owner", new DamlParty(Owner)));

    public static FooBar FromRecord(DamlRecord record) =>
        new(record.GetRequiredField("owner").As<DamlParty>().Value);
}
