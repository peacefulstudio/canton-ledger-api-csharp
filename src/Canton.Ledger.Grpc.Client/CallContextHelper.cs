// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Canton.Ledger.Abstractions;
using Canton.Ledger.Kernel.Authentication;
using Grpc.Core;
using Microsoft.Extensions.Logging;

namespace Canton.Ledger.Grpc.Client;

internal static partial class CallContextHelper
{
    internal static void LogStartupDiagnostics(
        ILogger logger,
        ITokenProvider? tokenProvider,
        string grpcAddress,
        string clientName,
        string registrationMethod)
    {
        LogInitialized(logger, clientName, grpcAddress);

        if (ReferenceEquals(tokenProvider, ITokenProvider.None))
            LogUnauthenticatedMode(logger, clientName, registrationMethod);

        if (SendsCredentialsOverPlaintextHttp(tokenProvider, grpcAddress))
            LogInsecureCredentialTransport(logger, clientName, grpcAddress);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "{ClientName} initialized with endpoint {Endpoint}")]
    private static partial void LogInitialized(ILogger logger, string clientName, string endpoint);

    [LoggerMessage(Level = LogLevel.Warning, Message = "{ClientName} running in unauthenticated mode. If this is unintentional, register an ITokenProvider or use the {RegistrationMethod} overload that accepts authConfiguration.")]
    private static partial void LogUnauthenticatedMode(ILogger logger, string clientName, string registrationMethod);

    [LoggerMessage(Level = LogLevel.Warning, Message = "{ClientName} will send bearer tokens over plaintext http to {Endpoint}, where anyone on the network path can read and replay them. Use an https GrpcAddress for anything beyond local development.")]
    private static partial void LogInsecureCredentialTransport(ILogger logger, string clientName, string endpoint);

    internal static bool SendsCredentialsOverPlaintextHttp(ITokenProvider? tokenProvider, string grpcAddress) =>
        tokenProvider is not null
        && !ReferenceEquals(tokenProvider, ITokenProvider.None)
        && Uri.TryCreate(grpcAddress, UriKind.Absolute, out var address)
        && address.Scheme == Uri.UriSchemeHttp;

    internal static async Task<Metadata?> GetHeadersAsync(ITokenProvider? tokenProvider, CancellationToken cancellationToken)
    {
        var token = await tokenProvider.ResolveBearerTokenAsync(cancellationToken).ConfigureAwait(false);
        if (token is null)
            return null;

        return new Metadata
        {
            { "authorization", $"Bearer {token}" }
        };
    }

    internal static DateTime? GetDeadline(TimeSpan? timeout) =>
        timeout is null ? null : DateTime.UtcNow.Add(timeout.Value);
}
