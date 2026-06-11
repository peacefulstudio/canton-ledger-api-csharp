// Copyright (c) 2026 Peaceful Studio OÜ. All rights reserved.

using System.Diagnostics;
using Canton.Ledger.Auth;
using Com.Daml.Ledger.Api.V2;
using Com.Daml.Ledger.Api.V2.Admin;
using Google.Protobuf;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Peaceful.Extensions.Logging;

namespace Canton.Ledger.Grpc.Client;

/// <summary>
/// Implementation of the Canton participant admin client using gRPC.
/// </summary>
public sealed partial class AdminClient : IAdminClient
{
    /// <summary>
    /// The <see cref="ActivitySource"/> name used for OpenTelemetry tracing.
    /// Register with <c>tracing.AddSource(AdminClient.ActivitySourceName)</c>.
    /// </summary>
    public static string ActivitySourceName => typeof(AdminClient).FullName!;

    private static readonly ActivitySource ActivitySource = new(typeof(AdminClient).FullName!);
    private static readonly ILogger<AdminClient> Logger = StaticLoggerFactory.Create<AdminClient>();

    private readonly GrpcChannel _channel;
    private readonly PartyManagementService.PartyManagementServiceClient _partyService;
    private readonly UserManagementService.UserManagementServiceClient _userService;
    private readonly PackageManagementService.PackageManagementServiceClient _packageManagementService;
    private readonly PackageService.PackageServiceClient _packageService;
    private readonly LedgerClientOptions _options;
    private readonly ITokenProvider? _tokenProvider;

    /// <summary>
    /// Creates a new AdminClient with the specified options and token provider.
    /// </summary>
    public AdminClient(IOptions<LedgerClientOptions> options, ITokenProvider tokenProvider)
        : this(options.Value, tokenProvider)
    {
    }

    /// <summary>
    /// Creates a new AdminClient with the specified options and token provider.
    /// </summary>
    public AdminClient(LedgerClientOptions options, ITokenProvider tokenProvider)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(tokenProvider);

        _options = options;
        _tokenProvider = tokenProvider;

        _channel = GrpcChannel.ForAddress(_options.GrpcAddress, new GrpcChannelOptions
        {
            MaxReceiveMessageSize = _options.MaxMessageSize,
            MaxSendMessageSize = _options.MaxMessageSize
        });

        _partyService = new PartyManagementService.PartyManagementServiceClient(_channel);
        _userService = new UserManagementService.UserManagementServiceClient(_channel);
        _packageManagementService = new PackageManagementService.PackageManagementServiceClient(_channel);
        _packageService = new PackageService.PackageServiceClient(_channel);

        LogInitialized(Logger, _options.GrpcAddress);

