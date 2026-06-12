// Copyright (c) 2026 Peaceful Studio OÜ. All rights reserved.

using System.Diagnostics;
using Canton.Ledger.Auth;
using Com.Daml.Ledger.Api.V2;
using Com.Daml.Ledger.Api.V2.Admin;
using FluentAssertions;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
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
    private readonly PackageManagementService.PackageManagementServiceClient _packageManagementService;
    private readonly PackageService.PackageServiceClient _packageService;
    private readonly ITokenProvider _tokenProvider = new StaticTokenProvider("test-token");

    public AdminClientTests()
    {
        _options = new LedgerClientOptions
        {
            GrpcAddress = "https://localhost:5001"
        };

        // Create a real channel (won't be used since we mock service clients)
        _channel = GrpcChannel.ForAddress(_options.GrpcAddress);

        // Create a mock CallInvoker and use ForPartsOf to create partial mocks of the service clients
        var callInvoker = Substitute.For<CallInvoker>();
        _partyService = Substitute.ForPartsOf<PartyManagementService.PartyManagementServiceClient>(callInvoker);
        _userService = Substitute.ForPartsOf<UserManagementService.UserManagementServiceClient>(callInvoker);
        _packageManagementService = Substitute.ForPartsOf<PackageManagementService.PackageManagementServiceClient>(callInvoker);
        _packageService = Substitute.ForPartsOf<PackageService.PackageServiceClient>(callInvoker);
    }

    private AdminClient CreateClient() =>
        new(_options, _channel, _partyService, _userService, _tokenProvider, _packageManagementService, _packageService);

    private static AsyncUnaryCall<TResponse> UnaryResponse<TResponse>(TResponse response) =>
        new(
            Task.FromResult(response),
            Task.FromResult(new Metadata()),
            () => Status.DefaultSuccess,
            () => new Metadata(),
            () => { });

    [Fact]
    public async Task GetParticipantId_returns_id_from_response()
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
        var result = await client.GetParticipantIdAsync(TestContext.Current.CancellationToken);

        result.Should().Be(expectedId);
    }

    [Fact]
    public async Task AllocateParty_returns_PartyDetails()
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
        var result = await client.AllocatePartyAsync("alice", cancellationToken: TestContext.Current.CancellationToken);

        result.Party.Should().Be(partyId);
        result.IsLocal.Should().BeTrue();
    }

    [Fact]
    public async Task GetParties_returns_list_of_PartyDetails()
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
        var result = await client.GetPartiesAsync(["party::alice", "party::bob"], TestContext.Current.CancellationToken);

        result.Should().HaveCount(2);
        result[0].Party.Should().Be("party::alice");
        result[0].IsLocal.Should().BeTrue();
        result[1].Party.Should().Be("party::bob");
        result[1].IsLocal.Should().BeFalse();
    }

    [Fact]
    public async Task ListKnownParties_returns_paginated_results()
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
        var result = await client.ListKnownPartiesAsync(pageSize: 50, cancellationToken: TestContext.Current.CancellationToken);

        result.Should().ContainSingle();
        result[0].Party.Should().Be("party::alice");
    }

    [Fact]
    public async Task CreateUser_returns_UserDetails()
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
        var result = await client.CreateUserAsync("test-user", "party::alice", cancellationToken: TestContext.Current.CancellationToken);

        result.UserId.Should().Be("test-user");
        result.PrimaryParty.Should().Be("party::alice");
    }

    [Fact]
    public async Task CreateUser_with_rights_sends_rights_in_request()
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

        await client.CreateUserAsync("test-user", "party::alice", rights, TestContext.Current.CancellationToken);

        capturedRequest.Should().NotBeNull();
        capturedRequest!.Rights.Should().HaveCount(3);
    }

    [Fact]
    public async Task GetUser_returns_user_when_found()
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
        var result = await client.GetUserAsync("test-user", TestContext.Current.CancellationToken);

        result.Should().NotBeNull();
        result!.UserId.Should().Be("test-user");
    }

    [Fact]
    public async Task GetUser_returns_null_when_not_found()
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
        var result = await client.GetUserAsync("non-existent-user", TestContext.Current.CancellationToken);

        result.Should().BeNull();
    }

    [Fact]
    public async Task GrantUserRights_calls_service()
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
        await client.GrantUserRightsAsync("test-user", [new UserRight.ActAs("party::alice")], TestContext.Current.CancellationToken);

        capturedRequest.Should().NotBeNull();
        capturedRequest!.UserId.Should().Be("test-user");
        capturedRequest.Rights.Should().ContainSingle();
    }

    [Fact]
    public async Task RevokeUserRights_calls_service()
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
        await client.RevokeUserRightsAsync("test-user", [new UserRight.ReadAs("party::bob")], TestContext.Current.CancellationToken);

        capturedRequest.Should().NotBeNull();
        capturedRequest!.UserId.Should().Be("test-user");
        capturedRequest.Rights.Should().ContainSingle();
    }

    [Fact]
    public async Task ListUsers_returns_paginated_results()
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
        var result = await client.ListUsersAsync(pageSize: 50, cancellationToken: TestContext.Current.CancellationToken);

        result.Should().HaveCount(2);
        result[0].UserId.Should().Be("user1");
        result[1].UserId.Should().Be("user2");
    }

    [Fact]
    public void ToProtoRight_converts_act_as()
    {
        var right = new UserRight.ActAs("party::alice");
        var protoRight = AdminClient.ToProtoRight(right);

        protoRight.CanActAs.Should().NotBeNull();
        protoRight.CanActAs.Party.Should().Be("party::alice");
    }

    [Fact]
    public void ToProtoRight_converts_read_as()
    {
        var right = new UserRight.ReadAs("party::bob");
        var protoRight = AdminClient.ToProtoRight(right);

        protoRight.CanReadAs.Should().NotBeNull();
        protoRight.CanReadAs.Party.Should().Be("party::bob");
    }

    [Fact]
    public void ToProtoRight_converts_participant_admin()
    {
        var protoRight = AdminClient.ToProtoRight(new UserRight.ParticipantAdmin());

        protoRight.ParticipantAdmin.Should().NotBeNull();
    }

    [Fact]
    public void ToProtoRight_converts_identity_provider_admin()
    {
        var protoRight = AdminClient.ToProtoRight(new UserRight.IdentityProviderAdmin());

        protoRight.IdentityProviderAdmin.Should().NotBeNull();
    }

    [Fact]
    public void FromProtoUser_converts_correctly()
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
    public async Task GetParticipantId_throws_when_token_provider_returns_empty_token()
    {
        var emptyProvider = Substitute.For<ITokenProvider>();
        emptyProvider.GetTokenAsync(Arg.Any<CancellationToken>()).Returns("");

        var client = new AdminClient(_options, _channel, _partyService, _userService, emptyProvider);

        var act = () => client.GetParticipantIdAsync(TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*returned an empty token*");
    }

    [Fact]
    public async Task GetParticipantId_throws_when_token_provider_returns_whitespace_token()
    {
        var whitespaceProvider = Substitute.For<ITokenProvider>();
        whitespaceProvider.GetTokenAsync(Arg.Any<CancellationToken>()).Returns("   ");

        var client = new AdminClient(_options, _channel, _partyService, _userService, whitespaceProvider);

        var act = () => client.GetParticipantIdAsync(TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*returned an empty token*");
    }

    [Fact]
    public void Dispose_does_not_throw()
    {
        var client = CreateClient();

        var action = () => client.Dispose();

        action.Should().NotThrow();
    }

    [Fact]
    public async Task Dispose_does_not_disable_tracing_for_subsequent_instances()
    {
        var response = new GetParticipantIdResponse { ParticipantId = "participant::test" };

        var secondCallInvoker = Substitute.For<CallInvoker>();
        var secondPartyService = Substitute.ForPartsOf<PartyManagementService.PartyManagementServiceClient>(secondCallInvoker);
        var secondUserService = Substitute.ForPartsOf<UserManagementService.UserManagementServiceClient>(secondCallInvoker);
        secondPartyService
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

        var startedActivities = new List<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == AdminClient.ActivitySourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStarted = startedActivities.Add
        };
        ActivitySource.AddActivityListener(listener);

        using var firstChannel = GrpcChannel.ForAddress(_options.GrpcAddress);
        var firstClient = new AdminClient(_options, firstChannel, _partyService, _userService, _tokenProvider);
        firstClient.Dispose();

        using var secondChannel = GrpcChannel.ForAddress(_options.GrpcAddress);
        var secondClient = new AdminClient(_options, secondChannel, secondPartyService, secondUserService, _tokenProvider);
        await secondClient.GetParticipantIdAsync(TestContext.Current.CancellationToken);

        startedActivities.Should().NotBeEmpty(
            "disposing one AdminClient must not disable tracing for subsequent instances");
    }

    [Fact]
    public void AdminClient_constructor_does_not_throw_when_ITokenProvider_None()
    {
        using var _ = new AdminClient(_options, ITokenProvider.None);
    }

    [Fact]
    public void AdminClient_constructor_does_not_throw_when_real_provider_registered()
    {
        using var _ = new AdminClient(_options, _tokenProvider);
    }

    [Fact]
    public async Task ListKnownPackages_returns_PackageDetails_with_name_version_id_and_size()
    {
        var knownSince = new DateTimeOffset(2026, 1, 15, 12, 0, 0, TimeSpan.Zero);
        var response = new ListKnownPackagesResponse
        {
            PackageDetails =
            {
                new Com.Daml.Ledger.Api.V2.Admin.PackageDetails
                {
                    PackageId = "pkg-id-1",
                    Name = "my-package",
                    Version = "1.2.3",
                    PackageSize = 12345,
                    KnownSince = Timestamp.FromDateTimeOffset(knownSince)
                }
            }
        };

        _packageManagementService
            .ListKnownPackagesAsync(
                Arg.Any<ListKnownPackagesRequest>(),
                Arg.Any<Metadata>(),
                Arg.Any<DateTime?>(),
                Arg.Any<CancellationToken>())
            .Returns(UnaryResponse(response));

        var client = CreateClient();
        var result = await client.ListKnownPackagesAsync(TestContext.Current.CancellationToken);

        result.Should().ContainSingle();
        result[0].PackageId.Should().Be("pkg-id-1");
        result[0].Name.Should().Be("my-package");
        result[0].Version.Should().Be("1.2.3");
        result[0].PackageSize.Should().Be(12345);
        result[0].KnownSince.Should().Be(knownSince);
    }

    [Fact]
    public async Task GetPackage_returns_PackageArchive_with_payload_hash_and_hash_function()
    {
        var payload = new byte[] { 0x01, 0x02, 0x03, 0x04 };
        var response = new GetPackageResponse
        {
            ArchivePayload = ByteString.CopyFrom(payload),
            Hash = "pkg-id-1",
            HashFunction = HashFunction.Sha256
        };

        GetPackageRequest? capturedRequest = null;
        _packageService
            .GetPackageAsync(
                Arg.Do<GetPackageRequest>(r => capturedRequest = r),
                Arg.Any<Metadata>(),
                Arg.Any<DateTime?>(),
                Arg.Any<CancellationToken>())
            .Returns(UnaryResponse(response));

        var client = CreateClient();
        var result = await client.GetPackageAsync("pkg-id-1", TestContext.Current.CancellationToken);

        result.Payload.Should().Equal(payload);
        result.Hash.Should().Be("pkg-id-1");
        result.HashFunction.Should().Be("Sha256");
        capturedRequest.Should().NotBeNull();
        capturedRequest!.PackageId.Should().Be("pkg-id-1");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task GetPackage_throws_ArgumentException_when_packageId_null_or_whitespace(string? packageId)
    {
        var client = CreateClient();

        var act = () => client.GetPackageAsync(packageId!, TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task GetPackage_throws_when_hash_function_unrecognized()
    {
        var response = new GetPackageResponse
        {
            ArchivePayload = ByteString.CopyFrom(new byte[] { 0x01 }),
            Hash = "pkg-id-1",
            HashFunction = (HashFunction)42
        };

        _packageService
            .GetPackageAsync(
                Arg.Any<GetPackageRequest>(),
                Arg.Any<Metadata>(),
                Arg.Any<DateTime?>(),
                Arg.Any<CancellationToken>())
            .Returns(UnaryResponse(response));

        var client = CreateClient();

        var act = () => client.GetPackageAsync("pkg-id-1", TestContext.Current.CancellationToken);

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .Which.Message.Should().Contain("pkg-id-1");
    }

    [Fact]
    public async Task GetPackage_throws_when_package_not_found()
    {
        _packageService
            .GetPackageAsync(
                Arg.Any<GetPackageRequest>(),
                Arg.Any<Metadata>(),
                Arg.Any<DateTime?>(),
                Arg.Any<CancellationToken>())
            .Returns<AsyncUnaryCall<GetPackageResponse>>(_ =>
                throw new RpcException(new Status(StatusCode.NotFound, "Package not found")));

        var client = CreateClient();

        var act = () => client.GetPackageAsync("pkg-id-missing", TestContext.Current.CancellationToken);

        (await act.Should().ThrowAsync<RpcException>())
            .Which.StatusCode.Should().Be(StatusCode.NotFound);
    }

    [Fact]
    public async Task ListKnownPackages_throws_when_KnownSince_missing()
    {
        var response = new ListKnownPackagesResponse
        {
            PackageDetails =
            {
                new Com.Daml.Ledger.Api.V2.Admin.PackageDetails
                {
                    PackageId = "pkg-id-no-timestamp",
                    Name = "my-package",
                    Version = "1.2.3",
                    PackageSize = 1
                }
            }
        };

        _packageManagementService
            .ListKnownPackagesAsync(
                Arg.Any<ListKnownPackagesRequest>(),
                Arg.Any<Metadata>(),
                Arg.Any<DateTime?>(),
                Arg.Any<CancellationToken>())
            .Returns(UnaryResponse(response));

        var client = CreateClient();

        var act = () => client.ListKnownPackagesAsync(TestContext.Current.CancellationToken);

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .Which.Message.Should().Contain("pkg-id-no-timestamp");
    }

    [Fact]
    public async Task ListVettedPackages_flattens_groups_into_VettedPackage_list()
    {
        var response = new ListVettedPackagesResponse
        {
            NextPageToken = "",
            VettedPackages =
            {
                new Com.Daml.Ledger.Api.V2.VettedPackages
                {
                    ParticipantId = "participant::p1",
                    SynchronizerId = "sync::s1",
                    Packages =
                    {
                        new Com.Daml.Ledger.Api.V2.VettedPackage
                        {
                            PackageId = "pkg-id-1",
                            PackageName = "my-package",
                            PackageVersion = "1.2.3"
                        }
                    }
                }
            }
        };

        _packageService
            .ListVettedPackagesAsync(
                Arg.Any<ListVettedPackagesRequest>(),
                Arg.Any<Metadata>(),
                Arg.Any<DateTime?>(),
                Arg.Any<CancellationToken>())
            .Returns(UnaryResponse(response));

        var client = CreateClient();
        var result = await client.ListVettedPackagesAsync(cancellationToken: TestContext.Current.CancellationToken);

        result.Should().ContainSingle();
        result[0].Should().Be(new VettedPackage("pkg-id-1", "my-package", "1.2.3", "participant::p1", "sync::s1"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(new object[] { new string[0] })]
    public async Task ListVettedPackages_sends_no_filter_when_prefixes_null_or_empty(string[]? packageNamePrefixes)
    {
        ListVettedPackagesRequest? capturedRequest = null;
        _packageService
            .ListVettedPackagesAsync(
                Arg.Do<ListVettedPackagesRequest>(r => capturedRequest = r),
                Arg.Any<Metadata>(),
                Arg.Any<DateTime?>(),
                Arg.Any<CancellationToken>())
            .Returns(UnaryResponse(new ListVettedPackagesResponse { NextPageToken = "" }));

        var client = CreateClient();
        await client.ListVettedPackagesAsync(packageNamePrefixes, TestContext.Current.CancellationToken);

        capturedRequest.Should().NotBeNull();
        capturedRequest!.PackageMetadataFilter.Should().BeNull();
    }

    [Fact]
    public async Task ListVettedPackages_sends_package_name_prefixes_in_filter()
    {
        ListVettedPackagesRequest? capturedRequest = null;
        _packageService
            .ListVettedPackagesAsync(
                Arg.Do<ListVettedPackagesRequest>(r => capturedRequest = r),
                Arg.Any<Metadata>(),
                Arg.Any<DateTime?>(),
                Arg.Any<CancellationToken>())
            .Returns(UnaryResponse(new ListVettedPackagesResponse { NextPageToken = "" }));

        var client = CreateClient();
        await client.ListVettedPackagesAsync(["splice", "canton"], TestContext.Current.CancellationToken);

        capturedRequest.Should().NotBeNull();
        capturedRequest!.PackageMetadataFilter.PackageNamePrefixes.Should().Equal("splice", "canton");
    }

    [Fact]
    public async Task ListVettedPackages_follows_pagination_until_next_page_token_empty()
    {
        var firstPage = new ListVettedPackagesResponse
        {
            NextPageToken = "page-2",
            VettedPackages =
            {
                new Com.Daml.Ledger.Api.V2.VettedPackages
                {
                    ParticipantId = "participant::p1",
                    SynchronizerId = "sync::s1",
                    Packages = { new Com.Daml.Ledger.Api.V2.VettedPackage { PackageId = "pkg-id-1" } }
                }
            }
        };

        var secondPage = new ListVettedPackagesResponse
        {
            NextPageToken = "",
            VettedPackages =
            {
                new Com.Daml.Ledger.Api.V2.VettedPackages
                {
                    ParticipantId = "participant::p1",
                    SynchronizerId = "sync::s2",
                    Packages = { new Com.Daml.Ledger.Api.V2.VettedPackage { PackageId = "pkg-id-2" } }
                }
            }
        };

        var capturedPageTokens = new List<string>();
        _packageService
            .ListVettedPackagesAsync(
                Arg.Do<ListVettedPackagesRequest>(r => capturedPageTokens.Add(r.PageToken)),
                Arg.Any<Metadata>(),
                Arg.Any<DateTime?>(),
                Arg.Any<CancellationToken>())
            .Returns(UnaryResponse(firstPage), UnaryResponse(secondPage));

        var client = CreateClient();
        var result = await client.ListVettedPackagesAsync(cancellationToken: TestContext.Current.CancellationToken);

        result.Select(p => p.PackageId).Should().Equal("pkg-id-1", "pkg-id-2");
        capturedPageTokens.Should().Equal("", "page-2");
    }

    [Fact]
    public async Task ListVettedPackages_throws_when_server_echoes_same_page_token()
    {
        var firstPage = new ListVettedPackagesResponse { NextPageToken = "page-2" };
        var echoedPage = new ListVettedPackagesResponse { NextPageToken = "page-2" };

        _packageService
            .ListVettedPackagesAsync(
                Arg.Any<ListVettedPackagesRequest>(),
                Arg.Any<Metadata>(),
                Arg.Any<DateTime?>(),
                Arg.Any<CancellationToken>())
            .Returns(UnaryResponse(firstPage), UnaryResponse(echoedPage));

        var client = CreateClient();

        var act = () => client.ListVettedPackagesAsync(cancellationToken: TestContext.Current.CancellationToken);

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .Which.Message.Should().Contain("page-2");
    }

    [Fact]
    public async Task ListVettedPackages_sends_filter_on_every_paginated_request()
    {
        var firstPage = new ListVettedPackagesResponse { NextPageToken = "page-2" };
        var secondPage = new ListVettedPackagesResponse { NextPageToken = "" };

        var capturedRequests = new List<ListVettedPackagesRequest>();
        _packageService
            .ListVettedPackagesAsync(
                Arg.Do<ListVettedPackagesRequest>(r => capturedRequests.Add(r.Clone())),
                Arg.Any<Metadata>(),
                Arg.Any<DateTime?>(),
                Arg.Any<CancellationToken>())
            .Returns(UnaryResponse(firstPage), UnaryResponse(secondPage));

        var client = CreateClient();
        await client.ListVettedPackagesAsync(["splice"], TestContext.Current.CancellationToken);

        capturedRequests.Should().HaveCount(2);
        capturedRequests.Should().AllSatisfy(request =>
            request.PackageMetadataFilter.PackageNamePrefixes.Should().Equal("splice"));
    }

    [Theory]
    [InlineData(null, "")]
    [InlineData("submission-1", "submission-1")]
    public async Task UploadDar_sends_dar_file_and_submission_id(string? submissionId, string expectedSubmissionId)
    {
        var darFile = new byte[] { 0x0A, 0x0B, 0x0C };

        UploadDarFileRequest? capturedRequest = null;
        _packageManagementService
            .UploadDarFileAsync(
                Arg.Do<UploadDarFileRequest>(r => capturedRequest = r),
                Arg.Any<Metadata>(),
                Arg.Any<DateTime?>(),
                Arg.Any<CancellationToken>())
            .Returns(UnaryResponse(new UploadDarFileResponse()));

        var client = CreateClient();
        await client.UploadDarAsync(darFile, submissionId, TestContext.Current.CancellationToken);

        capturedRequest.Should().NotBeNull();
        capturedRequest!.DarFile.ToByteArray().Should().Equal(darFile);
        capturedRequest.SubmissionId.Should().Be(expectedSubmissionId);
    }

    [Fact]
    public async Task UploadDar_throws_ArgumentNullException_when_darFile_null()
    {
        var client = CreateClient();

        var act = () => client.UploadDarAsync(null!, cancellationToken: TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task UploadDar_throws_ArgumentException_when_darFile_empty()
    {
        var client = CreateClient();

        var act = () => client.UploadDarAsync([], cancellationToken: TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task ValidateDar_throws_ArgumentNullException_when_darFile_null()
    {
        var client = CreateClient();

        var act = () => client.ValidateDarAsync(null!, TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task ValidateDar_throws_ArgumentException_when_darFile_empty()
    {
        var client = CreateClient();

        var act = () => client.ValidateDarAsync([], TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task ValidateDar_sends_dar_file_in_request()
    {
        var darFile = new byte[] { 0x0A, 0x0B, 0x0C };

        ValidateDarFileRequest? capturedRequest = null;
        _packageManagementService
            .ValidateDarFileAsync(
                Arg.Do<ValidateDarFileRequest>(r => capturedRequest = r),
                Arg.Any<Metadata>(),
                Arg.Any<DateTime?>(),
                Arg.Any<CancellationToken>())
            .Returns(UnaryResponse(new ValidateDarFileResponse()));

        var client = CreateClient();
        await client.ValidateDarAsync(darFile, TestContext.Current.CancellationToken);

        capturedRequest.Should().NotBeNull();
        capturedRequest!.DarFile.ToByteArray().Should().Equal(darFile);
    }
}
