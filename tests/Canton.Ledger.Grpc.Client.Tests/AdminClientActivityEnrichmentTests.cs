// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics;
using Canton.Ledger.Abstractions;
using Canton.Ledger.Kernel.Authentication;
using Com.Daml.Ledger.Api.V2;
using Com.Daml.Ledger.Api.V2.Admin;
using AwesomeAssertions;
using Grpc.Core;
using Grpc.Net.Client;
using NSubstitute;
using Xunit;
using Status = Grpc.Core.Status;
using WireHashFunction = Com.Daml.Ledger.Api.V2.HashFunction;

namespace Canton.Ledger.Grpc.Client.Tests;

[Collection(nameof(AdminClientActivitySourceIsolation))]
public class AdminClientActivityEnrichmentTests
{
    private readonly LedgerClientOptions _options;
    private readonly GrpcChannel _channel;
    private readonly PartyManagementService.PartyManagementServiceClient _partyService;
    private readonly PackageService.PackageServiceClient _packageService;
    private readonly ITokenProvider _tokenProvider = new StaticTokenProvider("test-token");

    public AdminClientActivityEnrichmentTests()
    {
        _options = new LedgerClientOptions { GrpcAddress = "https://localhost:5001" };
        _channel = GrpcChannel.ForAddress(_options.GrpcAddress);

        var callInvoker = Substitute.For<CallInvoker>();
        _partyService = Substitute.ForPartsOf<PartyManagementService.PartyManagementServiceClient>(callInvoker);
        _packageService = Substitute.ForPartsOf<PackageService.PackageServiceClient>(callInvoker);
    }

    private AdminClient CreateClient() => new(
        _options,
        _channel,
        _partyService,
        new UserManagementService.UserManagementServiceClient(_channel),
        _tokenProvider,
        packageService: _packageService);

    private static string Unique(string prefix) => $"{prefix}-{Guid.NewGuid():N}";


    [Fact]
    public async Task AllocatePartyAsync_tags_the_activity_with_grpc_semconv_and_canton_party_id_hint()
    {
        var partyIdHint = Unique("alice");
        var response = new AllocatePartyResponse
        {
            PartyDetails = new Com.Daml.Ledger.Api.V2.Admin.PartyDetails { Party = $"party::{partyIdHint}", IsLocal = true }
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

        using var capture = ActivityCapture.Of(AdminClient.ActivitySourceName);

        var client = CreateClient();
        await client.AllocatePartyAsync(partyIdHint, cancellationToken: TestContext.Current.CancellationToken);

        var activity = capture.Activities.Should()
            .ContainSingle(a => a.GetTagItem(LedgerClientActivityTags.CantonPartyIdHint) as string == partyIdHint)
            .Subject;
        activity.Kind.Should().Be(ActivityKind.Client);
        activity.GetTagItem(ActivityHelper.RpcSystem).Should().Be("grpc");
        activity.GetTagItem(ActivityHelper.RpcService).Should().Be("com.daml.ledger.api.v2.admin.PartyManagementService");
        activity.GetTagItem(ActivityHelper.RpcMethod).Should().Be("AllocateParty");
        activity.GetTagItem(ActivityHelper.ServerAddress).Should().Be("localhost");
        activity.GetTagItem(ActivityHelper.ServerPort).Should().Be(5001);
    }

    [Fact]
    public async Task AllocatePartyAsync_records_an_RpcException_as_an_activity_error()
    {
        var partyIdHint = Unique("alice");
        var ex = new RpcException(new Status(StatusCode.AlreadyExists, $"party exists {Guid.NewGuid()}"));
        _partyService
            .AllocatePartyAsync(
                Arg.Any<AllocatePartyRequest>(),
                Arg.Any<Metadata>(),
                Arg.Any<DateTime?>(),
                Arg.Any<CancellationToken>())
            .Returns(new AsyncUnaryCall<AllocatePartyResponse>(
                Task.FromException<AllocatePartyResponse>(ex),
                Task.FromResult(new Metadata()),
                () => ex.Status,
                () => new Metadata(),
                () => { }));

        using var capture = ActivityCapture.Of(AdminClient.ActivitySourceName);

        var client = CreateClient();
        var act = () => client.AllocatePartyAsync(partyIdHint, cancellationToken: TestContext.Current.CancellationToken);
        await act.Should().ThrowAsync<RpcException>();

        var activity = capture.Activities.Should()
            .ContainSingle(a => a.GetTagItem(LedgerClientActivityTags.CantonPartyIdHint) as string == partyIdHint)
            .Subject;
        activity.Status.Should().Be(ActivityStatusCode.Error);
        activity.GetTagItem(ActivityHelper.ErrorType).Should().Be(StatusCode.AlreadyExists.ToString());
        activity.GetTagItem(ActivityHelper.RpcGrpcStatusCode).Should().Be((int)StatusCode.AlreadyExists);
    }

    [Fact]
    public async Task GetPackageAsync_tags_the_activity_with_grpc_semconv_and_daml_package_id()
    {
        var packageId = Unique("pkg");
        var response = new GetPackageResponse
        {
            ArchivePayload = Google.Protobuf.ByteString.CopyFrom([1, 2, 3]),
            Hash = "hash-123",
            HashFunction = WireHashFunction.Sha256,
        };
        _packageService
            .GetPackageAsync(
                Arg.Any<GetPackageRequest>(),
                Arg.Any<Metadata>(),
                Arg.Any<DateTime?>(),
                Arg.Any<CancellationToken>())
            .Returns(new AsyncUnaryCall<GetPackageResponse>(
                Task.FromResult(response),
                Task.FromResult(new Metadata()),
                () => Status.DefaultSuccess,
                () => new Metadata(),
                () => { }));

        using var capture = ActivityCapture.Of(AdminClient.ActivitySourceName);

        var client = CreateClient();
        await client.GetPackageAsync(packageId, TestContext.Current.CancellationToken);

        var activity = capture.Activities.Should()
            .ContainSingle(a => a.GetTagItem(LedgerClientActivityTags.DamlPackageId) as string == packageId)
            .Subject;
        activity.GetTagItem(ActivityHelper.RpcService).Should().Be("com.daml.ledger.api.v2.PackageService");
        activity.GetTagItem(ActivityHelper.RpcMethod).Should().Be("GetPackage");
    }
}