        if (ReferenceEquals(_tokenProvider, ITokenProvider.None))
            LogUnauthenticatedMode(Logger);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "AdminClient initialized with endpoint {Endpoint}")]
    private static partial void LogInitialized(ILogger logger, string endpoint);

    [LoggerMessage(Level = LogLevel.Warning, Message = "AdminClient running in unauthenticated mode. If this is unintentional, register an ITokenProvider or use the AddAdminClient overload that accepts authConfiguration.")]
    private static partial void LogUnauthenticatedMode(ILogger logger);

    /// <summary>
    /// Creates a new AdminClient with injected gRPC channel and service clients.
    /// This constructor is intended for testing scenarios.
    /// </summary>
    internal AdminClient(
        LedgerClientOptions options,
        GrpcChannel channel,
        PartyManagementService.PartyManagementServiceClient partyService,
        UserManagementService.UserManagementServiceClient userService,
        ITokenProvider? tokenProvider = null,
        PackageManagementService.PackageManagementServiceClient? packageManagementService = null,
        PackageService.PackageServiceClient? packageService = null)
    {
        _options = options;
        _channel = channel;
        _partyService = partyService;
        _userService = userService;
        _packageManagementService = packageManagementService ?? new PackageManagementService.PackageManagementServiceClient(channel);
        _packageService = packageService ?? new PackageService.PackageServiceClient(channel);
        _tokenProvider = tokenProvider;
    }

    /// <inheritdoc />
    public async Task<string> GetParticipantIdAsync(CancellationToken cancellationToken = default)
    {
        using var activity = ActivityHelper.StartActivity<AdminClient>(ActivitySource);

        var response = await _partyService.GetParticipantIdAsync(
            new GetParticipantIdRequest(),
            headers: await GetHeadersAsync(cancellationToken),
            deadline: GetDeadline(),
            cancellationToken: cancellationToken);

        return response.ParticipantId;
    }

    /// <inheritdoc />
    public async Task<PartyDetails> AllocatePartyAsync(
        string partyIdHint,
        string? displayName = null,
        CancellationToken cancellationToken = default)
    {
        using var activity = ActivityHelper.StartActivity<AdminClient>(ActivitySource);
        activity?.SetTag("partyIdHint", partyIdHint);

        LogAllocatingParty(Logger, partyIdHint);

        var request = new AllocatePartyRequest
        {
            PartyIdHint = partyIdHint
        };

        var response = await _partyService.AllocatePartyAsync(
            request,
            headers: await GetHeadersAsync(cancellationToken),
            deadline: GetDeadline(),
            cancellationToken: cancellationToken);

        var details = response.PartyDetails;

        LogPartyAllocated(Logger, details.Party);

        return new PartyDetails(details.Party, details.IsLocal);
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Allocating party with hint: {PartyIdHint}")]
    private static partial void LogAllocatingParty(ILogger logger, string partyIdHint);

    [LoggerMessage(Level = LogLevel.Information, Message = "Party allocated: {PartyId}")]
    private static partial void LogPartyAllocated(ILogger logger, string partyId);

    /// <inheritdoc />
    public async Task<IReadOnlyList<PartyDetails>> GetPartiesAsync(
        IEnumerable<string> partyIds,
        CancellationToken cancellationToken = default)
    {
        using var activity = ActivityHelper.StartActivity<AdminClient>(ActivitySource);

        var request = new GetPartiesRequest();
        request.Parties.AddRange(partyIds);

        var response = await _partyService.GetPartiesAsync(
            request,
            headers: await GetHeadersAsync(cancellationToken),
            deadline: GetDeadline(),
            cancellationToken: cancellationToken);

        return response.PartyDetails
            .Select(p => new PartyDetails(p.Party, p.IsLocal))
            .ToList();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<PartyDetails>> ListKnownPartiesAsync(
        int pageSize = 100,
        string? pageToken = null,
        CancellationToken cancellationToken = default)
    {
        using var activity = ActivityHelper.StartActivity<AdminClient>(ActivitySource);

        var request = new ListKnownPartiesRequest
        {
            PageSize = pageSize,
            PageToken = pageToken ?? string.Empty
        };

        var response = await _partyService.ListKnownPartiesAsync(
            request,
            headers: await GetHeadersAsync(cancellationToken),
            deadline: GetDeadline(),
            cancellationToken: cancellationToken);

        return response.PartyDetails
            .Select(p => new PartyDetails(p.Party, p.IsLocal))
            .ToList();
    }

    /// <inheritdoc />
    public async Task<UserDetails> CreateUserAsync(
        string userId,
        string primaryParty,
        IEnumerable<UserRight>? rights = null,
        CancellationToken cancellationToken = default)
    {
        using var activity = ActivityHelper.StartActivity<AdminClient>(ActivitySource);
        activity?.SetTag("userId", userId);

        LogCreatingUser(Logger, userId);

        var user = new User
        {
            Id = userId,
            PrimaryParty = primaryParty
        };

        var request = new CreateUserRequest { User = user };

        if (rights != null)
        {
            request.Rights.AddRange(rights.Select(ToProtoRight));
        }

        var response = await _userService.CreateUserAsync(
            request,
            headers: await GetHeadersAsync(cancellationToken),
            deadline: GetDeadline(),
            cancellationToken: cancellationToken);

        LogUserCreated(Logger, userId);

        return FromProtoUser(response.User);
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Creating user: {UserId}")]
    private static partial void LogCreatingUser(ILogger logger, string userId);

    [LoggerMessage(Level = LogLevel.Information, Message = "User created: {UserId}")]
    private static partial void LogUserCreated(ILogger logger, string userId);

    /// <inheritdoc />
    public async Task<UserDetails?> GetUserAsync(
        string userId,
        CancellationToken cancellationToken = default)
    {
        using var activity = ActivityHelper.StartActivity<AdminClient>(ActivitySource);

        try
        {
            var response = await _userService.GetUserAsync(
                new GetUserRequest { UserId = userId },
                headers: await GetHeadersAsync(cancellationToken),
                deadline: GetDeadline(),
                cancellationToken: cancellationToken);

            return FromProtoUser(response.User);
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.NotFound)
        {
            return null;
        }
    }

    /// <inheritdoc />
    public async Task GrantUserRightsAsync(
        string userId,
        IEnumerable<UserRight> rights,
        CancellationToken cancellationToken = default)
    {
        using var activity = ActivityHelper.StartActivity<AdminClient>(ActivitySource);

        var request = new GrantUserRightsRequest { UserId = userId };
        request.Rights.AddRange(rights.Select(ToProtoRight));

        await _userService.GrantUserRightsAsync(
            request,
            headers: await GetHeadersAsync(cancellationToken),
            deadline: GetDeadline(),
            cancellationToken: cancellationToken);

        LogRightsGranted(Logger, userId);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Rights granted to user {UserId}")]
    private static partial void LogRightsGranted(ILogger logger, string userId);

    /// <inheritdoc />
    public async Task RevokeUserRightsAsync(
        string userId,
        IEnumerable<UserRight> rights,
        CancellationToken cancellationToken = default)
    {
        using var activity = ActivityHelper.StartActivity<AdminClient>(ActivitySource);

        var request = new RevokeUserRightsRequest { UserId = userId };
        request.Rights.AddRange(rights.Select(ToProtoRight));

        await _userService.RevokeUserRightsAsync(
            request,
            headers: await GetHeadersAsync(cancellationToken),
            deadline: GetDeadline(),
            cancellationToken: cancellationToken);

        LogRightsRevoked(Logger, userId);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Rights revoked from user {UserId}")]
    private static partial void LogRightsRevoked(ILogger logger, string userId);

    /// <inheritdoc />
    public async Task<IReadOnlyList<UserDetails>> ListUsersAsync(
        int pageSize = 100,
        string? pageToken = null,
        CancellationToken cancellationToken = default)
    {
        using var activity = ActivityHelper.StartActivity<AdminClient>(ActivitySource);

        var request = new ListUsersRequest
        {
            PageSize = pageSize,
            PageToken = pageToken ?? string.Empty
        };

        var response = await _userService.ListUsersAsync(
            request,
            headers: await GetHeadersAsync(cancellationToken),
            deadline: GetDeadline(),
            cancellationToken: cancellationToken);

        return response.Users.Select(FromProtoUser).ToList();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<PackageDetails>> ListKnownPackagesAsync(
        CancellationToken cancellationToken = default)
    {
        using var activity = ActivityHelper.StartActivity<AdminClient>(ActivitySource);

        var response = await _packageManagementService.ListKnownPackagesAsync(
            new ListKnownPackagesRequest(),
            headers: await GetHeadersAsync(cancellationToken),
            deadline: GetDeadline(),
            cancellationToken: cancellationToken);

        return response.PackageDetails
            .Select(p => new PackageDetails(
                p.PackageId,
                p.Name,
                p.Version,
                (long)p.PackageSize,
                KnownSinceOrThrow(p).ToDateTimeOffset()))
            .ToList();
    }

    private static Google.Protobuf.WellKnownTypes.Timestamp KnownSinceOrThrow(
        Com.Daml.Ledger.Api.V2.Admin.PackageDetails details) =>
        details.KnownSince ?? throw new InvalidOperationException(
            $"Package '{details.PackageId}' is missing the required known_since timestamp.");

    /// <inheritdoc />
    public async Task<PackageArchive> GetPackageAsync(
        string packageId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);

        using var activity = ActivityHelper.StartActivity<AdminClient>(ActivitySource);
        activity?.SetTag("packageId", packageId);

        var response = await _packageService.GetPackageAsync(
            new GetPackageRequest { PackageId = packageId },
            headers: await GetHeadersAsync(cancellationToken),
            deadline: GetDeadline(),
            cancellationToken: cancellationToken);

        return new PackageArchive(
            response.ArchivePayload.ToByteArray(),
            response.Hash,
            response.HashFunction.ToString());
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<VettedPackage>> ListVettedPackagesAsync(
        IEnumerable<string>? packageNamePrefixes = null,
        CancellationToken cancellationToken = default)
    {
        using var activity = ActivityHelper.StartActivity<AdminClient>(ActivitySource);

        var request = new ListVettedPackagesRequest();

        var prefixes = packageNamePrefixes?.ToList();
        if (prefixes is { Count: > 0 })
        {
            request.PackageMetadataFilter = new PackageMetadataFilter();
            request.PackageMetadataFilter.PackageNamePrefixes.AddRange(prefixes);
        }

        var vettedPackages = new List<VettedPackage>();

        do
        {
            var response = await _packageService.ListVettedPackagesAsync(
                request,
                headers: await GetHeadersAsync(cancellationToken),
                deadline: GetDeadline(),
                cancellationToken: cancellationToken);

            vettedPackages.AddRange(response.VettedPackages.SelectMany(group =>
                group.Packages.Select(p => new VettedPackage(
                    p.PackageId,
                    p.PackageName,
                    p.PackageVersion,
                    group.ParticipantId,
                    group.SynchronizerId))));

            if (response.NextPageToken.Length > 0 && response.NextPageToken == request.PageToken)
                throw new InvalidOperationException(
                    $"ListVettedPackages pagination is not progressing: the server returned the page token '{response.NextPageToken}' that was just sent.");

            request.PageToken = response.NextPageToken;
        } while (request.PageToken.Length > 0);

        return vettedPackages;
    }

    /// <inheritdoc />
    public async Task UploadDarAsync(
        byte[] darFile,
        string? submissionId = null,
        CancellationToken cancellationToken = default)
    {
        ThrowIfNullOrEmpty(darFile);

        using var activity = ActivityHelper.StartActivity<AdminClient>(ActivitySource);
        activity?.SetTag("submissionId", submissionId);

        LogUploadingDar(Logger, darFile.Length);

        var request = new UploadDarFileRequest
        {
            DarFile = ByteString.CopyFrom(darFile),
            SubmissionId = submissionId ?? string.Empty
        };

        await _packageManagementService.UploadDarFileAsync(
            request,
            headers: await GetHeadersAsync(cancellationToken),
            deadline: GetDeadline(),
            cancellationToken: cancellationToken);

        LogDarUploaded(Logger, darFile.Length);
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Uploading DAR file ({DarSize} bytes)")]
    private static partial void LogUploadingDar(ILogger logger, int darSize);

    [LoggerMessage(Level = LogLevel.Information, Message = "DAR file uploaded ({DarSize} bytes)")]
    private static partial void LogDarUploaded(ILogger logger, int darSize);

    /// <inheritdoc />
    public async Task ValidateDarAsync(
        byte[] darFile,
        CancellationToken cancellationToken = default)
    {
        ThrowIfNullOrEmpty(darFile);

        using var activity = ActivityHelper.StartActivity<AdminClient>(ActivitySource);

        var request = new ValidateDarFileRequest
        {
            DarFile = ByteString.CopyFrom(darFile)
        };

        await _packageManagementService.ValidateDarFileAsync(
            request,
            headers: await GetHeadersAsync(cancellationToken),
            deadline: GetDeadline(),
            cancellationToken: cancellationToken);

        LogDarValidated(Logger, darFile.Length);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "DAR file validated ({DarSize} bytes)")]
    private static partial void LogDarValidated(ILogger logger, int darSize);

    private static void ThrowIfNullOrEmpty(byte[] darFile)
    {
        ArgumentNullException.ThrowIfNull(darFile);
        if (darFile.Length == 0)
            throw new ArgumentException("DAR file must not be empty.", nameof(darFile));
    }

    internal static Right ToProtoRight(UserRight right) => right switch
    {
        UserRight.ActAs actAs => new Right { CanActAs = new Right.Types.CanActAs { Party = actAs.Party } },
        UserRight.ReadAs readAs => new Right { CanReadAs = new Right.Types.CanReadAs { Party = readAs.Party } },
        UserRight.ParticipantAdmin => new Right { ParticipantAdmin = new Right.Types.ParticipantAdmin() },
        UserRight.IdentityProviderAdmin => new Right { IdentityProviderAdmin = new Right.Types.IdentityProviderAdmin() },
        _ => throw new NotSupportedException($"Unknown right type: {right.GetType().Name}")
    };

    internal static UserDetails FromProtoUser(User user) =>
        new(user.Id, user.PrimaryParty, Array.Empty<UserRight>());

    private Task<Metadata?> GetHeadersAsync(CancellationToken cancellationToken) =>
        AuthHeaderHelper.GetHeadersAsync(_tokenProvider, cancellationToken);

    private DateTime? GetDeadline()
    {
        if (_options.Timeout == null)
            return null;

        return DateTime.UtcNow.Add(_options.Timeout.Value);
    }

    /// <summary>
    /// Releases the underlying gRPC channel.
    /// </summary>
    public void Dispose()
    {
        _channel.Dispose();
    }
}
