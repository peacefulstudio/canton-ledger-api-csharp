// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics;
using Canton.Ledger.Kernel.Telemetry;
using Google.Protobuf.Reflection;
using Grpc.Core;
using RuntimeCommands = Daml.Runtime.Commands;

namespace Canton.Ledger.Grpc.Client;

internal static class ActivityHelper
{
    internal const string RpcSystem = "rpc.system";
    internal const string RpcService = "rpc.service";
    internal const string RpcMethod = "rpc.method";
    internal const string ServerAddress = SemanticConventions.ServerAddress;
    internal const string ServerPort = SemanticConventions.ServerPort;
    internal const string RpcGrpcStatusCode = "rpc.grpc.status_code";
    internal const string ErrorType = SemanticConventions.ErrorType;

    internal const int DefaultHttpsPort = 443;
    internal const int DefaultHttpPort = 80;

    internal static (string Address, int Port) ParseServerEndpoint(string grpcAddress)
    {
        if (!Uri.TryCreate(grpcAddress, UriKind.Absolute, out var uri) || !IsHttpScheme(uri.Scheme))
        {
            throw new ArgumentException(
                $"gRPC endpoint must be an absolute 'http'/'https' URL (e.g. \"https://localhost:5001\"), but was \"{grpcAddress}\".",
                nameof(grpcAddress));
        }

        if (string.IsNullOrEmpty(uri.Host))
        {
            throw new ArgumentException(
                $"gRPC endpoint must have a non-empty host (e.g. \"https://localhost:5001\"), but was \"{grpcAddress}\".",
                nameof(grpcAddress));
        }

        var port = uri.Port < 0 ? DefaultPortForScheme(uri.Scheme) : uri.Port;
        return (uri.Host, port);
    }

    private static bool IsHttpScheme(string scheme) =>
        scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
        || scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);

    private static int DefaultPortForScheme(string scheme) =>
        scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            ? DefaultHttpsPort
            : DefaultHttpPort;

    public static void SetGrpcCallTags(
        this Activity? activity,
        ServiceDescriptor service,
        string method,
        string serverAddress,
        int serverPort)
    {
        if (activity is null) return;

        activity.SetTag(RpcSystem, "grpc");
        activity.SetTag(RpcService, service.FullName);
        activity.SetTag(RpcMethod, method);
        activity.SetTag(ServerAddress, serverAddress);
        activity.SetTag(ServerPort, serverPort);
    }

    internal static void SetSubmitterTags(this Activity? activity, RuntimeCommands.SubmitterInfo submitter)
    {
        if (activity is null) return;

        activity.SetTag(LedgerClientActivityTags.CantonSubmitterActAs, string.Join(",", submitter.ActAs.Select(p => p.Id)));
        if (submitter.ReadAs.Count > 0)
        {
            activity.SetTag(LedgerClientActivityTags.CantonSubmitterReadAs, string.Join(",", submitter.ReadAs.Select(p => p.Id)));
        }
    }

    public static void RecordGrpcError(this Activity? activity, RpcException exception)
    {
        if (activity is null) return;

        activity.SetStatus(ActivityStatusCode.Error, exception.Status.Detail);
        activity.SetTag(RpcGrpcStatusCode, (int)exception.StatusCode);
        activity.SetTag(ErrorType, exception.StatusCode.ToString());
    }

    public static void RecordDamlError(this Activity? activity, string errorId)
    {
        if (activity is null) return;

        activity.SetStatus(ActivityStatusCode.Error, errorId);
        activity.SetTag(ErrorType, errorId);
    }

    public static void RecordInfraError(this Activity? activity, int statusCode, string message)
    {
        if (activity is null) return;

        activity.SetStatus(ActivityStatusCode.Error, message);
        activity.SetTag(RpcGrpcStatusCode, statusCode);
        activity.SetTag(ErrorType, StatusCodeName(statusCode));
    }

    private static string StatusCodeName(int statusCode) =>
        Enum.IsDefined(typeof(StatusCode), statusCode)
            ? ((StatusCode)statusCode).ToString()
            : statusCode.ToString(System.Globalization.CultureInfo.InvariantCulture);
}
