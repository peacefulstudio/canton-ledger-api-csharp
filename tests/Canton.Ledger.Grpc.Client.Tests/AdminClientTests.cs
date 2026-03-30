// Copyright (c) 2026 Peaceful Studio OÜ. All rights reserved.

using Com.Daml.Ledger.Api.V2.Admin;
using FluentAssertions;
using Grpc.Core;
using Grpc.Net.Client;
using NSubstitute;
using Xunit;

namespace Canton.Ledger.Grpc.Client.Tests;

public class AdminClientTests
{
    private readonly LedgerClientOptions _options;
    private readonly GrpcChannel _channel;
    private readonly PartyManagementService.PartyManagementServiceClient _partyService;
    private readonly UserManagementService.UserManagementServiceClient _userService;

    public AdminClientTests()
    {
        _options = new LedgerClientOptions
        {
            GrpcAddress = "https://localhost:5001",
            AccessToken = "test-token"
        };

        // Create a real channel (won't be used since we mock service clients)
        _channel = GrpcChannel.ForAddress(_options.GrpcAddress);

        // Create a mock CallInvoker and use ForPartsOf to create partial mocks of the service clients
        var callInvoker = Substitute.For<CallInvoker>();
        _partyService = Substitute.ForPartsOf<PartyManagementService.PartyManagementServiceClient>(callInvoker);
        _userService = Substitute.ForPartsOf<UserManagementService.UserManagementServiceClient>(callInvoker);
    }

    private AdminClient CreateClient() => new(_options, _channel, _partyService, _userService);

    [Fact]
    public async Task get_participant_id_returns_id_from_response()
    {
        var expectedId = "participant::test-participant";
        var response = new GetParticipantIdResponse { ParticipantId = expectedId };

        _partyService
            .GetParticipantIdAsync(
                Arg.Any<GetParticipantIdRequest>(),
                Arg.Any<Metadata>(),
                Arg.Any<DateTime?>(),
                Arg.Any<CancellationToken>())
            .Returns(new AsyncUnaryCall<GetParticipantIdResponse>(
                Task.FromResult(response),
                Task.FromResult(new Metadata()),
                () => Status.DefaultSuccess,
                () => new Metadata(),
                () => { }));

        var client = CreateClient();
        var result = await client.GetParticipantIdAsync();

        result.Should().Be(expectedId);
    }

    [Fact]
    public async Task allocate_party_returns_party_details()
    {
        var partyId = "party::alice";
        var response = new AllocatePartyResponse
        {
            PartyDetails = new Com.Daml.Ledger.Api.V2.Admin.PartyDetails
            {
                Party = partyId,
                IsLocal = true
            }
        };

        _partyService
            .AllocatePartyAsync(
                Arg.Any<AllocatePartyRequest>(),
                Arg.Any<Metadata>(),
                Arg.Any<DateTime?>(),
                Arg.Any<CancellationToken>())
            .Returns(new AsyncUnaryCall<AllocatePartyResponse>(
                Task.FromResult(response),
                Task.FromResult(new Metadata()),
                () => Status.DefaultSuccess,
                () => new Metadata(),
                () => { }));

        var client = CreateClient();
        var result = await client.AllocatePartyAsync("alice");

        result.Party.Should().Be(partyId);
        result.IsLocal.Should().BeTrue();
    }

    [Fact]
    public async Task get_parties_returns_list_of_party_details()
    {
        var response = new GetPartiesResponse();
        response.PartyDetails.Add(new Com.Daml.Ledger.Api.V2.Admin.PartyDetails
        {
            Party = "party::alice",
            IsLocal = true
        });
        response.PartyDetails.Add(new Com.Daml.Ledger.Api.V2.Admin.PartyDetails
        {
            Party = "party::bob",
            IsLocal = false
        });

        _partyService
            .GetPartiesAsync(
                Arg.Any<GetPartiesRequest>(),
                Arg.Any<Metadata>(),
                Arg.Any<DateTime?>(),
                Arg.Any<CancellationToken>())
            .Returns(new AsyncUnaryCall<GetPartiesResponse>(
                Task.FromResult(response),
                Task.FromResult(new Metadata()),
                () => Status.DefaultSuccess,
                () => new Metadata(),
                () => { }));

        var client = CreateClient();
        var result = await client.GetPartiesAsync(["party::alice", "party::bob"]);

        result.Should().HaveCount(2);
        result[0].Party.Should().Be("party::alice");
        result[0].IsLocal.Should().BeTrue();
        result[1].Party.Should().Be("party::bob");
        result[1].IsLocal.Should().BeFalse();
    }

