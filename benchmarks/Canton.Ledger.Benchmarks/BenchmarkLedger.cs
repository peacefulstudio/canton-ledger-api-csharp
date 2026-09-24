// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Canton.Ledger.Abstractions;
using Canton.Ledger.Grpc.Client;
using Canton.Ledger.Grpc.Client.Integration.Tests;
using Canton.Ledger.Pqs.Client;
using Canton.Ledger.Rest.Client;
using Canton.Ledger.Testing.Localnet;
using Daml.Runtime.Data;
using Microsoft.Extensions.DependencyInjection;
using Peaceful.Canton.Localnet.Testing;

namespace Canton.Ledger.Benchmarks;

internal sealed class BenchmarkLedger : IAsyncDisposable
{
    private const string GrpcUrlEnv = "CANTON_LOCALNET_A_VALIDATOR_1_GRPC_URL";
    private const string DefaultGrpcUrl = "http://localhost:11901";
    private const string PqsConnectionStringEnv = "CANTON_LOCALNET_A_VALIDATOR_1_PQS_CONNECTION_STRING";

    private readonly LocalnetFixture _fixture;
    private readonly ActAsRightsLease _actAsRights;
    private readonly List<ServiceProvider> _services = [];

    private BenchmarkLedger(LocalnetFixture fixture, ActAsRightsLease actAsRights)
    {
        _fixture = fixture;
        _actAsRights = actAsRights;
    }

    public Party Owner { get; private set; }

    public Party SnapshotOwner { get; private set; }

    public Party Operator { get; private set; }

    public Transport Grpc { get; private set; } = null!;

    public Transport Rest { get; private set; } = null!;

    public IReadOnlyList<Transport> RestIdleWindowVariants { get; private set; } = [];

    public IPqsClient? Pqs { get; private set; }

    public IReadOnlyList<Transport> Transports => [Grpc, Rest];

    public static async Task<BenchmarkLedger> OpenAsync(BenchmarkOptions options, CancellationToken cancellationToken)
    {
        if (!EndpointDiscovery.IsLocalnetAvailable())
        {
            throw new InvalidOperationException(
                "no LocalNet found: set CANTON_LOCALNET_A_VALIDATOR_1_JSON_API_URL, _CLIENT_ID and _CLIENT_SECRET "
                + "and bring it up with `canton-localnet up && canton-localnet wait-ready`");
        }

        var fixture = LocalnetFixture.FromEnvironment();
        var ledger = new BenchmarkLedger(fixture, ActAsRightsLease.ForValidator(fixture));
        try
        {
            await ledger.ConnectAsync(options, cancellationToken).ConfigureAwait(false);
            return ledger;
        }
        catch
        {
            await ledger.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private async Task ConnectAsync(BenchmarkOptions options, CancellationToken cancellationToken)
    {
        await _fixture.UploadDarAsync(RichTypesDar.Path, cancellationToken).ConfigureAwait(false);
        var party = await _fixture.AllocatePartyAsync("benchmark", cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        await _actAsRights.GrantAsync(party.PartyId, cancellationToken).ConfigureAwait(false);
        Owner = new Party(party.PartyId);
        var snapshotParty = await _fixture.AllocatePartyAsync("benchmark-snapshot", cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        await _actAsRights.GrantAsync(snapshotParty.PartyId, cancellationToken).ConfigureAwait(false);
        SnapshotOwner = new Party(snapshotParty.PartyId);

        var tokenProvider = new LocalnetTokenProvider(_fixture.TokenProvider.GetAccessTokenAsync);
        var userId = _fixture.ValidatorUserId;
        var grpcAddress = Environment.GetEnvironmentVariable(GrpcUrlEnv) ?? DefaultGrpcUrl;
        var restAddress = _fixture.Endpoints.JsonLedgerApi.ToString();

        void ConfigureGrpc(LedgerClientOptions options)
        {
            options.GrpcAddress = grpcAddress;
            options.UserId = userId;
        }

        var grpcServices = Track(new ServiceCollection()
            .AddSingleton<ITokenProvider>(tokenProvider)
            .AddLedgerClient(ConfigureGrpc)
            .AddAdminClient(ConfigureGrpc)
            .BuildServiceProvider());
        Grpc = new Transport("gRPC", grpcServices.GetRequiredService<ICantonLedgerClient>());

        Transport RestTransport(string name, TimeSpan idleWindow)
        {
            var restServices = Track(new ServiceCollection()
                .AddSingleton<ITokenProvider>(tokenProvider)
                .AddRestLedgerClient(restOptions =>
                {
                    restOptions.HttpAddress = restAddress;
                    restOptions.StreamWindowIdleTimeout = idleWindow;
                })
                .BuildServiceProvider());
            return new Transport(name, restServices.GetRequiredService<ICantonLedgerClient>());
        }

        Rest = RestTransport("REST", RestLedgerClientOptions.DefaultStreamWindowIdleTimeout);
        RestIdleWindowVariants = options.RestIdleWindowsMs
            .Select(idleMs => RestTransport($"REST, {idleMs} ms idle window", TimeSpan.FromMilliseconds(idleMs)))
            .ToArray();

        var validatorUser = await grpcServices.GetRequiredService<IAdminClient>()
            .GetUserAsync(userId, cancellationToken).ConfigureAwait(false);
        Operator = new Party(validatorUser?.PrimaryParty
            ?? throw new InvalidOperationException($"validator user '{userId}' has no primary party"));

        var pqsConnectionString = Environment.GetEnvironmentVariable(PqsConnectionStringEnv);
        if (!string.IsNullOrWhiteSpace(pqsConnectionString))
        {
            var pqsServices = Track(new ServiceCollection()
                .AddPqsClient(options => options.ConnectionString = pqsConnectionString)
                .BuildServiceProvider());
            Pqs = pqsServices.GetRequiredService<IPqsClient>();
        }
    }

    public async Task<string> LedgerApiVersionAsync(CancellationToken cancellationToken) =>
        await Grpc.Client.GetLedgerApiVersionAsync(cancellationToken: cancellationToken).ConfigureAwait(false);

    private ServiceProvider Track(ServiceProvider services)
    {
        _services.Add(services);
        return services;
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            foreach (var services in _services)
            {
                await services.DisposeAsync().ConfigureAwait(false);
            }
        }
        finally
        {
            try
            {
                await _actAsRights.DisposeAsync().ConfigureAwait(false);
            }
            finally
            {
                await _fixture.DisposeAsync().ConfigureAwait(false);
            }
        }
    }
}
