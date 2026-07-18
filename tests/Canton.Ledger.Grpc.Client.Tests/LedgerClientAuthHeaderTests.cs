// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Canton.Ledger.Kernel.Authentication;
using Com.Daml.Ledger.Api.V2;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using AwesomeAssertions;
using Grpc.Core;
using Grpc.Net.Client;
using NSubstitute;
using Xunit;
using RuntimeIdentifier = Daml.Runtime.Data.Identifier;
using Status = Grpc.Core.Status;

namespace Canton.Ledger.Grpc.Client.Tests;

public class LedgerClientAuthHeaderTests
{
    private const string AuthorizationKey = "authorization";
    private static readonly Party ActAs = new("party::alice");

    private readonly LedgerClientOptions _options;
    private readonly GrpcChannel _channel;
    private readonly CommandService.CommandServiceClient _commandService;
    private readonly UpdateService.UpdateServiceClient _updateService;
    private readonly StateService.StateServiceClient _stateService;

    public LedgerClientAuthHeaderTests()
    {
        _options = new LedgerClientOptions
        {
            GrpcAddress = "https://localhost:5001",
            UserId = "test-user",
        };
        _channel = GrpcChannel.ForAddress(_options.GrpcAddress);

        var callInvoker = Substitute.For<CallInvoker>();
        _commandService = Substitute.ForPartsOf<CommandService.CommandServiceClient>(callInvoker);
        _updateService = Substitute.ForPartsOf<UpdateService.UpdateServiceClient>(callInvoker);
        _stateService = Substitute.ForPartsOf<StateService.StateServiceClient>(callInvoker);
    }

    private LedgerClient CreateClient(ITokenProvider tokenProvider) => new(
        _options,
        _channel,
        _commandService,
        _updateService,
        _stateService,
        tokenProvider);

    [Fact]
    public async Task GetLedgerEndAsync_attaches_bearer_authorization_header_on_the_unary_call()
    {
        Metadata? captured = null;
        StubGetLedgerEnd(headers => captured = headers);

        var client = CreateClient(new StaticTokenProvider("test-token"));
        _ = await client.GetLedgerEndAsync(cancellationToken: TestContext.Current.CancellationToken);

        AuthorizationHeaderOf(captured).Should().Be("Bearer test-token");
    }

    [Fact]
    public async Task SubscribeAsync_attaches_bearer_authorization_header_on_the_streaming_call()
    {
        Metadata? captured = null;
        StubGetUpdates(headers => captured = headers);

        var client = CreateClient(new StaticTokenProvider("test-token"));
        await foreach (var _ in client.SubscribeAsync<FooBar>(ActAs, cancellationToken: TestContext.Current.CancellationToken)) { }

        AuthorizationHeaderOf(captured).Should().Be("Bearer test-token");
    }

    [Fact]
    public async Task GetLedgerEndAsync_attaches_no_authorization_header_for_ITokenProvider_None()
    {
        Metadata? captured = null;
        StubGetLedgerEnd(headers => captured = headers);

        var client = CreateClient(ITokenProvider.None);
        _ = await client.GetLedgerEndAsync(cancellationToken: TestContext.Current.CancellationToken);

        AuthorizationHeaderOf(captured).Should().BeNull(
            "ITokenProvider.None signals unauthenticated access, so no Authorization header is sent");
    }

    private static string? AuthorizationHeaderOf(Metadata? headers) =>
        headers?.FirstOrDefault(entry => entry.Key == AuthorizationKey)?.Value;

    private void StubGetLedgerEnd(Action<Metadata?> captureHeaders)
    {
        _stateService
            .GetLedgerEndAsync(
                Arg.Any<GetLedgerEndRequest>(),
                Arg.Do<Metadata?>(captureHeaders),
                Arg.Any<DateTime?>(),
                Arg.Any<CancellationToken>())
            .Returns(new AsyncUnaryCall<GetLedgerEndResponse>(
                Task.FromResult(new GetLedgerEndResponse { Offset = 42L }),
                Task.FromResult(new Metadata()),
                () => Status.DefaultSuccess,
                () => new Metadata(),
                () => { }));
    }

    private void StubGetUpdates(Action<Metadata?> captureHeaders)
    {
        var reader = new FakeStreamReader<GetUpdatesResponse>(Array.Empty<GetUpdatesResponse>());
        var call = new AsyncServerStreamingCall<GetUpdatesResponse>(
            reader,
            Task.FromResult(new Metadata()),
            () => Status.DefaultSuccess,
            () => new Metadata(),
            () => { });

        _updateService
            .GetUpdates(
                Arg.Any<GetUpdatesRequest>(),
                Arg.Do<Metadata?>(captureHeaders),
                Arg.Any<DateTime?>(),
                Arg.Any<CancellationToken>())
            .Returns(call);
    }
}