    [Fact]
    public async Task list_known_parties_returns_paginated_results()
    {
        var response = new ListKnownPartiesResponse();
        response.PartyDetails.Add(new Com.Daml.Ledger.Api.V2.Admin.PartyDetails
        {
            Party = "party::alice",
            IsLocal = true
        });

        _partyService
            .ListKnownPartiesAsync(
                Arg.Any<ListKnownPartiesRequest>(),
                Arg.Any<Metadata>(),
                Arg.Any<DateTime?>(),
                Arg.Any<CancellationToken>())
            .Returns(new AsyncUnaryCall<ListKnownPartiesResponse>(
                Task.FromResult(response),
                Task.FromResult(new Metadata()),
                () => Status.DefaultSuccess,
                () => new Metadata(),
                () => { }));

        var client = CreateClient();
        var result = await client.ListKnownPartiesAsync(pageSize: 50);

        result.Should().ContainSingle();
        result[0].Party.Should().Be("party::alice");
    }

    [Fact]
    public async Task create_user_returns_user_details()
    {
        var response = new CreateUserResponse
        {
            User = new User
            {
                Id = "test-user",
                PrimaryParty = "party::alice"
            }
        };

        _userService
            .CreateUserAsync(
                Arg.Any<CreateUserRequest>(),
                Arg.Any<Metadata>(),
                Arg.Any<DateTime?>(),
                Arg.Any<CancellationToken>())
            .Returns(new AsyncUnaryCall<CreateUserResponse>(
                Task.FromResult(response),
                Task.FromResult(new Metadata()),
                () => Status.DefaultSuccess,
                () => new Metadata(),
                () => { }));

        var client = CreateClient();
        var result = await client.CreateUserAsync("test-user", "party::alice");

        result.UserId.Should().Be("test-user");
        result.PrimaryParty.Should().Be("party::alice");
    }

    [Fact]
    public async Task create_user_with_rights_sends_rights_in_request()
    {
        var response = new CreateUserResponse
        {
            User = new User
            {
                Id = "test-user",
                PrimaryParty = "party::alice"
            }
        };

        CreateUserRequest? capturedRequest = null;
        _userService
            .CreateUserAsync(
                Arg.Do<CreateUserRequest>(r => capturedRequest = r),
                Arg.Any<Metadata>(),
                Arg.Any<DateTime?>(),
                Arg.Any<CancellationToken>())
            .Returns(new AsyncUnaryCall<CreateUserResponse>(
                Task.FromResult(response),
                Task.FromResult(new Metadata()),
                () => Status.DefaultSuccess,
                () => new Metadata(),
                () => { }));

        var client = CreateClient();
        var rights = new List<UserRight>
        {
            new UserRight.ActAs("party::alice"),
            new UserRight.ReadAs("party::bob"),
            new UserRight.ParticipantAdmin()
        };

        await client.CreateUserAsync("test-user", "party::alice", rights);

        capturedRequest.Should().NotBeNull();
        capturedRequest!.Rights.Should().HaveCount(3);
    }

    [Fact]
    public async Task get_user_returns_user_when_found()
    {
        var response = new GetUserResponse
        {
            User = new User
            {
                Id = "test-user",
                PrimaryParty = "party::alice"
            }
        };

        _userService
            .GetUserAsync(
                Arg.Any<GetUserRequest>(),
                Arg.Any<Metadata>(),
                Arg.Any<DateTime?>(),
                Arg.Any<CancellationToken>())
            .Returns(new AsyncUnaryCall<GetUserResponse>(
                Task.FromResult(response),
                Task.FromResult(new Metadata()),
                () => Status.DefaultSuccess,
                () => new Metadata(),
                () => { }));

        var client = CreateClient();
        var result = await client.GetUserAsync("test-user");

        result.Should().NotBeNull();
        result!.UserId.Should().Be("test-user");
    }

    [Fact]
    public async Task get_user_returns_null_when_not_found()
    {
        _userService
            .GetUserAsync(
                Arg.Any<GetUserRequest>(),
                Arg.Any<Metadata>(),
                Arg.Any<DateTime?>(),
                Arg.Any<CancellationToken>())
            .Returns<AsyncUnaryCall<GetUserResponse>>(_ =>
                throw new RpcException(new Status(StatusCode.NotFound, "User not found")));

        var client = CreateClient();
        var result = await client.GetUserAsync("non-existent-user");

        result.Should().BeNull();
    }

