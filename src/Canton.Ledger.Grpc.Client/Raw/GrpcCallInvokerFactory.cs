// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Canton.Ledger.Abstractions;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Canton.Ledger.Grpc.Client.Raw;

internal sealed class GrpcCallInvokerFactory : IGrpcCallInvokerFactory, IDisposable
{
    private readonly GrpcChannel _channel;
    private readonly LedgerCallInvoker _invoker;
    private readonly ILogger<GrpcCallInvokerFactory> _logger;
    private bool _disposed;

    internal GrpcCallInvokerFactory(
        IOptions<LedgerClientOptions> options,
        ITokenProvider tokenProvider,
        ILogger<GrpcCallInvokerFactory>? logger = null)
        : this(options.Value, tokenProvider, logger)
    {
    }

    internal GrpcCallInvokerFactory(
        LedgerClientOptions options,
        ITokenProvider tokenProvider,
        ILogger<GrpcCallInvokerFactory>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(tokenProvider);

        _logger = logger ?? NullLogger<GrpcCallInvokerFactory>.Instance;
        _channel = LedgerGrpcChannel.Create(options);
        _invoker = new LedgerCallInvoker(options, tokenProvider);

        CallContextHelper.LogStartupDiagnostics(
            _logger, tokenProvider, options.GrpcAddress, nameof(GrpcCallInvokerFactory), "AddLedgerRawGrpc");
    }

    public CallInvoker CreateCallInvoker()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return new AuthenticatedCallInvoker(_channel.CreateCallInvoker(), _invoker, _logger);
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        _channel.Dispose();
    }
}
