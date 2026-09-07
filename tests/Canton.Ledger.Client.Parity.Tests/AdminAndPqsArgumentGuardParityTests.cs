// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Canton.Ledger.Abstractions;
using Canton.Ledger.Grpc.Client;
using Canton.Ledger.Pqs.Client;
using Canton.Ledger.Testing;
using Daml.Runtime.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Richtypes;
using Xunit;

namespace Canton.Ledger.Client.Parity.Tests;

/// <summary>
/// The <see cref="LedgerClientArgumentGuardParityTests"/> contract carried onto the two narrower
/// neutral surfaces: a <see langword="null"/> reference argument is refused with an
/// <see cref="ArgumentNullException"/> naming the parameter, synchronously, whether the caller
/// holds the real client or the fake. The fakes are the reason these rows exist — a fake that
/// accepts an argument the real client refuses lets a test pass over code that would fail against a
/// participant.
/// </summary>
public sealed class AdminAndPqsArgumentGuardParityTests
{
    private const string Real = "real";
    private const string Fake = "fake";

    public static TheoryData<string> AdminClients() => new() { Real, Fake };

    public static TheoryData<string> PqsClients() => new() { Real, Fake };

    [Theory]
    [MemberData(nameof(AdminClients))]
    public void AllocatePartyAsync_rejects_a_null_partyIdHint_synchronously(string flavour) =>
        AssertAdminRejectsNull(flavour, "partyIdHint", client => client.AllocatePartyAsync(null!));

    [Theory]
    [MemberData(nameof(AdminClients))]
    public void GetPartiesAsync_rejects_a_null_partyIds_synchronously(string flavour) =>
        AssertAdminRejectsNull(flavour, "partyIds", client => client.GetPartiesAsync(null!));

    [Theory]
    [MemberData(nameof(AdminClients))]
    public void CreateUserAsync_rejects_a_null_userId_synchronously(string flavour) =>
        AssertAdminRejectsNull(flavour, "userId", client => client.CreateUserAsync(null!, "party::alice"));

    [Theory]
    [MemberData(nameof(AdminClients))]
    public void CreateUserAsync_rejects_a_null_primaryParty_synchronously(string flavour) =>
        AssertAdminRejectsNull(flavour, "primaryParty", client => client.CreateUserAsync("alice", null!));

    [Theory]
    [MemberData(nameof(AdminClients))]
    public void GetUserAsync_rejects_a_null_userId_synchronously(string flavour) =>
        AssertAdminRejectsNull(flavour, "userId", client => client.GetUserAsync(null!));

    [Theory]
    [MemberData(nameof(AdminClients))]
    public void ListUserRightsAsync_rejects_a_null_userId_synchronously(string flavour) =>
        AssertAdminRejectsNull(flavour, "userId", client => client.ListUserRightsAsync(null!));

    [Theory]
    [MemberData(nameof(AdminClients))]
    public void GrantUserRightsAsync_rejects_a_null_rights_synchronously(string flavour) =>
        AssertAdminRejectsNull(flavour, "rights", client => client.GrantUserRightsAsync("alice", null!));

    [Theory]
    [MemberData(nameof(AdminClients))]
    public void RevokeUserRightsAsync_rejects_a_null_rights_synchronously(string flavour) =>
        AssertAdminRejectsNull(flavour, "rights", client => client.RevokeUserRightsAsync("alice", null!));

    [Theory]
    [MemberData(nameof(AdminClients))]
    public void GetPackageAsync_rejects_a_null_packageId_synchronously(string flavour) =>
        AssertAdminRejectsNull(flavour, "packageId", client => client.GetPackageAsync(null!));

    [Theory]
    [MemberData(nameof(AdminClients))]
    public void UploadDarAsync_rejects_a_null_darFile_synchronously(string flavour) =>
        AssertAdminRejectsNull(flavour, "darFile", client => client.UploadDarAsync(null!));

    [Theory]
    [MemberData(nameof(AdminClients))]
    public void ValidateDarAsync_rejects_a_null_darFile_synchronously(string flavour) =>
        AssertAdminRejectsNull(flavour, "darFile", client => client.ValidateDarAsync(null!));

    [Theory]
    [MemberData(nameof(AdminClients))]
    public void ListVettedPackagesAsync_accepts_a_null_prefix_filter_as_no_filter(string flavour)
    {
        using var scope = NewAdminClient(flavour);

        var act = () => { _ = scope.Client.ListVettedPackagesAsync(null); };

        act.Should().NotThrow<ArgumentNullException>(
            "the prefix filter is declared nullable and null means 'every vetted package'");
    }

    [Theory]
    [MemberData(nameof(AdminClients))]
    public void ListUserRightsAsync_accepts_an_empty_userId_as_the_authenticated_user(string flavour)
    {
        using var scope = NewAdminClient(flavour);

        var act = () => { _ = scope.Client.ListUserRightsAsync(string.Empty); };

        act.Should().NotThrow<ArgumentException>(
            "ListUserRightsRequest.user_id is documented as 'if set to empty string, then the rights "
            + "for the authenticated user will be listed', so the empty string is a meaningful value "
            + "and this guard must stay ThrowIfNull rather than ThrowIfNullOrWhiteSpace");
    }

