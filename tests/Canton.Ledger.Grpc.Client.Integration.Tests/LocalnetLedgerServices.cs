// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Canton.Ledger.Abstractions;
using Canton.Ledger.Testing.Localnet;
using Microsoft.Extensions.DependencyInjection;
using Peaceful.Canton.Localnet.Testing;

namespace Canton.Ledger.Grpc.Client.Integration.Tests;

/// <summary>
/// Builds the container every LocalNet lane resolves its clients from, and owns the participant
/// address they all reach. Disposing the returned provider disposes the clients it handed out.
/// </summary>
internal static class LocalnetLedgerServices
{
    private const string GrpcUrlEnv = "CANTON_LOCALNET_A_VALIDATOR_1_GRPC_URL";
    private const string DefaultGrpcUrl = "http://localhost:11901";

    internal static string GrpcAddress =>
        Environment.GetEnvironmentVariable(GrpcUrlEnv) ?? DefaultGrpcUrl;

    internal static ServiceProvider ForValidator(LocalnetFixture fixture, string userId)
    {
        var grpcAddress = GrpcAddress;

        void Configure(LedgerClientOptions options)
        {
            options.GrpcAddress = grpcAddress;
            options.UserId = userId;
        }

        return new ServiceCollection()
            .AddSingleton<ITokenProvider>(new LocalnetTokenProvider(fixture.TokenProvider.GetAccessTokenAsync))
            .AddLedgerClient(Configure)
            .AddAdminClient(Configure)
            .BuildServiceProvider();
    }

    /// <summary>
    /// Builds the same container against a caller-supplied token source, so a lane can reach the
    /// participant with credentials it chose — a token the participant will refuse, for one.
    /// </summary>
    internal static ServiceProvider ForTokenProvider(ITokenProvider tokenProvider, string userId)
    {
        var grpcAddress = GrpcAddress;

        return new ServiceCollection()
            .AddSingleton(tokenProvider)
            .AddLedgerClient(options =>
            {
                options.GrpcAddress = grpcAddress;
                options.UserId = userId;
            })
            .BuildServiceProvider();
    }
}