    [Fact]
    public async Task grant_user_rights_calls_service()
    {
        var response = new GrantUserRightsResponse();

        GrantUserRightsRequest? capturedRequest = null;
        _userService
            .GrantUserRightsAsync(
                Arg.Do<GrantUserRightsRequest>(r => capturedRequest = r),
                Arg.Any<Metadata>(),
                Arg.Any<DateTime?>(),
                Arg.Any<CancellationToken>())
            .Returns(new AsyncUnaryCall<GrantUserRightsResponse>(
                Task.FromResult(response),
                Task.FromResult(new Metadata()),
                () => Status.DefaultSuccess,
                () => new Metadata(),
                () => { }));

        var client = CreateClient();
        await client.GrantUserRightsAsync("test-user", [new UserRight.ActAs("party::alice")]);

        capturedRequest.Should().NotBeNull();
        capturedRequest!.UserId.Should().Be("test-user");
        capturedRequest.Rights.Should().ContainSingle();
    }

    [Fact]
    public async Task revoke_user_rights_calls_service()
    {
        var response = new RevokeUserRightsResponse();

        RevokeUserRightsRequest? capturedRequest = null;
        _userService
            .RevokeUserRightsAsync(
                Arg.Do<RevokeUserRightsRequest>(r => capturedRequest = r),
                Arg.Any<Metadata>(),
                Arg.Any<DateTime?>(),
                Arg.Any<CancellationToken>())
            .Returns(new AsyncUnaryCall<RevokeUserRightsResponse>(
                Task.FromResult(response),
                Task.FromResult(new Metadata()),
                () => Status.DefaultSuccess,
                () => new Metadata(),
                () => { }));

        var client = CreateClient();
        await client.RevokeUserRightsAsync("test-user", [new UserRight.ReadAs("party::bob")]);

        capturedRequest.Should().NotBeNull();
        capturedRequest!.UserId.Should().Be("test-user");
        capturedRequest.Rights.Should().ContainSingle();
    }

    [Fact]
    public async Task list_users_returns_paginated_results()
    {
        var response = new ListUsersResponse();
        response.Users.Add(new User { Id = "user1", PrimaryParty = "party::alice" });
        response.Users.Add(new User { Id = "user2", PrimaryParty = "party::bob" });

        _userService
            .ListUsersAsync(
                Arg.Any<ListUsersRequest>(),
                Arg.Any<Metadata>(),
                Arg.Any<DateTime?>(),
                Arg.Any<CancellationToken>())
            .Returns(new AsyncUnaryCall<ListUsersResponse>(
                Task.FromResult(response),
                Task.FromResult(new Metadata()),
                () => Status.DefaultSuccess,
                () => new Metadata(),
                () => { }));

        var client = CreateClient();
        var result = await client.ListUsersAsync(pageSize: 50);

        result.Should().HaveCount(2);
        result[0].UserId.Should().Be("user1");
        result[1].UserId.Should().Be("user2");
    }

    [Fact]
    public void to_proto_right_converts_act_as()
    {
        var right = new UserRight.ActAs("party::alice");
        var protoRight = AdminClient.ToProtoRight(right);

        protoRight.CanActAs.Should().NotBeNull();
        protoRight.CanActAs.Party.Should().Be("party::alice");
    }

    [Fact]
    public void to_proto_right_converts_read_as()
    {
        var right = new UserRight.ReadAs("party::bob");
        var protoRight = AdminClient.ToProtoRight(right);

        protoRight.CanReadAs.Should().NotBeNull();
        protoRight.CanReadAs.Party.Should().Be("party::bob");
    }

    [Fact]
    public void to_proto_right_converts_participant_admin()
    {
        var protoRight = AdminClient.ToProtoRight(new UserRight.ParticipantAdmin());

        protoRight.ParticipantAdmin.Should().NotBeNull();
    }

    [Fact]
    public void to_proto_right_converts_identity_provider_admin()
    {
        var protoRight = AdminClient.ToProtoRight(new UserRight.IdentityProviderAdmin());

        protoRight.IdentityProviderAdmin.Should().NotBeNull();
    }

    [Fact]
    public void from_proto_user_converts_correctly()
    {
        var protoUser = new User
        {
            Id = "test-user",
            PrimaryParty = "party::alice"
        };

        var userDetails = AdminClient.FromProtoUser(protoUser);

        userDetails.UserId.Should().Be("test-user");
        userDetails.PrimaryParty.Should().Be("party::alice");
        userDetails.Rights.Should().BeEmpty();
    }

    [Fact]
    public void dispose_does_not_throw()
    {
        var client = CreateClient();

        var action = () => client.Dispose();

        action.Should().NotThrow();
    }
}
