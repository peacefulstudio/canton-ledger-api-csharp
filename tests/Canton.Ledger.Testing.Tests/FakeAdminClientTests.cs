// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Canton.Ledger.Abstractions;
using AwesomeAssertions;
using Xunit;

namespace Canton.Ledger.Testing.Tests;

public class FakeAdminClientTests
{
    [Fact]
    public async Task GetParticipantIdAsync_returns_the_staged_participant_id()
    {
        var client = FakeAdminClient.Create().WithParticipantId("participant1").Build();

        var participantId = await client.GetParticipantIdAsync(TestContext.Current.CancellationToken);

        participantId.Should().Be("participant1");
    }

    [Fact]
    public async Task GetParticipantIdAsync_for_unstaged_client_throws_descriptive_NotSupportedException()
    {
        var client = FakeAdminClient.Create().Build();

        var act = () => client.GetParticipantIdAsync();

        (await act.Should().ThrowAsync<NotSupportedException>())
            .Which.Message.Should().Contain("WithParticipantId");
    }

    [Fact]
    public async Task AllocatePartyAsync_returns_the_staged_PartyDetails_for_the_hint()
    {
        var details = new PartyDetails("alice::1220", IsLocal: true);
        var client = FakeAdminClient.Create().WithAllocatedParty("alice", details).Build();

        var result = await client.AllocatePartyAsync("alice", cancellationToken: TestContext.Current.CancellationToken);

        result.Should().Be(details);
    }

    [Fact]
    public async Task AllocatePartyAsync_for_unstaged_hint_throws_descriptive_NotSupportedException()
    {
        var client = FakeAdminClient.Create()
            .WithAllocatedParty("alice", new PartyDetails("alice::1220", true))
            .Build();

        var act = () => client.AllocatePartyAsync("bob");

        (await act.Should().ThrowAsync<NotSupportedException>())
            .Which.Message.Should().Contain("WithAllocatedParty").And.Contain("bob");
    }

    [Fact]
    public async Task GetPartiesAsync_returns_the_staged_parties_matching_the_requested_ids()
    {
        var alice = new PartyDetails("alice", true);
        var bob = new PartyDetails("bob", true);
        var client = FakeAdminClient.Create().WithParties(alice, bob).Build();

        var result = await client.GetPartiesAsync(["bob"], TestContext.Current.CancellationToken);

        result.Should().ContainSingle().Which.Should().Be(bob);
    }

    [Fact]
    public async Task ListKnownPartiesAsync_returns_all_staged_parties()
    {
        var alice = new PartyDetails("alice", true);
        var bob = new PartyDetails("bob", true);
        var client = FakeAdminClient.Create().WithParties(alice, bob).Build();

        var result = await client.ListKnownPartiesAsync(cancellationToken: TestContext.Current.CancellationToken);

        result.Should().Equal(alice, bob);
    }

    [Fact]
    public async Task ListKnownPartiesAsync_for_unstaged_client_throws_descriptive_NotSupportedException()
    {
        var client = FakeAdminClient.Create().Build();

        var act = () => client.ListKnownPartiesAsync();

        (await act.Should().ThrowAsync<NotSupportedException>())
            .Which.Message.Should().Contain("WithParties");
    }

    [Fact]
    public async Task GetPartiesAsync_for_unstaged_client_throws_descriptive_NotSupportedException()
    {
        var client = FakeAdminClient.Create().Build();

        var act = () => client.GetPartiesAsync(["alice"]);

        (await act.Should().ThrowAsync<NotSupportedException>())
            .Which.Message.Should().Contain("WithParties");
    }

