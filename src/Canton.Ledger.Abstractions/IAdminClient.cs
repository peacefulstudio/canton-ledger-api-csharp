// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

namespace Canton.Ledger.Abstractions;

/// <summary>
/// Client interface for Canton participant administration.
/// Provides methods for managing parties, users, and packages.
/// </summary>
public interface IAdminClient : IDisposable
{
    /// <summary>
    /// Gets the participant ID.
    /// </summary>
    Task<string> GetParticipantIdAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Allocates a new party on the ledger.
    /// </summary>
    /// <param name="partyIdHint">A hint for the party ID (may be modified by the ledger).</param>
    /// <param name="synchronizerId">
    /// Optional id of the synchronizer to allocate the party on. Required when the participant
    /// is connected to more than one synchronizer — otherwise Canton rejects the request with
    /// <c>PARTY_ALLOCATION_CANNOT_DETERMINE_SYNCHRONIZER</c>. When <see langword="null"/> the
    /// participant falls back to its single connected synchronizer (the prior behaviour).
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The allocated party details.</returns>
    Task<PartyDetails> AllocatePartyAsync(
        string partyIdHint,
        string? synchronizerId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets details for the specified parties.
    /// </summary>
    Task<IReadOnlyList<PartyDetails>> GetPartiesAsync(
        IEnumerable<string> partyIds,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists all known parties.
    /// Transparently follows server pagination and returns the complete result set.
    /// </summary>
    /// <param name="pageSize">Maximum number of parties fetched per server round-trip.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<PartyDetails>> ListKnownPartiesAsync(
        int pageSize = 100,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a new user on the participant.
    /// </summary>
    Task<UserDetails> CreateUserAsync(
        string userId,
        string primaryParty,
        IEnumerable<UserRight>? rights = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets details for a user.
    /// </summary>
    Task<UserDetails?> GetUserAsync(
        string userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Grants rights to a user.
    /// </summary>
    Task GrantUserRightsAsync(
        string userId,
        IEnumerable<UserRight> rights,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Revokes rights from a user.
    /// </summary>
    Task RevokeUserRightsAsync(
        string userId,
        IEnumerable<UserRight> rights,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the rights granted to a user.
    /// </summary>
    /// <param name="userId">
    /// The user whose rights to list. An empty string lists the rights of the authenticated user.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// The rights granted to the user, or <see langword="null"/> when the user does not exist —
    /// mirroring <see cref="GetUserAsync"/>.
    /// </returns>
    Task<IReadOnlyList<UserRight>?> ListUserRightsAsync(
        string userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists all users.
    /// Transparently follows server pagination and returns the complete result set.
    /// </summary>
    /// <param name="pageSize">Maximum number of users fetched per server round-trip.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<UserDetails>> ListUsersAsync(
        int pageSize = 100,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists all Daml-LF packages known to the participant.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<PackageDetails>> ListKnownPackagesAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Downloads the archive of a single package.
    /// </summary>
    /// <param name="packageId">The ID of the requested package.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The <c>daml_lf</c> archive payload together with its hash and hash function.</returns>
    Task<PackageArchive> GetPackageAsync(
        string packageId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the packages vetted on the participant's connected synchronizers.
    /// Transparently follows server pagination and returns the complete result set.
    /// </summary>
    /// <param name="packageNamePrefixes">
    /// Optional package name prefixes to filter by; a vetted package matches when its name
    /// starts with at least one prefix. Null or empty returns all vetted packages.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<VettedPackage>> ListVettedPackagesAsync(
        IEnumerable<string>? packageNamePrefixes = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Uploads a DAR file to the participant. By default the ledger also vets all packages
    /// in the DAR (the underlying request's <c>vetting_change</c> defaults to
    /// <c>VETTING_CHANGE_VET_ALL_PACKAGES</c>).
    /// </summary>
    /// <param name="darFile">The DAR file contents.</param>
    /// <param name="submissionId">Optional unique submission identifier; the ledger generates one when null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task UploadDarAsync(
        byte[] darFile,
        string? submissionId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates a DAR file without persisting or vetting anything;
    /// throws <c>Grpc.Core.RpcException</c> on validation failure.
    /// </summary>
    /// <param name="darFile">The DAR file contents.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task ValidateDarAsync(
        byte[] darFile,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Details about a party.
/// </summary>
public record PartyDetails(
    string Party,
    bool IsLocal);

/// <summary>
/// Details about a user. Rights are not part of the underlying <c>User</c> proto;
/// read them back with <see cref="IAdminClient.ListUserRightsAsync"/>.
/// </summary>
public record UserDetails(
    string UserId,
    string PrimaryParty);

/// <summary>
/// Details about a Daml-LF package known to the participant.
/// </summary>
public record PackageDetails(
    string PackageId,
    string Name,
    string Version,
    long PackageSize,
    DateTimeOffset KnownSince);

/// <summary>
/// The hash function used to compute a <see cref="PackageArchive.Hash"/>.
/// </summary>
public enum HashFunction
{
    /// <summary>SHA-256.</summary>
    Sha256,

    /// <summary>
    /// A hash function reported by the participant that this SDK version does not recognise.
    /// </summary>
    Unrecognized,
}

/// <summary>
/// A package archive downloaded from the participant: the <c>daml_lf</c> payload
/// together with its hash and the hash function used to compute it.
/// </summary>
public sealed record PackageArchive(
    ReadOnlyMemory<byte> Payload,
    string Hash,
    HashFunction HashFunction)
{
    /// <inheritdoc />
    public bool Equals(PackageArchive? other) =>
        ReferenceEquals(this, other)
        || (other is not null
            && Payload.Span.SequenceEqual(other.Payload.Span)
            && Hash == other.Hash
            && HashFunction == other.HashFunction);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var hashCode = new HashCode();
        hashCode.AddBytes(Payload.Span);
        hashCode.Add(Hash);
        hashCode.Add(HashFunction);
        return hashCode.ToHashCode();
    }
}

/// <summary>
/// A package vetted on a participant and synchronizer.
/// </summary>
public record VettedPackage(
    string PackageId,
    string PackageName,
    string PackageVersion,
    string ParticipantId,
    string SynchronizerId);

/// <summary>
/// A right that can be granted to a user.
/// </summary>
public abstract record UserRight
{
    /// <summary>
    /// Right to act as a party.
    /// </summary>
    public record ActAs(string Party) : UserRight;

    /// <summary>
    /// Right to read as a party.
    /// </summary>
    public record ReadAs(string Party) : UserRight;

    /// <summary>
    /// Right to administer the participant.
    /// </summary>
    public record ParticipantAdmin : UserRight;

    /// <summary>
    /// Right to administer an identity provider.
    /// </summary>
    public record IdentityProviderAdmin : UserRight;

    /// <summary>
    /// Right to read ledger data visible to any party on the participant.
    /// Intended for tools that consume the whole ledger, such as PQS.
    /// </summary>
    public record ReadAsAnyParty : UserRight;

    /// <summary>
    /// Right to prepare and execute submissions as a party, without any read entitlement.
    /// Combine with <see cref="ReadAs"/> when reading is also required; <see cref="ActAs"/>
    /// implicitly contains this right.
    /// </summary>
    public record ExecuteAs(string Party) : UserRight;

    /// <summary>
    /// Right to prepare and execute submissions as any party on the participant.
    /// Intended for users that perform interactive submissions on behalf of many parties.
    /// </summary>
    public record ExecuteAsAnyParty : UserRight;
}
