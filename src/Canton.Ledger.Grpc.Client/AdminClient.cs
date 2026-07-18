// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics;
using Canton.Ledger.Kernel.Authentication;
using Canton.Ledger.Kernel.Telemetry;
using Com.Daml.Ledger.Api.V2;
using Com.Daml.Ledger.Api.V2.Admin;
using Google.Protobuf;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using WireHashFunction = Com.Daml.Ledger.Api.V2.HashFunction;

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
    public static string ActivitySourceName => LedgerActivitySource.NameFor<AdminClient>();

    internal const int MaxPagesPerPaginatedCall = 10_000;

    private static readonly ActivitySource ActivitySource = LedgerActivitySource.Create<AdminClient>();

    private readonly GrpcChannel _channel;
    private readonly PartyManagementService.PartyManagementServiceClient _partyService;
    private readonly UserManagementService.UserManagementServiceClient _userService;
    private readonly PackageManagementService.PackageManagementServiceClient _packageManagementService;
    private readonly PackageService.PackageServiceClient _packageService;
    private readonly LedgerClientOptions _options;
    private readonly ITokenProvider? _tokenProvider;
    private readonly LedgerCallInvoker _invoker;
    private readonly ILogger<AdminClient> _logger;

    /// <summary>
    /// Creates a new AdminClient with the specified options and token provider.
    /// Logs are discarded unless a <paramref name="logger"/> is supplied.
    /// </summary>
    public AdminClient(IOptions<LedgerClientOptions> options, ITokenProvider tokenProvider, ILogger<AdminClient>? logger = null)
        : this(options.Value, tokenProvider, logger)
    {
    }

    /// <summary>
    /// Creates a new AdminClient with the specified options and token provider.
    /// Logs are discarded unless a <paramref name="logger"/> is supplied.
    /// </summary>
    public AdminClient(LedgerClientOptions options, ITokenProvider tokenProvider, ILogger<AdminClient>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(tokenProvider);

        _options = options;
        _tokenProvider = tokenProvider;
        _logger = logger ?? NullLogger<AdminClient>.Instance;
        _invoker = new LedgerCallInvoker(_options, _tokenProvider);

        _channel = LedgerGrpcChannel.Create(_options);

        _partyService = new PartyManagementService.PartyManagementServiceClient(_channel);
        _userService = new UserManagementService.UserManagementServiceClient(_channel);
        _packageManagementService = new PackageManagementService.PackageManagementServiceClient(_channel);
        _packageService = new PackageService.PackageServiceClient(_channel);

        CallContextHelper.LogStartupDiagnostics(
            _logger, _tokenProvider, _options.GrpcAddress, nameof(AdminClient), "AddAdminClient");
    }

    internal AdminClient(
        LedgerClientOptions options,
        GrpcChannel channel,
        PartyManagementService.PartyManagementServiceClient partyService,
        UserManagementService.UserManagementServiceClient userService,
        ITokenProvider? tokenProvider = null,
        PackageManagementService.PackageManagementServiceClient? packageManagementService = null,
        PackageService.PackageServiceClient? packageService = null,
        ILogger<AdminClient>? logger = null)
    {
        _options = options;
        _channel = channel;
        _partyService = partyService;
        _userService = userService;
        _packageManagementService = packageManagementService ?? new PackageManagementService.PackageManagementServiceClient(channel);
        _packageService = packageService ?? new PackageService.PackageServiceClient(channel);
        _tokenProvider = tokenProvider;
        _logger = logger ?? NullLogger<AdminClient>.Instance;
        _invoker = new LedgerCallInvoker(options, tokenProvider);
    }

    /// <inheritdoc />
    public Task<string> GetParticipantIdAsync(CancellationToken cancellationToken = default) =>
        _invoker.InvokeTracedAsync<AdminClient, GetParticipantIdResponse, string>(
            ActivitySource,
            PartyManagementService.Descriptor,
            "GetParticipantId",
            (headers, deadline, token) => _partyService.GetParticipantIdAsync(new GetParticipantIdRequest(), headers, deadline, token),
            response => response.ParticipantId,
            cancellationToken);

    /// <inheritdoc />
    public async Task<PartyDetails> AllocatePartyAsync(
        string partyIdHint,
        string? synchronizerId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(partyIdHint);

        LogAllocatingParty(_logger, partyIdHint);

        var request = new AllocatePartyRequest { PartyIdHint = partyIdHint };
        if (!string.IsNullOrEmpty(synchronizerId))
            request.SynchronizerId = synchronizerId;

        var details = await _invoker.InvokeTracedAsync<AdminClient, AllocatePartyResponse, PartyDetails>(
            ActivitySource,
            PartyManagementService.Descriptor,
            "AllocateParty",
            (headers, deadline, token) => _partyService.AllocatePartyAsync(request, headers, deadline, token),
            response => new PartyDetails(response.PartyDetails.Party, response.PartyDetails.IsLocal),
            cancellationToken,
            configureActivity: activity => activity?.SetTag(LedgerClientActivityTags.CantonPartyIdHint, partyIdHint)).ConfigureAwait(false);

        LogPartyAllocated(_logger, details.Party);
        return details;
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Allocating party with hint: {PartyIdHint}")]
    private static partial void LogAllocatingParty(ILogger logger, string partyIdHint);

    [LoggerMessage(Level = LogLevel.Information, Message = "Party allocated: {PartyId}")]
    private static partial void LogPartyAllocated(ILogger logger, string partyId);

    /// <inheritdoc />
    public Task<IReadOnlyList<PartyDetails>> GetPartiesAsync(
        IEnumerable<string> partyIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(partyIds);

        var request = new GetPartiesRequest();
        request.Parties.AddRange(partyIds);

        return _invoker.InvokeTracedAsync<AdminClient, GetPartiesResponse, IReadOnlyList<PartyDetails>>(
            ActivitySource,
            PartyManagementService.Descriptor,
            "GetParties",
            (headers, deadline, token) => _partyService.GetPartiesAsync(request, headers, deadline, token),
            response => response.PartyDetails.Select(p => new PartyDetails(p.Party, p.IsLocal)).ToList(),
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<PartyDetails>> ListKnownPartiesAsync(
        int pageSize = 100,
        CancellationToken cancellationToken = default)
    {
        var request = new ListKnownPartiesRequest { PageSize = pageSize };

        return _invoker.ExecuteTracedAsync<AdminClient, IReadOnlyList<PartyDetails>>(
            ActivitySource,
            PartyManagementService.Descriptor,
            "ListKnownParties",
            (activity, token) => FetchAllPagesAsync(
                activity,
                "ListKnownParties",
                async pageToken =>
                {
                    request.PageToken = pageToken;
                    return await _invoker.InvokeAsync(
                        (headers, deadline, callToken) => _partyService.ListKnownPartiesAsync(request, headers, deadline, callToken),
                        token).ConfigureAwait(false);
                },
                response => response.NextPageToken,
                response => response.PartyDetails.Select(p => new PartyDetails(p.Party, p.IsLocal))),
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task<UserDetails> CreateUserAsync(
        string userId,
        string primaryParty,
        IEnumerable<UserRight>? rights = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        ArgumentNullException.ThrowIfNull(primaryParty);

        LogCreatingUser(_logger, userId);

        var user = new User { Id = userId, PrimaryParty = primaryParty };
        var request = new CreateUserRequest { User = user };
        if (rights != null)
            request.Rights.AddRange(rights.Select(ToProtoRight));

        var details = await _invoker.InvokeTracedAsync<AdminClient, CreateUserResponse, UserDetails>(
            ActivitySource,
            UserManagementService.Descriptor,
            "CreateUser",
            (headers, deadline, token) => _userService.CreateUserAsync(request, headers, deadline, token),
            response => FromProtoUser(response.User),
            cancellationToken,
            configureActivity: activity => activity?.SetTag(LedgerClientActivityTags.CantonUserId, userId)).ConfigureAwait(false);

        LogUserCreated(_logger, userId);
        return details;
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
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        try
        {
            return await _invoker.InvokeTracedAsync<AdminClient, GetUserResponse, UserDetails?>(
                ActivitySource,
                UserManagementService.Descriptor,
                "GetUser",
                (headers, deadline, token) => _userService.GetUserAsync(new GetUserRequest { UserId = userId }, headers, deadline, token),
                response => FromProtoUser(response.User),
                cancellationToken,
                isExpectedFailure: IsNotFound).ConfigureAwait(false);
        }
        catch (RpcException ex) when (IsNotFound(ex))
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
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        ArgumentNullException.ThrowIfNull(rights);

        var request = new GrantUserRightsRequest { UserId = userId };
        request.Rights.AddRange(rights.Select(ToProtoRight));

        await _invoker.InvokeTracedAsync<AdminClient, GrantUserRightsResponse>(
            ActivitySource,
            UserManagementService.Descriptor,
            "GrantUserRights",
            (headers, deadline, token) => _userService.GrantUserRightsAsync(request, headers, deadline, token),
            cancellationToken,
            configureActivity: activity => activity?.SetTag(LedgerClientActivityTags.CantonUserId, userId)).ConfigureAwait(false);

        LogRightsGranted(_logger, userId);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Rights granted to user {UserId}")]
    private static partial void LogRightsGranted(ILogger logger, string userId);

    /// <inheritdoc />
    public async Task RevokeUserRightsAsync(
        string userId,
        IEnumerable<UserRight> rights,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        ArgumentNullException.ThrowIfNull(rights);

        var request = new RevokeUserRightsRequest { UserId = userId };
        request.Rights.AddRange(rights.Select(ToProtoRight));

        await _invoker.InvokeTracedAsync<AdminClient, RevokeUserRightsResponse>(
            ActivitySource,
            UserManagementService.Descriptor,
            "RevokeUserRights",
            (headers, deadline, token) => _userService.RevokeUserRightsAsync(request, headers, deadline, token),
            cancellationToken,
            configureActivity: activity => activity?.SetTag(LedgerClientActivityTags.CantonUserId, userId)).ConfigureAwait(false);

        LogRightsRevoked(_logger, userId);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Rights revoked from user {UserId}")]
    private static partial void LogRightsRevoked(ILogger logger, string userId);

    /// <inheritdoc />
    public async Task<IReadOnlyList<UserRight>?> ListUserRightsAsync(
        string userId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(userId);

        try
        {
            return await _invoker.InvokeTracedAsync<AdminClient, ListUserRightsResponse, IReadOnlyList<UserRight>?>(
                ActivitySource,
                UserManagementService.Descriptor,
                "ListUserRights",
                (headers, deadline, token) => _userService.ListUserRightsAsync(new ListUserRightsRequest { UserId = userId }, headers, deadline, token),
                response => response.Rights.Select(FromProtoRight).ToList(),
                cancellationToken,
                configureActivity: activity => activity?.SetTag(LedgerClientActivityTags.CantonUserId, userId),
                isExpectedFailure: IsNotFound).ConfigureAwait(false);
        }
        catch (RpcException ex) when (IsNotFound(ex))
        {
            return null;
        }
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<UserDetails>> ListUsersAsync(
        int pageSize = 100,
        CancellationToken cancellationToken = default)
    {
        var request = new ListUsersRequest { PageSize = pageSize };

        return _invoker.ExecuteTracedAsync<AdminClient, IReadOnlyList<UserDetails>>(
            ActivitySource,
            UserManagementService.Descriptor,
            "ListUsers",
            (activity, token) => FetchAllPagesAsync(
                activity,
                "ListUsers",
                async pageToken =>
                {
                    request.PageToken = pageToken;
                    return await _invoker.InvokeAsync(
                        (headers, deadline, callToken) => _userService.ListUsersAsync(request, headers, deadline, callToken),
                        token).ConfigureAwait(false);
                },
                response => response.NextPageToken,
                response => response.Users.Select(FromProtoUser)),
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<PackageDetails>> ListKnownPackagesAsync(
        CancellationToken cancellationToken = default) =>
        _invoker.InvokeTracedAsync<AdminClient, ListKnownPackagesResponse, IReadOnlyList<PackageDetails>>(
            ActivitySource,
            PackageManagementService.Descriptor,
            "ListKnownPackages",
            (headers, deadline, token) => _packageManagementService.ListKnownPackagesAsync(new ListKnownPackagesRequest(), headers, deadline, token),
            response => response.PackageDetails
                .Select(p => new PackageDetails(
                    p.PackageId,
                    p.Name,
                    p.Version,
                    p.PackageSize <= long.MaxValue
                        ? (long)p.PackageSize
                        : throw new InvalidOperationException(
                            $"Package '{p.PackageId}' reports a size of {p.PackageSize} bytes, which exceeds the supported maximum of {long.MaxValue}."),
                    (p.KnownSince ?? throw new InvalidOperationException(
                        $"Package '{p.PackageId}' is missing the required known_since timestamp.")).ToDateTimeOffset()))
                .ToList(),
            cancellationToken);

    /// <inheritdoc />
    public Task<PackageArchive> GetPackageAsync(
        string packageId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);

        return _invoker.InvokeTracedAsync<AdminClient, GetPackageResponse, PackageArchive>(
            ActivitySource,
            PackageService.Descriptor,
            "GetPackage",
            (headers, deadline, token) => _packageService.GetPackageAsync(new GetPackageRequest { PackageId = packageId }, headers, deadline, token),
            response => new PackageArchive(
                response.ArchivePayload.Memory,
                response.Hash,
                MapHashFunction(response.HashFunction)),
            cancellationToken,
            configureActivity: activity => activity?.SetTag(LedgerClientActivityTags.DamlPackageId, packageId));
    }

    private static HashFunction MapHashFunction(WireHashFunction hashFunction) => hashFunction switch
    {
        WireHashFunction.Sha256 => HashFunction.Sha256,
        _ => HashFunction.Unrecognized,
    };

    /// <inheritdoc />
    public Task<IReadOnlyList<VettedPackage>> ListVettedPackagesAsync(
        IEnumerable<string>? packageNamePrefixes = null,
        CancellationToken cancellationToken = default)
    {
        var request = new ListVettedPackagesRequest();

        var prefixes = packageNamePrefixes?.ToList();
        if (prefixes is { Count: > 0 })
            request.PackageMetadataFilter = new PackageMetadataFilter { PackageNamePrefixes = { prefixes } };

        return _invoker.ExecuteTracedAsync<AdminClient, IReadOnlyList<VettedPackage>>(
            ActivitySource,
            PackageService.Descriptor,
            "ListVettedPackages",
            (activity, token) => FetchAllPagesAsync(
                activity,
                "ListVettedPackages",
                async pageToken =>
                {
                    request.PageToken = pageToken;
                    return await _invoker.InvokeAsync(
                        (headers, deadline, callToken) => _packageService.ListVettedPackagesAsync(request, headers, deadline, callToken),
                        token).ConfigureAwait(false);
                },
                response => response.NextPageToken,
                response => response.VettedPackages.SelectMany(group =>
                    group.Packages.Select(p => new VettedPackage(
                        p.PackageId,
                        p.PackageName,
                        p.PackageVersion,
                        group.ParticipantId,
                        group.SynchronizerId)))),
            cancellationToken);
    }

    private static async Task<IReadOnlyList<TItem>> FetchAllPagesAsync<TResponse, TItem>(
        Activity? activity,
        string grpcMethodName,
        Func<string, Task<TResponse>> fetchPage,
        Func<TResponse, string> readNextPageToken,
        Func<TResponse, IEnumerable<TItem>> readItems)
    {
        var items = new List<TItem>();
        var pageToken = string.Empty;
        var seenPageTokens = new HashSet<string>(StringComparer.Ordinal);
        var fetchedPages = 0;

        do
        {
            var response = await fetchPage(pageToken).ConfigureAwait(false);
            fetchedPages++;
            var nextPageToken = readNextPageToken(response);

            if (nextPageToken.Length > 0 && !seenPageTokens.Add(nextPageToken))
            {
                var error = new InvalidOperationException(
                    $"{grpcMethodName} pagination is not progressing: the server returned the page token '{nextPageToken}' that was already used earlier in this call.");
                activity.RecordException(error);
                throw error;
            }

            items.AddRange(readItems(response));
            pageToken = nextPageToken;

            if (pageToken.Length > 0 && fetchedPages >= MaxPagesPerPaginatedCall)
            {
                var error = new InvalidOperationException(
                    $"{grpcMethodName} pagination did not complete after {MaxPagesPerPaginatedCall} pages; aborting instead of following an unbounded page-token stream.");
                activity.RecordException(error);
                throw error;
            }
        } while (pageToken.Length > 0);

        return items;
    }

    /// <inheritdoc />
    public async Task UploadDarAsync(
        byte[] darFile,
        string? submissionId = null,
        CancellationToken cancellationToken = default)
    {
        ThrowIfNullOrEmpty(darFile);

        LogUploadingDar(_logger, darFile.Length);

        var request = new UploadDarFileRequest
        {
            DarFile = ByteString.CopyFrom(darFile),
            SubmissionId = submissionId ?? string.Empty
        };

        await _invoker.InvokeTracedAsync<AdminClient, UploadDarFileResponse>(
            ActivitySource,
            PackageManagementService.Descriptor,
            "UploadDarFile",
            (headers, deadline, token) => _packageManagementService.UploadDarFileAsync(request, headers, deadline, token),
            cancellationToken,
            configureActivity: activity => activity?.SetTag(LedgerClientActivityTags.CantonSubmissionId, submissionId)).ConfigureAwait(false);

        LogDarUploaded(_logger, darFile.Length);
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

        var request = new ValidateDarFileRequest { DarFile = ByteString.CopyFrom(darFile) };

        await _invoker.InvokeTracedAsync<AdminClient, ValidateDarFileResponse>(
            ActivitySource,
            PackageManagementService.Descriptor,
            "ValidateDarFile",
            (headers, deadline, token) => _packageManagementService.ValidateDarFileAsync(request, headers, deadline, token),
            cancellationToken).ConfigureAwait(false);

        LogDarValidated(_logger, darFile.Length);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "DAR file validated ({DarSize} bytes)")]
    private static partial void LogDarValidated(ILogger logger, int darSize);

    private static bool IsNotFound(RpcException exception) => exception.StatusCode == StatusCode.NotFound;

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
        UserRight.ReadAsAnyParty => new Right { CanReadAsAnyParty = new Right.Types.CanReadAsAnyParty() },
        UserRight.ExecuteAs executeAs => new Right { CanExecuteAs = new Right.Types.CanExecuteAs { Party = executeAs.Party } },
        UserRight.ExecuteAsAnyParty => new Right { CanExecuteAsAnyParty = new Right.Types.CanExecuteAsAnyParty() },
        _ => throw new NotSupportedException($"Unknown right type: {right.GetType().Name}")
    };

    internal static UserRight FromProtoRight(Right right) => right.KindCase switch
    {
        Right.KindOneofCase.ParticipantAdmin => new UserRight.ParticipantAdmin(),
        Right.KindOneofCase.CanActAs => new UserRight.ActAs(right.CanActAs.Party),
        Right.KindOneofCase.CanReadAs => new UserRight.ReadAs(right.CanReadAs.Party),
        Right.KindOneofCase.IdentityProviderAdmin => new UserRight.IdentityProviderAdmin(),
        Right.KindOneofCase.CanReadAsAnyParty => new UserRight.ReadAsAnyParty(),
        Right.KindOneofCase.CanExecuteAs => new UserRight.ExecuteAs(right.CanExecuteAs.Party),
        Right.KindOneofCase.CanExecuteAsAnyParty => new UserRight.ExecuteAsAnyParty(),
        _ => throw new NotSupportedException($"Unknown right kind: {right.KindCase}")
    };

    internal static UserDetails FromProtoUser(User user) =>
        new(user.Id, user.PrimaryParty);

    /// <summary>
    /// Releases the underlying gRPC channel.
    /// </summary>
    public void Dispose()
    {
        _channel.Dispose();
    }
}
