// Copyright (c) 2026 Peaceful Studio OÜ. All rights reserved.

namespace Canton.Ledger.Grpc.Client;

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
    /// <param name="displayName">Optional display name for the party.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The allocated party details.</returns>
    Task<PartyDetails> AllocatePartyAsync(
        string partyIdHint,
        string? displayName = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets details for the specified parties.
    /// </summary>
    Task<IReadOnlyList<PartyDetails>> GetPartiesAsync(
        IEnumerable<string> partyIds,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists all known parties.
    /// </summary>
    Task<IReadOnlyList<PartyDetails>> ListKnownPartiesAsync(
        int pageSize = 100,
        string? pageToken = null,
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
    /// Lists all users.
    /// </summary>
    Task<IReadOnlyList<UserDetails>> ListUsersAsync(
        int pageSize = 100,
        string? pageToken = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists all Daml-LF packages known to the participant.
    /// </summary>
    Task<IReadOnlyList<PackageDetails>> ListKnownPackagesAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Downloads the archive payload of a single package.
    /// </summary>
    /// <param name="packageId">The ID of the requested package.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The <c>daml_lf</c> archive payload bytes.</returns>
    Task<byte[]> GetPackageAsync(
        string packageId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the packages vetted on the participant's connected synchronizers.
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
    /// Uploads a DAR file to the participant.
    /// </summary>
    /// <param name="darFile">The DAR file contents.</param>
    /// <param name="submissionId">Optional unique submission identifier; the ledger generates one when null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task UploadDarAsync(
        byte[] darFile,
        string? submissionId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates a DAR file without uploading it; throws on validation failure.
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
/// Details about a user.
/// </summary>
public record UserDetails(
    string UserId,
    string PrimaryParty,
    IReadOnlyList<UserRight> Rights);

/// <summary>
/// Details about a Daml-LF package known to the participant.
/// </summary>
public record PackageDetails(
    string PackageId,
    string Name,
    string Version,
    ulong PackageSize,
    DateTimeOffset KnownSince);

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
}
