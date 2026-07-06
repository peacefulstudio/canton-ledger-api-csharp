// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics;
using Google.Protobuf.Reflection;
using Grpc.Core;

namespace Canton.Ledger.Grpc.Client;

internal static class ActivityHelper
{
    internal const string RpcSystem = "rpc.system";
    internal const string RpcService = "rpc.service";
    internal const string RpcMethod = "rpc.method";
    internal const string ServerAddress = "server.address";
    internal const string ServerPort = "server.port";
    internal const string RpcGrpcStatusCode = "rpc.grpc.status_code";
    internal const string ErrorType = "error.type";

    internal const int DefaultHttpsPort = 443;
    internal const int DefaultHttpPort = 80;

    internal static (string Address, int Port) ParseServerEndpoint(string grpcAddress)
    {
        var uri = new Uri(grpcAddress, UriKind.Absolute);
        var port = uri.Port < 0 ? DefaultPortForScheme(uri.Scheme) : uri.Port;
        return (uri.Host, port);
    }

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

    public static void RecordException(this Activity? activity, Exception exception)
    {
        if (activity is null) return;

        activity.SetStatus(ActivityStatusCode.Error, exception.Message);
        activity.SetTag(ErrorType, exception.GetType().FullName);
    }
}
