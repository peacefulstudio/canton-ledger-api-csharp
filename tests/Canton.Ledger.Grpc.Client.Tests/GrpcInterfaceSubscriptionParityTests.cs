// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using Canton.Ledger.Kernel.Authentication;
using Canton.Ledger.Testing.Helpers;
using Com.Daml.Ledger.Api.V2;
using Daml.Ledger.Abstractions;
using Grpc.Core;
using Grpc.Net.Client;
using NSubstitute;
using ProtoCreatedEvent = Com.Daml.Ledger.Api.V2.CreatedEvent;
using ProtoIdentifier = Com.Daml.Ledger.Api.V2.Identifier;
using ProtoRecord = Com.Daml.Ledger.Api.V2.Record;
using ProtoValue = Com.Daml.Ledger.Api.V2.Value;
using RpcStatus = Google.Rpc.Status;
using Status = Grpc.Core.Status;

namespace Canton.Ledger.Grpc.Client.Tests;

public sealed class GrpcInterfaceSubscriptionParityTests : InterfaceSubscriptionParityTests, IDisposable
{
    private static readonly ProtoIdentifier ImplementingTemplate = new()
    {
        PackageId = "impl-pkg",
        ModuleName = "Token.Impl",
        EntityName = "Asset",
    };

    private static readonly ProtoIdentifier ViewedInterface = new()
    {
        PackageId = "viewed-pkg",
        ModuleName = "Token.Api",
        EntityName = "IViewedHolding",
    };

    private readonly GrpcChannel _channel = GrpcChannel.ForAddress("https://localhost:5001");

    public void Dispose() => _channel.Dispose();

    protected override Task<ILedgerStreamer> OpenInterfaceStreamerAsync()
    {
        var callInvoker = Substitute.For<CallInvoker>();
        var commandService = Substitute.ForPartsOf<CommandService.CommandServiceClient>(callInvoker);
        var updateService = Substitute.ForPartsOf<UpdateService.UpdateServiceClient>(callInvoker);
        var stateService = Substitute.ForPartsOf<StateService.StateServiceClient>(callInvoker);

        stateService
            .GetLedgerEndAsync(
                Arg.Any<GetLedgerEndRequest>(), Arg.Any<Metadata>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(_ => UnaryCall(new GetLedgerEndResponse { Offset = Window.To.Value }));

        stateService
            .GetActiveContracts(
                Arg.Any<GetActiveContractsRequest>(), Arg.Any<Metadata>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(_ => ServerStream(ActiveContractWithView()));

        updateService
            .GetUpdates(
                Arg.Any<GetUpdatesRequest>(), Arg.Any<Metadata>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(_ => ServerStream(TransactionWithView()));

        ILedgerStreamer streamer = new LedgerClient(
            new LedgerClientOptions { GrpcAddress = "https://localhost:5001", UserId = "test-user" },
            _channel,
            commandService,
            updateService,
            stateService,
            new StaticTokenProvider("test-token"));

        return Task.FromResult(streamer);
    }

    private static GetActiveContractsResponse ActiveContractWithView() => new()
    {
        ActiveContract = new ActiveContract
        {
            CreatedEvent = CreatedEventWithView(),
            SynchronizerId = Synchronizer.Id,
        },
    };

    private static GetUpdatesResponse TransactionWithView()
    {
        var transaction = new Transaction
        {
            UpdateId = "u-viewed",
            Offset = CreatedOffset.Value,
            SynchronizerId = Synchronizer.Id,
        };
        transaction.Events.Add(new Event { Created = CreatedEventWithView() });
        return new GetUpdatesResponse { Transaction = transaction };
    }

    private static ProtoCreatedEvent CreatedEventWithView()
    {
        var created = new ProtoCreatedEvent
        {
            ContractId = ViewedContractId,
            TemplateId = ImplementingTemplate,
            CreateArguments = new ProtoRecord(),
            Offset = CreatedOffset.Value,
        };
        created.InterfaceViews.Add(new InterfaceView
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
                        Value = new ProtoValue { Numeric = ViewAmount.ToString(CultureInfo.InvariantCulture) },
                    },
                },
            },
        });
        return created;
    }

    private static AsyncUnaryCall<TResponse> UnaryCall<TResponse>(TResponse response) =>
        new(
            Task.FromResult(response),
            Task.FromResult(new Metadata()),
            () => Status.DefaultSuccess,
            () => new Metadata(),
            () => { });

    private static AsyncServerStreamingCall<TResponse> ServerStream<TResponse>(params TResponse[] responses) =>
        new(
            new FakeStreamReader<TResponse>(responses),
            Task.FromResult(new Metadata()),
            () => Status.DefaultSuccess,
            () => new Metadata(),
            () => { });
}
