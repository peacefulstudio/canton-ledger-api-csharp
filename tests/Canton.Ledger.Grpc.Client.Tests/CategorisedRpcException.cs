// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Google.Rpc;
using GrpcCallStatus = Grpc.Core.Status;
using GrpcStatus = Google.Rpc.Status;
using Metadata = Grpc.Core.Metadata;
using RpcException = Grpc.Core.RpcException;
using StatusCode = Grpc.Core.StatusCode;

namespace Canton.Ledger.Grpc.Client.Tests;

internal static class CategorisedRpcException
{
    internal static RpcException WithCategory(StatusCode statusCode, string errorId, string message, string wireCategory)
    {
        var errorInfo = new ErrorInfo { Reason = errorId, Domain = "ledger.api" };
        errorInfo.Metadata.Add("category", wireCategory);

        var status = new GrpcStatus { Code = (int)statusCode, Message = message };
        status.Details.Add(Any.Pack(errorInfo));

        return new RpcException(
            new GrpcCallStatus(statusCode, message),
            new Metadata { { "grpc-status-details-bin", status.ToByteArray() } });
    }
}
