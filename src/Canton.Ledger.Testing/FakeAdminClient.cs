// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Canton.Ledger.Abstractions;

namespace Canton.Ledger.Testing;

/// <summary>
/// An in-memory <see cref="IAdminClient"/> test double that serves canned participant, party,
/// user, and package data staged through the fluent <see cref="FakeAdminClientBuilder"/>. It lets
/// business logic that talks to the admin client be unit-tested without a live participant and
/// without a mocking framework.
/// </summary>
/// <remarks>
/// A query-style member you did not stage throws a descriptive <see cref="NotSupportedException"/>
/// naming the missing setup, so a test never silently exercises unconfigured behaviour. The
/// mutation-only members with no return payload — <see cref="GrantUserRightsAsync"/>,
/// <see cref="RevokeUserRightsAsync"/>, <see cref="UploadDarAsync"/>, <see cref="ValidateDarAsync"/>
/// — are unconditional no-op successes instead, the same way <see cref="FakeLedgerClient.Dispose"/>
/// is: there is no return value to fake, so requiring staging first would add ceremony without
/// adding safety. <see cref="CreateUserAsync"/> and <see cref="AllocatePartyAsync"/> sit between
/// these two shapes: they do have a return payload, but rather than echoing it from the call
/// arguments they look up and replay the matching staged <see cref="UserDetails"/> / <see cref="PartyDetails"/>
/// verbatim, ignoring <c>primaryParty</c>/<c>rights</c> and <c>synchronizerId</c> respectively — stage
/// the exact result you expect back under the same id/hint rather than relying on the arguments a test's
/// system under test happens to pass in. <see cref="AllocatePartyAsync"/> in particular does not model
/// the multi-synchronizer allocation failure a live participant surfaces when <c>synchronizerId</c> is
/// omitted; a test exercising that path needs a different approach. The per-id stages
/// (<see cref="AllocatePartyAsync"/>'s and <see cref="CreateUserAsync"/>'s) are also independent from
/// the list-style stages (<see cref="ListKnownPartiesAsync"/>/<see cref="GetPartiesAsync"/> and
/// <see cref="ListUsersAsync"/>): staging an allocated party or a user does not automatically make it
/// appear in the corresponding known-parties/known-users list — unlike a live participant, where
/// allocating a party or creating a user makes it listable. Stage both sides explicitly if your test
/// exercises an allocate/create-then-list flow. Construct instances through <see cref="Create"/>.
/// </remarks>
public sealed class FakeAdminClient : IAdminClient
{
    private readonly string? _participantId;
    private readonly IReadOnlyDictionary<string, PartyDetails> _allocatedParties;
    private readonly IReadOnlyList<PartyDetails>? _knownParties;
    private readonly IReadOnlyDictionary<string, UserDetails> _users;
    private readonly IReadOnlyList<UserDetails>? _knownUsers;
    private readonly IReadOnlyDictionary<string, IReadOnlyList<UserRight>> _userRights;
    private readonly IReadOnlyList<PackageDetails>? _knownPackages;
    private readonly IReadOnlyDictionary<string, PackageArchive> _packages;
    private readonly IReadOnlyList<VettedPackage>? _vettedPackages;

    internal FakeAdminClient(
        string? participantId,
        IReadOnlyDictionary<string, PartyDetails> allocatedParties,
        IReadOnlyList<PartyDetails>? knownParties,
        IReadOnlyDictionary<string, UserDetails> users,
        IReadOnlyList<UserDetails>? knownUsers,
        IReadOnlyDictionary<string, IReadOnlyList<UserRight>> userRights,
        IReadOnlyList<PackageDetails>? knownPackages,
        IReadOnlyDictionary<string, PackageArchive> packages,
        IReadOnlyList<VettedPackage>? vettedPackages)
    {
        _participantId = participantId;
        _allocatedParties = allocatedParties;
        _knownParties = knownParties;
        _users = users;
        _knownUsers = knownUsers;
        _userRights = userRights;
        _knownPackages = knownPackages;
        _packages = packages;
        _vettedPackages = vettedPackages;
    }

    /// <summary>Starts a new fluent builder for a <see cref="FakeAdminClient"/>.</summary>
    /// <returns>An empty builder; stage data on it, then call <see cref="FakeAdminClientBuilder.Build"/>.</returns>
    public static FakeAdminClientBuilder Create() => new();

    /// <inheritdoc />
    public Task<string> GetParticipantIdAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(_participantId ?? throw new NotSupportedException(
            "FakeAdminClient has no participant id staged. Stage one with " +
            "FakeAdminClient.Create().WithParticipantId(...).Build() before exercising this path."));