    [Fact]
    public async Task ListKnownPartiesAsync_staged_with_zero_parties_returns_empty()
    {
        var client = FakeAdminClient.Create().WithParties().Build();

        var result = await client.ListKnownPartiesAsync(cancellationToken: TestContext.Current.CancellationToken);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetUserAsync_returns_the_staged_UserDetails()
    {
        var user = new UserDetails("alice", "alice");
        var client = FakeAdminClient.Create().WithUser(user).Build();

        var result = await client.GetUserAsync("alice", TestContext.Current.CancellationToken);

        result.Should().Be(user);
    }

    [Fact]
    public async Task GetUserAsync_returns_null_for_unstaged_userId()
    {
        var client = FakeAdminClient.Create().WithUser(new UserDetails("alice", "alice")).Build();

        var result = await client.GetUserAsync("bob", TestContext.Current.CancellationToken);

        result.Should().BeNull();
    }

    [Fact]
    public async Task CreateUserAsync_returns_the_staged_UserDetails_for_the_userId()
    {
        var user = new UserDetails("alice", "alice");
        var client = FakeAdminClient.Create().WithUser(user).Build();

        var result = await client.CreateUserAsync("alice", "alice", cancellationToken: TestContext.Current.CancellationToken);

        result.Should().Be(user);
    }

    [Fact]
    public async Task CreateUserAsync_for_unstaged_userId_throws_descriptive_NotSupportedException()
    {
        var client = FakeAdminClient.Create().Build();

        var act = () => client.CreateUserAsync("alice", "alice");

        (await act.Should().ThrowAsync<NotSupportedException>())
            .Which.Message.Should().Contain("WithUser").And.Contain("alice");
    }

    [Fact]
    public async Task ListUsersAsync_returns_all_staged_users()
    {
        var alice = new UserDetails("alice", "alice");
        var bob = new UserDetails("bob", "bob");
        var client = FakeAdminClient.Create().WithUsers(alice, bob).Build();

        var result = await client.ListUsersAsync(cancellationToken: TestContext.Current.CancellationToken);

        result.Should().Equal(alice, bob);
    }

    [Fact]
    public async Task ListUsersAsync_for_unstaged_client_throws_descriptive_NotSupportedException()
    {
        var client = FakeAdminClient.Create().Build();

        var act = () => client.ListUsersAsync();

        (await act.Should().ThrowAsync<NotSupportedException>())
            .Which.Message.Should().Contain("WithUsers");
    }

    [Fact]
    public async Task ListUserRightsAsync_returns_the_staged_rights()
    {
        var rights = new UserRight[] { new UserRight.ParticipantAdmin() };
        var client = FakeAdminClient.Create().WithUserRights("alice", rights).Build();

        var result = await client.ListUserRightsAsync("alice", TestContext.Current.CancellationToken);

        result.Should().Equal(rights);
    }

    [Fact]
    public async Task ListUserRightsAsync_returns_null_for_unstaged_userId()
    {
        var client = FakeAdminClient.Create().WithUserRights("alice", new UserRight.ParticipantAdmin()).Build();

        var result = await client.ListUserRightsAsync("bob", TestContext.Current.CancellationToken);

        result.Should().BeNull();
    }

    [Fact]
    public async Task GrantUserRightsAsync_succeeds_without_any_staging()
    {
        var client = FakeAdminClient.Create().Build();

        var act = () => client.GrantUserRightsAsync("alice", [new UserRight.ParticipantAdmin()]);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task RevokeUserRightsAsync_succeeds_without_any_staging()
    {
        var client = FakeAdminClient.Create().Build();

        var act = () => client.RevokeUserRightsAsync("alice", [new UserRight.ParticipantAdmin()]);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task ListKnownPackagesAsync_returns_all_staged_packages()
    {
        var package = new PackageDetails("pkg1", "name", "1.0.0", 123L, DateTimeOffset.UnixEpoch);
        var client = FakeAdminClient.Create().WithKnownPackages(package).Build();

        var result = await client.ListKnownPackagesAsync(TestContext.Current.CancellationToken);

        result.Should().Equal(package);
    }

    [Fact]
    public async Task ListKnownPackagesAsync_for_unstaged_client_throws_descriptive_NotSupportedException()
    {
        var client = FakeAdminClient.Create().Build();

        var act = () => client.ListKnownPackagesAsync();

        (await act.Should().ThrowAsync<NotSupportedException>())
            .Which.Message.Should().Contain("WithKnownPackages");
    }

    [Fact]
    public async Task GetPackageAsync_returns_the_staged_PackageArchive()
    {
        var archive = new PackageArchive(new byte[] { 1, 2, 3 }, "hash1", HashFunction.Sha256);
        var client = FakeAdminClient.Create().WithPackage("pkg1", archive).Build();

        var result = await client.GetPackageAsync("pkg1", TestContext.Current.CancellationToken);

        result.Should().Be(archive);
    }

    [Fact]
    public async Task GetPackageAsync_for_unstaged_packageId_throws_descriptive_NotSupportedException()
    {
        var client = FakeAdminClient.Create().Build();

        var act = () => client.GetPackageAsync("pkg1");

        (await act.Should().ThrowAsync<NotSupportedException>())
            .Which.Message.Should().Contain("WithPackage").And.Contain("pkg1");
    }

    [Fact]
    public async Task ListVettedPackagesAsync_with_no_prefixes_returns_all_staged_packages()
    {
        var alpha = new VettedPackage("pkg1", "alpha-service", "1.0.0", "participant1", "sync1");
        var beta = new VettedPackage("pkg2", "beta-service", "1.0.0", "participant1", "sync1");
        var client = FakeAdminClient.Create().WithVettedPackages(alpha, beta).Build();

        var result = await client.ListVettedPackagesAsync(cancellationToken: TestContext.Current.CancellationToken);

        result.Should().Equal(alpha, beta);
    }

    [Fact]
    public async Task ListVettedPackagesAsync_filters_by_package_name_prefix()
    {
        var alpha = new VettedPackage("pkg1", "alpha-service", "1.0.0", "participant1", "sync1");
        var beta = new VettedPackage("pkg2", "beta-service", "1.0.0", "participant1", "sync1");
        var client = FakeAdminClient.Create().WithVettedPackages(alpha, beta).Build();

        var result = await client.ListVettedPackagesAsync(["alpha"], TestContext.Current.CancellationToken);

        result.Should().ContainSingle().Which.Should().Be(alpha);
    }

    [Fact]
    public async Task ListVettedPackagesAsync_for_unstaged_client_throws_descriptive_NotSupportedException()
    {
        var client = FakeAdminClient.Create().Build();

        var act = () => client.ListVettedPackagesAsync();

        (await act.Should().ThrowAsync<NotSupportedException>())
            .Which.Message.Should().Contain("WithVettedPackages");
    }

    [Fact]
    public async Task UploadDarAsync_succeeds_without_any_staging()
    {
        var client = FakeAdminClient.Create().Build();

        var act = () => client.UploadDarAsync([1, 2, 3]);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task ValidateDarAsync_succeeds_without_any_staging()
    {
        var client = FakeAdminClient.Create().Build();

        var act = () => client.ValidateDarAsync([1, 2, 3]);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public void Dispose_does_not_throw()
    {
        var client = FakeAdminClient.Create().Build();

        var act = () => client.Dispose();

        act.Should().NotThrow();
    }

    [Fact]
    public async Task Build_snapshots_staged_users_so_later_builder_mutation_is_ignored()
    {
        var builder = FakeAdminClient.Create().WithUser(new UserDetails("alice", "alice"));
        var client = builder.Build();
        builder.WithUser(new UserDetails("bob", "bob"));

        var alice = await client.GetUserAsync("alice", TestContext.Current.CancellationToken);
        var bob = await client.GetUserAsync("bob", TestContext.Current.CancellationToken);

        alice.Should().NotBeNull();
        bob.Should().BeNull();
    }
}
