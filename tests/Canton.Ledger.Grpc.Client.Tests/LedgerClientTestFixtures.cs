// Copyright (c) 2026 Peaceful Studio OÜ. All rights reserved.

using Com.Daml.Ledger.Api.V2;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Google.Rpc;
using Grpc.Core;
using NSubstitute;
using GrpcStatus = Google.Rpc.Status;
using Status = Grpc.Core.Status;

namespace Canton.Ledger.Grpc.Client.Tests;

internal static class LedgerClientTestFixtures
{
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

    internal static RpcException MakeDamlRpcException(string errorId, string message, string category)
    {
        var info = new ErrorInfo { Reason = errorId, Domain = "ledger.api" };
        info.Metadata.Add("category", category);
        var status = new GrpcStatus { Code = (int)StatusCode.FailedPrecondition, Message = message };
        status.Details.Add(Any.Pack(info));
        var trailers = new Metadata { { "grpc-status-details-bin", status.ToByteArray() } };
        return new RpcException(new Status(StatusCode.FailedPrecondition, message), trailers);
    }
}