    /// <inheritdoc />
    public Task<PartyDetails> AllocatePartyAsync(
        string partyIdHint,
        string? synchronizerId = null,
        CancellationToken cancellationToken = default)
    {
        if (_allocatedParties.TryGetValue(partyIdHint, out var details))
        {
            return Task.FromResult(details);
        }

        throw new NotSupportedException(
            $"FakeAdminClient has no allocated party staged for party id hint '{partyIdHint}'. Stage one with " +
            $"FakeAdminClient.Create().WithAllocatedParty(\"{partyIdHint}\", ...).Build() before exercising this path.");
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<PartyDetails>> GetPartiesAsync(
        IEnumerable<string> partyIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(partyIds);
        var requested = new HashSet<string>(partyIds);
        return Task.FromResult<IReadOnlyList<PartyDetails>>(
            KnownParties().Where(p => requested.Contains(p.Party)).ToList());
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<PartyDetails>> ListKnownPartiesAsync(
        int pageSize = 100,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(KnownParties());

    /// <inheritdoc />
    public Task<UserDetails> CreateUserAsync(
        string userId,
        string primaryParty,
        IEnumerable<UserRight>? rights = null,
        CancellationToken cancellationToken = default)
    {
        if (_users.TryGetValue(userId, out var details))
        {
            return Task.FromResult(details);
        }

        throw new NotSupportedException(
            $"FakeAdminClient has no user staged for user id '{userId}'. Stage one with " +
            $"FakeAdminClient.Create().WithUser(new UserDetails(\"{userId}\", ...)).Build() before exercising this path.");
    }

    /// <inheritdoc />
    public Task<UserDetails?> GetUserAsync(string userId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_users.TryGetValue(userId, out var details) ? details : null);

    /// <inheritdoc />
    public Task GrantUserRightsAsync(
        string userId,
        IEnumerable<UserRight> rights,
        CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    /// <inheritdoc />
    public Task RevokeUserRightsAsync(
        string userId,
        IEnumerable<UserRight> rights,
        CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    /// <inheritdoc />
    public Task<IReadOnlyList<UserRight>?> ListUserRightsAsync(
        string userId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(_userRights.TryGetValue(userId, out var rights) ? rights : null);

    /// <inheritdoc />
    public Task<IReadOnlyList<UserDetails>> ListUsersAsync(
        int pageSize = 100,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(_knownUsers as IReadOnlyList<UserDetails> ?? throw new NotSupportedException(
            "FakeAdminClient has no known users staged. Stage them with " +
            "FakeAdminClient.Create().WithUsers(...).Build() before exercising this path."));

    /// <inheritdoc />
    public Task<IReadOnlyList<PackageDetails>> ListKnownPackagesAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(_knownPackages as IReadOnlyList<PackageDetails> ?? throw new NotSupportedException(
            "FakeAdminClient has no known packages staged. Stage them with " +
            "FakeAdminClient.Create().WithKnownPackages(...).Build() before exercising this path."));

    /// <inheritdoc />
    public Task<PackageArchive> GetPackageAsync(string packageId, CancellationToken cancellationToken = default)
    {
        if (_packages.TryGetValue(packageId, out var archive))
        {
            return Task.FromResult(archive);
        }

        throw new NotSupportedException(
            $"FakeAdminClient has no package archive staged for package id '{packageId}'. Stage one with " +
            $"FakeAdminClient.Create().WithPackage(\"{packageId}\", ...).Build() before exercising this path.");
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<VettedPackage>> ListVettedPackagesAsync(
        IEnumerable<string>? packageNamePrefixes = null,
        CancellationToken cancellationToken = default)
    {
        if (_vettedPackages is null)
        {
            throw new NotSupportedException(
                "FakeAdminClient has no vetted packages staged. Stage them with " +
                "FakeAdminClient.Create().WithVettedPackages(...).Build() before exercising this path.");
        }

        var prefixes = packageNamePrefixes?.ToList();
        if (prefixes is not { Count: > 0 })
        {
            return Task.FromResult<IReadOnlyList<VettedPackage>>(_vettedPackages);
        }

        return Task.FromResult<IReadOnlyList<VettedPackage>>(
            _vettedPackages.Where(p => prefixes.Any(prefix => p.PackageName.StartsWith(prefix, StringComparison.Ordinal))).ToList());
    }

    /// <inheritdoc />
    public Task UploadDarAsync(
        byte[] darFile,
        string? submissionId = null,
        CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    /// <inheritdoc />
    public Task ValidateDarAsync(byte[] darFile, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    /// <inheritdoc />
    public void Dispose()
    {
    }

    private IReadOnlyList<PartyDetails> KnownParties() =>
        _knownParties ?? throw new NotSupportedException(
            "FakeAdminClient has no known parties staged. Stage them with " +
            "FakeAdminClient.Create().WithParties(...).Build() before exercising this path.");
}
