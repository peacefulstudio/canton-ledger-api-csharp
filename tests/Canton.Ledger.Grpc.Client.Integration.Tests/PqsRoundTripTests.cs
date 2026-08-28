// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Canton.Ledger.Grpc.Client;
using Canton.Ledger.Pqs.Client;
using Canton.Ledger.Testing.Localnet;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Outcomes;
using Peaceful.Canton.Localnet.Testing;
using Richtypes;
using Xunit;

namespace Canton.Ledger.Grpc.Client.Integration.Tests;

[Trait("Category", "Integration")]
public class PqsRoundTripTests
{
    private const string GrpcUrlEnv = "CANTON_LOCALNET_A_VALIDATOR_1_GRPC_URL";
    private const string DefaultGrpcUrl = "http://localhost:11901";

    private const string PqsConnectionStringEnv = "CANTON_LOCALNET_A_VALIDATOR_1_PQS_CONNECTION_STRING";

    private const string LocalnetSkipMessage =
        "Skipping: set CANTON_LOCALNET_A_VALIDATOR_1_JSON_API_URL, _CLIENT_ID, _CLIENT_SECRET "
        + "(or the legacy un-namespaced CANTON_LOCALNET_* globals) and bring up the localnet "
        + "(canton-localnet up && canton-localnet wait-ready) to run this integration test.";

    private const string PqsSkipMessage =
        "Skipping: set " + PqsConnectionStringEnv + " to a PQS PostgreSQL connection string "
        + "(the LocalNet compose exposes the a-validator-1 store as "
        + "'Host=localhost;Port=5432;Database=pqs-a-validator-1;Username=cnadmin;Password=…') "
        + "to exercise the PQS read path. The integration lane sets this automatically.";

    // The LocalNet scribe JVMs run under tight PQS resource limits
    // (mem_limit 1g / -Xmx768m) and get OOM-killed under the test run's own
    // memory spike on the shared runner; the deadline must ride out a full
    // scribe restart + package re-registration cycle.
    private static readonly TimeSpan ProjectionTimeout = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);

    private static string DarPath() => Path.Combine(
        AppContext.BaseDirectory, "testdata", "richtypes", "richtypes.dar");

    private static LedgerClient NewLedgerClient(LocalnetFixture fixture, string userId)
    {
        var grpcAddress = Environment.GetEnvironmentVariable(GrpcUrlEnv) ?? DefaultGrpcUrl;
        var tokenProvider = new LocalnetTokenProvider(fixture.TokenProvider.GetAccessTokenAsync);
        return new LedgerClient(
            new LedgerClientOptions { GrpcAddress = grpcAddress, UserId = userId },
            tokenProvider);
    }

    private static async Task<Party> ScribeReadablePartyAsync(LocalnetFixture fixture, string userId)
    {
        var grpcAddress = Environment.GetEnvironmentVariable(GrpcUrlEnv) ?? DefaultGrpcUrl;
        var tokenProvider = new LocalnetTokenProvider(fixture.TokenProvider.GetAccessTokenAsync);
        using var admin = new AdminClient(
            new LedgerClientOptions { GrpcAddress = grpcAddress, UserId = userId },
            tokenProvider);
        var user = await admin.GetUserAsync(userId, TestContext.Current.CancellationToken);
        Assert.False(
            string.IsNullOrWhiteSpace(user?.PrimaryParty),
            $"user '{userId}' has no primary party — the LocalNet PQS scribe user only has ReadAs "
            + "for the validator operator party, so the contract under test must be issued by it "
            + "to ever be projected into PQS");
        return new Party(user!.PrimaryParty);
    }

    private const decimal AssetAmount = 123.45m;

    [Fact]
    public async Task QueryAsync_projects_and_maps_a_contract_created_via_LedgerClient()
    {
        var pqsConnectionString = RequirePqsConnectionString();

        await using var fixture = LocalnetFixture.FromEnvironment();
        var created = await CreateAssetAsync(fixture);

        var pqs = new PqsClient(new PqsClientOptions { ConnectionString = pqsConnectionString });

        var projected = await PollForProjectionAsync(
            () => pqs.QueryAsync<Asset>(TestContext.Current.CancellationToken),
            c => c.Id.Value == created.ContractId);

        Assert.NotNull(projected);
        Assert.Equal(created.Issuer.Id, projected!.Data.Issuer.Id);
        Assert.Equal(AssetAmount, projected.Data.Amount);
    }

    [Fact]
    public async Task QueryAsync_projects_the_interface_view_of_an_implementing_contract()
    {
        var pqsConnectionString = RequirePqsConnectionString();

        await using var fixture = LocalnetFixture.FromEnvironment();
        var created = await CreateAssetAsync(fixture);

        var pqs = new PqsClient(new PqsClientOptions { ConnectionString = pqsConnectionString });

        var projected = await PollForProjectionAsync(
            () => pqs.QueryAsync<IHolding, HoldingView>(TestContext.Current.CancellationToken),
            c => c.Id.Value == created.ContractId);

        Assert.NotNull(projected);
        Assert.Equal(AssetAmount, projected!.View.Amount);
    }

    private static string RequirePqsConnectionString()
    {
        if (!EndpointDiscovery.IsLocalnetAvailable())
        {
            Assert.Skip(LocalnetSkipMessage);
        }

        var pqsConnectionString = Environment.GetEnvironmentVariable(PqsConnectionStringEnv);
        if (string.IsNullOrWhiteSpace(pqsConnectionString))
        {
            Assert.Skip(PqsSkipMessage);
        }

        return pqsConnectionString!;
    }

    private static async Task<(Party Issuer, string ContractId)> CreateAssetAsync(LocalnetFixture fixture)
    {
        var darOutcome = await fixture.UploadDarAsync(DarPath(), TestContext.Current.CancellationToken);
        Assert.True(
            darOutcome is DarUploadOutcome.Uploaded or DarUploadOutcome.AlreadyKnown,
            $"Unexpected DAR upload outcome: {darOutcome}");

        var userId = fixture.ValidatorUserId;
        var issuer = await ScribeReadablePartyAsync(fixture, userId);

        using var ledger = NewLedgerClient(fixture, userId);

        var createOutcome = await ledger.CreateAsync(
            new Asset(issuer, AssetAmount), issuer, TestContext.Current.CancellationToken);
        var createdCid = Assert.IsType<ExerciseOutcome<ContractId<Asset>>.One>(createOutcome).Result;
        Assert.False(string.IsNullOrWhiteSpace(createdCid.Value), "created Asset ContractId is empty");

        return (issuer, createdCid.Value);
    }

    private static async Task<T?> PollForProjectionAsync<T>(
        Func<Task<IReadOnlyList<T>>> query,
        Func<T, bool> match)
        where T : class
    {
        var deadline = DateTimeOffset.UtcNow.Add(ProjectionTimeout);
        while (DateTimeOffset.UtcNow < deadline)
        {
            var rows = await query();
            var projected = rows.FirstOrDefault(match);
            if (projected is not null) return projected;

            try
            {
                await Task.Delay(PollInterval, TestContext.Current.CancellationToken);
            }
            catch (OperationCanceledException) when (TestContext.Current.CancellationToken.IsCancellationRequested)
            {
                break;
            }
        }

        return null;
    }
}
