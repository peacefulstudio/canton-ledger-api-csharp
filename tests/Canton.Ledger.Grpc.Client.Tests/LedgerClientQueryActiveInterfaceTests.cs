// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Canton.Ledger.Abstractions;
using Canton.Ledger.Kernel.Authentication;
using Canton.Ledger.Testing.Helpers;
using Com.Daml.Ledger.Api.V2;
using Daml.Ledger.Abstractions;
using Daml.Runtime.Data;
using Grpc.Core;
using Grpc.Net.Client;
using NSubstitute;
using Xunit;
using ProtoCreatedEvent = Com.Daml.Ledger.Api.V2.CreatedEvent;
using ProtoIdentifier = Com.Daml.Ledger.Api.V2.Identifier;
using ProtoRecord = Com.Daml.Ledger.Api.V2.Record;
using ProtoValue = Com.Daml.Ledger.Api.V2.Value;
using RpcStatus = Google.Rpc.Status;
using Status = Grpc.Core.Status;

namespace Canton.Ledger.Grpc.Client.Tests;

public class LedgerClientQueryActiveInterfaceTests
{
    private static readonly Party ActAs = new("party::alice");

    private static readonly ProtoIdentifier ImplementingTemplate = new()
    {
        PackageId = "impl-pkg",
        ModuleName = "Token.Impl",
        EntityName = "Asset",
    };

    private static readonly ProtoIdentifier ViewedInterface = new()
    {
        PackageId = "any-pkg",
        ModuleName = "Token.Api",
        EntityName = "IViewedHolding",
    };

    private readonly LedgerClientOptions _options;
    private readonly GrpcChannel _channel;
    private readonly StateService.StateServiceClient _stateService;
    private readonly CommandService.CommandServiceClient _commandService;
    private readonly UpdateService.UpdateServiceClient _updateService;
    private readonly ITokenProvider _tokenProvider = new StaticTokenProvider("test-token");

    public LedgerClientQueryActiveInterfaceTests()
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

    private ICantonLedgerClient CreateClient() => new LedgerClient(
        _options,
        _channel,
        _commandService,
        _updateService,
        _stateService,
        _tokenProvider);

    [Fact]
    public async Task QueryActiveAsync_decodes_the_participant_computed_interface_view_into_the_view_record()
    {
        StubGetLedgerEnd(offset: 10L);
        StubGetActiveContracts(
            MakeActiveContractWithView("00impl", amount: 42.5m),
            MakeActiveContractWithView("00impl2", amount: 7m));

        var client = CreateClient();
        var holdings = await client.QueryActiveAsync<IViewedInterfaceMarker, ViewedInterfaceView>(
            ActAs, cancellationToken: TestContext.Current.CancellationToken);

        holdings.Should().HaveCount(2);
        holdings[0].Id.Value.Should().Be("00impl");
        holdings[0].View.Amount.Should().Be(42.5m);
        holdings[1].Id.Value.Should().Be("00impl2");
        holdings[1].View.Amount.Should().Be(7m);
    }

    [Fact]
    public async Task QueryActiveAsync_asks_the_participant_for_an_InterfaceFilter_carrying_the_view()
    {
        StubGetLedgerEnd(offset: 10L);
        GetActiveContractsRequest? captured = null;
        StubGetActiveContracts(request => captured = request, MakeActiveContractWithView("00impl", amount: 1m));

        var client = CreateClient();
        _ = await client.QueryActiveAsync<IViewedInterfaceMarker, ViewedInterfaceView>(
            ActAs, cancellationToken: TestContext.Current.CancellationToken);

        var filters = captured!.EventFormat.FiltersByParty[ActAs.Id];
        var interfaceFilter = filters.Cumulative.Should().ContainSingle().Subject.InterfaceFilter;
        interfaceFilter.Should().NotBeNull();
        interfaceFilter.IncludeInterfaceView.Should().BeTrue();
        interfaceFilter.InterfaceId.EntityName.Should().Be("IViewedHolding");
    }

    [Fact]
    public async Task QueryActiveAsync_ignores_the_implementing_templates_create_arguments()
    {
        StubGetLedgerEnd(offset: 10L);
        var entry = MakeActiveContractWithView("00impl", amount: 42.5m);
        entry.ActiveContract.CreatedEvent.CreateArguments = new ProtoRecord
        {
            Fields = { new RecordField { Label = "amount", Value = new ProtoValue { Numeric = "999" } } },
        };
        StubGetActiveContracts(entry);

        var client = CreateClient();
        var holdings = await client.QueryActiveAsync<IViewedInterfaceMarker, ViewedInterfaceView>(
            ActAs, cancellationToken: TestContext.Current.CancellationToken);

        holdings.Should().ContainSingle().Which.View.Amount.Should().Be(42.5m);
    }

    [Fact]
    public async Task QueryActiveAsync_returns_an_empty_list_for_an_empty_snapshot()
    {
        StubGetLedgerEnd(offset: 10L);
        StubGetActiveContracts();

        var client = CreateClient();
        var holdings = await client.QueryActiveAsync<IViewedInterfaceMarker, ViewedInterfaceView>(
            ActAs, cancellationToken: TestContext.Current.CancellationToken);

        holdings.Should().BeEmpty();
    }

    [Fact]
    public async Task QueryActiveAsync_throws_LedgerOperationException_when_the_snapshot_faults()
    {
        StubGetLedgerEnd(offset: 10L);
        StubGetActiveContractsFailure(new RpcException(new Status(StatusCode.Unavailable, "participant down")));

        var client = CreateClient();
        var querying = async () => await client.QueryActiveAsync<IViewedInterfaceMarker, ViewedInterfaceView>(
            ActAs, cancellationToken: TestContext.Current.CancellationToken);

        (await querying.Should().ThrowAsync<LedgerOperationException>())
            .Which.StatusCode.Should().Be((int)StatusCode.Unavailable);
    }