    [Theory]
    [MemberData(nameof(AdminClients))]
    public void GetUserAsync_accepts_an_empty_userId_as_the_authenticated_user(string flavour)
    {
        using var scope = NewAdminClient(flavour);

        var act = () => { _ = scope.Client.GetUserAsync(string.Empty); };

        act.Should().NotThrow<ArgumentException>(
            "GetUserRequest.user_id is marked Optional and documented as 'If set to empty string (the "
            + "default), then the data for the authenticated user will be retrieved', so the empty "
            + "string is a meaningful value and this guard must stay ThrowIfNull rather than "
            + "ThrowIfNullOrWhiteSpace");
    }

    [Theory]
    [MemberData(nameof(AdminClients))]
    public void AllocatePartyAsync_accepts_an_empty_partyIdHint_as_participant_chosen(string flavour)
    {
        using var scope = NewAdminClient(flavour);

        var act = () => { _ = scope.Client.AllocatePartyAsync(string.Empty); };

        act.Should().NotThrow<ArgumentException>(
            "AllocatePartyRequest.party_id_hint is marked Optional and documented as 'A hint to the "
            + "participant which party ID to allocate. It can be ignored', so an empty hint is the "
            + "ordinary way to allocate a participant-chosen party and this guard must stay "
            + "ThrowIfNull rather than ThrowIfNullOrWhiteSpace");
    }

    [Theory]
    [MemberData(nameof(PqsClients))]
    public void FetchByIdAsync_rejects_a_null_contractId_synchronously(string flavour) =>
        AssertPqsRejectsNull(flavour, "contractId", client => client.FetchByIdAsync<Marker>(null!));

    [Theory]
    [MemberData(nameof(PqsClients))]
    public void ExistsAsync_rejects_a_null_contractId_synchronously(string flavour) =>
        AssertPqsRejectsNull(flavour, "contractId", client => client.ExistsAsync<Marker>(null!));

    [Theory]
    [MemberData(nameof(PqsClients))]
    public void QueryAsync_rejects_a_null_filter_synchronously(string flavour) =>
        AssertPqsRejectsNull(flavour, "filter", client => client.QueryAsync<Marker>((PqsFilter)null!));

    [Theory]
    [MemberData(nameof(PqsClients))]
    public void QueryAsync_rejects_a_null_page_synchronously(string flavour) =>
        AssertPqsRejectsNull(flavour, "page", client => client.QueryAsync<Marker>((PqsPage)null!));

    [Theory]
    [MemberData(nameof(PqsClients))]
    public void QueryOneAsync_rejects_a_null_filter_synchronously(string flavour) =>
        AssertPqsRejectsNull(flavour, "filter", client => client.QueryOneAsync<Marker>(null!));

    private static void AssertAdminRejectsNull(
        string flavour,
        string expectedParamName,
        Func<IAdminClient, object> call)
    {
        using var scope = NewAdminClient(flavour);

        var act = () => { _ = call(scope.Client); };

        act.Should().Throw<ArgumentNullException>(
                "the {0} admin client must diagnose a null argument before a task exists", flavour)
            .Which.ParamName.Should().Be(expectedParamName);
    }

    private static void AssertPqsRejectsNull(
        string flavour,
        string expectedParamName,
        Func<IPqsClient, object> call)
    {
        using var scope = NewPqsClient(flavour);

        var act = () => { _ = call(scope.Client); };

        act.Should().Throw<ArgumentNullException>(
                "the {0} PQS client must diagnose a null argument before a task exists", flavour)
            .Which.ParamName.Should().Be(expectedParamName);
    }

    private static ClientScope<IAdminClient> NewAdminClient(string flavour) => flavour switch
    {
        Real => ClientScope<IAdminClient>.Resolve(new ServiceCollection()
            .AddAdminClient(options => options.GrpcAddress = "http://127.0.0.1:1")),
        Fake => ClientScope<IAdminClient>.Owning(FakeAdminClient.Create().Build()),
        _ => throw new ArgumentOutOfRangeException(nameof(flavour), flavour, "Unknown admin client."),
    };

    private static ClientScope<IPqsClient> NewPqsClient(string flavour) => flavour switch
    {
        Real => ClientScope<IPqsClient>.Resolve(new ServiceCollection()
            .AddPqsClient(options => options.ConnectionString = "Host=127.0.0.1;Database=none")),
        Fake => ClientScope<IPqsClient>.Owning(FakePqsClient.Create().Build()),
        _ => throw new ArgumentOutOfRangeException(nameof(flavour), flavour, "Unknown PQS client."),
    };

    private sealed class ClientScope<TClient>(TClient client, IDisposable? owned) : IDisposable
        where TClient : notnull
    {
        public TClient Client { get; } = client;

        public static ClientScope<TClient> Resolve(IServiceCollection services)
        {
            var provider = services.BuildServiceProvider();
            return new ClientScope<TClient>(provider.GetRequiredService<TClient>(), provider);
        }

        public static ClientScope<TClient> Owning(TClient client) =>
            new(client, client as IDisposable);

        public void Dispose() => owned?.Dispose();
    }
}
