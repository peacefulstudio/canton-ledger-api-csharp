// Copyright (c) 2026 Peaceful Studio. All rights reserved.

using System.Diagnostics;
using Com.Daml.Ledger.Api.V2.Admin;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Canton.Ledger.Grpc.Client;

/// <summary>
/// Implementation of the Canton participant admin client using gRPC.
/// </summary>
public sealed partial class AdminClient : IAdminClient
{
    private static readonly ActivitySource ActivitySource = new(typeof(AdminClient).AssemblyQualifiedName!);
    private static readonly ILogger<AdminClient> Logger = LoggerFactory.Create<AdminClient>();

    private readonly GrpcChannel _channel;
    private readonly PartyManagementService.PartyManagementServiceClient _partyService;
    private readonly UserManagementService.UserManagementServiceClient _userService;
    private readonly LedgerClientOptions _options;

    /// <summary>
    /// Creates a new AdminClient with the specified options.
    /// </summary>
    public AdminClient(IOptions<LedgerClientOptions> options)
        : this(options.Value)
    {
    }

    /// <summary>
    /// Creates a new AdminClient with the specified options.
    /// </summary>
    public AdminClient(LedgerClientOptions options)
    {
        _options = options;

        _channel = GrpcChannel.ForAddress(_options.GrpcAddress, new GrpcChannelOptions
        {
            MaxReceiveMessageSize = _options.MaxMessageSize,
            MaxSendMessageSize = _options.MaxMessageSize
        });

        _partyService = new PartyManagementService.PartyManagementServiceClient(_channel);
        _userService = new UserManagementService.UserManagementServiceClient(_channel);

        LogInitialized(Logger, _options.GrpcAddress);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "AdminClient initialized with endpoint {Endpoint}")]

    private static partial void LogInitialized(ILogger logger, string endpoint);
    /// <summary>
    /// Creates a new AdminClient with injected gRPC channel and service clients.
    /// This constructor is intended for testing scenarios.
    /// </summary>
    internal AdminClient(
        LedgerClientOptions options,
        GrpcChannel channel,
        PartyManagementService.PartyManagementServiceClient partyService,
        UserManagementService.UserManagementServiceClient userService)
    {
        _options = options;
        _channel = channel;
        _partyService = partyService;
        _userService = userService;
    }

    /// <inheritdoc />
    public async Task<string> GetParticipantIdAsync(CancellationToken cancellationToken = default)
    {
        using var activity = ActivityHelper.StartActivity<AdminClient>(ActivitySource);

        var response = await _partyService.GetParticipantIdAsync(
            new GetParticipantIdRequest(),
            headers: GetHeaders(),
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
            headers: GetHeaders(),
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
            headers: GetHeaders(),
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
            headers: GetHeaders(),
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
            headers: GetHeaders(),
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
                headers: GetHeaders(),
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
            headers: GetHeaders(),
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
            headers: GetHeaders(),
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
            headers: GetHeaders(),
            deadline: GetDeadline(),
            cancellationToken: cancellationToken);

        return response.Users.Select(FromProtoUser).ToList();
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

    private Metadata? GetHeaders()
    {
        if (string.IsNullOrEmpty(_options.AccessToken))
            return null;

        return new Metadata
        {
            { "Authorization", $"Bearer {_options.AccessToken}" }
        };
    }

    private DateTime? GetDeadline()
    {
        if (_options.Timeout == null)
            return null;

        return DateTime.UtcNow.Add(_options.Timeout.Value);
    }

    public void Dispose()
    {
        _channel.Dispose();
        ActivitySource.Dispose();
    }
}