    [Fact]
    public async Task QueryActiveAsync_throws_LedgerOperationException_rather_than_shortening_the_list_on_an_unclassified_row()
    {
        StubGetLedgerEnd(offset: 10L);
        StubGetActiveContracts(
            MakeActiveContractWithView("00impl", amount: 1m),
            MakeActiveContract("00unrelated", offset: 88L));

        var client = CreateClient();
        var querying = async () => await client.QueryActiveAsync<IViewedInterfaceMarker, ViewedInterfaceView>(
            ActAs, cancellationToken: TestContext.Current.CancellationToken);

        await querying.Should().ThrowAsync<LedgerOperationException>()
            .WithMessage("*unclassified row*offset 88*");
    }

    [Fact]
    public async Task QueryActiveAsync_throws_LedgerOperationException_when_the_view_does_not_decode()
    {
        StubGetLedgerEnd(offset: 10L);
        var entry = MakeActiveContractWithView("00impl", amount: 1m);
        entry.ActiveContract.CreatedEvent.InterfaceViews[0].ViewValue = new ProtoRecord
        {
            Fields = { new RecordField { Label = "quantity", Value = new ProtoValue { Numeric = "1" } } },
        };
        StubGetActiveContracts(entry);

        var client = CreateClient();
        var querying = async () => await client.QueryActiveAsync<IViewedInterfaceMarker, ViewedInterfaceView>(
            ActAs, cancellationToken: TestContext.Current.CancellationToken);

        await querying.Should().ThrowAsync<LedgerOperationException>()
            .WithMessage($"*did not decode into {nameof(ViewedInterfaceView)}*");
    }

    [Fact]
    public async Task QueryActiveAsync_throws_LedgerOperationException_when_the_view_status_is_not_Ok()
    {
        StubGetLedgerEnd(offset: 10L);
        var entry = MakeActiveContractWithView("00impl", amount: 1m);
        entry.ActiveContract.CreatedEvent.InterfaceViews[0].ViewStatus = new RpcStatus { Code = 9, Message = "view failed" };
        StubGetActiveContracts(entry);

        var client = CreateClient();
        var querying = async () => await client.QueryActiveAsync<IViewedInterfaceMarker, ViewedInterfaceView>(
            ActAs, cancellationToken: TestContext.Current.CancellationToken);

        await querying.Should().ThrowAsync<LedgerOperationException>()
            .WithMessage("*unclassified row*");
    }

    private static GetActiveContractsResponse MakeActiveContract(string contractId, long offset = 0L) =>
        new()
        {
            ActiveContract = new ActiveContract
            {
                CreatedEvent = new ProtoCreatedEvent
                {
                    ContractId = contractId,
                    TemplateId = ImplementingTemplate,
                    CreateArguments = new ProtoRecord(),
                    Offset = offset,
                },
                SynchronizerId = "sync-1",
            },
        };

    private static GetActiveContractsResponse MakeActiveContractWithView(string contractId, decimal amount)
    {
        var response = MakeActiveContract(contractId);
        response.ActiveContract.CreatedEvent.InterfaceViews.Add(new InterfaceView
        {
            InterfaceId = ViewedInterface,
            ViewStatus = new RpcStatus { Code = 0 },
            ViewValue = new ProtoRecord
            {
                Fields =
                {
                    new RecordField
                    {
                        Label = "amount",
                        Value = new ProtoValue { Numeric = amount.ToString(System.Globalization.CultureInfo.InvariantCulture) },
                    },
                },
            },
        });
        return response;
    }

    private void StubGetLedgerEnd(long offset) =>
        _stateService
            .GetLedgerEndAsync(
                Arg.Any<GetLedgerEndRequest>(),
                Arg.Any<Metadata>(),
                Arg.Any<DateTime?>(),
                Arg.Any<CancellationToken>())
            .Returns(new AsyncUnaryCall<GetLedgerEndResponse>(
                Task.FromResult(new GetLedgerEndResponse { Offset = offset }),
                Task.FromResult(new Metadata()),
                () => Status.DefaultSuccess,
                () => new Metadata(),
                () => { }));

    private void StubGetActiveContracts(params GetActiveContractsResponse[] responses) =>
        StubGetActiveContracts(captureRequest: null, responses);

    private void StubGetActiveContracts(
        Action<GetActiveContractsRequest>? captureRequest,
        params GetActiveContractsResponse[] responses) =>
        StubGetActiveContractsCall(new FakeStreamReader<GetActiveContractsResponse>(responses), captureRequest);

    private void StubGetActiveContractsFailure(RpcException fault) =>
        StubGetActiveContractsCall(
            new FakeStreamReader<GetActiveContractsResponse>([], fault),
            captureRequest: null);

    private void StubGetActiveContractsCall(
        FakeStreamReader<GetActiveContractsResponse> reader,
        Action<GetActiveContractsRequest>? captureRequest)
    {
        var call = new AsyncServerStreamingCall<GetActiveContractsResponse>(
            reader,
            Task.FromResult(new Metadata()),
            () => Status.DefaultSuccess,
            () => new Metadata(),
            () => { });

        _stateService
            .GetActiveContracts(
                Arg.Any<GetActiveContractsRequest>(),
                Arg.Any<Metadata>(),
                Arg.Any<DateTime?>(),
                Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                if (captureRequest is { } capture && callInfo.Arg<GetActiveContractsRequest>() is { } request)
                {
                    capture(request);
                }
                return call;
            });
    }
}
