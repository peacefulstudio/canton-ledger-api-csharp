// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Net.Http;
using Grpc.Net.Client;

namespace Canton.Ledger.Grpc.Client;

internal static class LedgerGrpcChannel
{
    public static GrpcChannel Create(LedgerClientOptions options) =>
        GrpcChannel.ForAddress(options.GrpcAddress, BuildOptions(options));

    internal static GrpcChannelOptions BuildOptions(LedgerClientOptions options)
    {
        var channelOptions = new GrpcChannelOptions
        {
            MaxReceiveMessageSize = options.MaxMessageSize,
            MaxSendMessageSize = options.MaxMessageSize,
            HttpHandler = new SocketsHttpHandler
            {
                KeepAlivePingDelay = options.KeepAlivePingDelay,
                KeepAlivePingTimeout = options.KeepAlivePingTimeout,
            },
            DisposeHttpClient = true,
        };

        options.ConfigureChannel?.Invoke(channelOptions);
        return channelOptions;
    }
}
