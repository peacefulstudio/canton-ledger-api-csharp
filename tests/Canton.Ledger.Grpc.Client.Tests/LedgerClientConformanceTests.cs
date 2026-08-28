// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Canton.Ledger.Kernel.Authentication;
using Com.Daml.Ledger.Api.V2;
using Daml.Ledger.Abstractions;
using Daml.Ledger.Abstractions.Testing.Conformance;
using Daml.Runtime;
using Daml.Runtime.Commands;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Grpc.Core;
using Grpc.Net.Client;
using NSubstitute;
using ProtoArchivedEvent = Com.Daml.Ledger.Api.V2.ArchivedEvent;
using ProtoCreatedEvent = Com.Daml.Ledger.Api.V2.CreatedEvent;
using ProtoExercisedEvent = Com.Daml.Ledger.Api.V2.ExercisedEvent;
using ProtoIdentifier = Com.Daml.Ledger.Api.V2.Identifier;
using ProtoRecord = Com.Daml.Ledger.Api.V2.Record;
using ProtoValue = Com.Daml.Ledger.Api.V2.Value;
using RuntimeIdentifier = Daml.Runtime.Data.Identifier;

namespace Canton.Ledger.Grpc.Client.Tests;

public class LedgerClientConformanceTests : LedgerClientConformanceTests<GrpcConformanceProbe>
{
    private const string Synchronizer = "sync-1";
    private const long CreatedOffset = 1L;
    private const long UnclassifiableOffset = 2L;
    private const long ConsumedOffset = 3L;
    private const long LedgerEndOffset = 5L;

    private static readonly ProtoIdentifier ProbeTemplate = new()
    {
        PackageId = "conformance-pkg",
        ModuleName = "Conformance.Probe",
        EntityName = "Probe",
    };

    private static readonly ProtoIdentifier ForeignTemplate = new()
    {
        PackageId = "conformance-pkg",
        ModuleName = "Conformance.Other",
        EntityName = "Other",
    };

    protected override SubmitterInfo Reader { get; } = new Party("party::alice");

    protected override ILedgerClient CreateClient() => BuildClient(snapshotFaultsMidStream: false);

    protected override ILedgerClient CreateFaultingSnapshotClient() => BuildClient(snapshotFaultsMidStream: true);

    private static LedgerClient BuildClient(bool snapshotFaultsMidStream)
    {
        var options = new LedgerClientOptions
        {
            GrpcAddress = "https://localhost:5001",
            UserId = "conformance-user",
        };
        var callInvoker = Substitute.For<CallInvoker>();
        var commandService = Substitute.ForPartsOf<CommandService.CommandServiceClient>(callInvoker);
        var updateService = Substitute.ForPartsOf<UpdateService.UpdateServiceClient>(callInvoker);
        var stateService = Substitute.ForPartsOf<StateService.StateServiceClient>(callInvoker);

        stateService
            .GetLedgerEndAsync(
                Arg.Any<GetLedgerEndRequest>(), Arg.Any<Metadata>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(_ => UnaryCall(new GetLedgerEndResponse { Offset = LedgerEndOffset }));

        stateService
            .GetActiveContracts(
                Arg.Any<GetActiveContractsRequest>(), Arg.Any<Metadata>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(call => ServerStream(
                ActiveContractsAt(call.Arg<GetActiveContractsRequest>()!.ActiveAtOffset),
                snapshotFaultsMidStream
                    ? new RpcException(new Status(StatusCode.Unavailable, "snapshot aborted mid-stream"))
                    : null));

        updateService
            .GetUpdates(
                Arg.Any<GetUpdatesRequest>(), Arg.Any<Metadata>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(call => ServerStream(UpdatesFor(call.Arg<GetUpdatesRequest>()!), afterItemsException: null));

        return new LedgerClient(
            options,
            GrpcChannel.ForAddress(options.GrpcAddress),
            commandService,
            updateService,
            stateService,
            new StaticTokenProvider("conformance-token"));
    }

    private static IReadOnlyList<GetActiveContractsResponse> ActiveContractsAt(long activeAtOffset) =>
    [
        .. new[]
        {
            ActiveContract("00probe", ProbeTemplate, CreatedOffset),
            ActiveContract("00foreign", ForeignTemplate, UnclassifiableOffset),
        }.Where(entry => entry.ActiveContract.CreatedEvent.Offset <= activeAtOffset),
    ];

    private static IReadOnlyList<GetUpdatesResponse> UpdatesFor(GetUpdatesRequest request)
    {
        var endInclusive = request.HasEndInclusive ? request.EndInclusive : long.MaxValue;
        var consumption = request.UpdateFormat.IncludeTransactions.TransactionShape == TransactionShape.LedgerEffects
            ? ConsumingExercise()
            : Archival();

        return
        [
            .. new[] { (CreatedOffset, Creation()), (ConsumedOffset, consumption) }
                .Where(seeded => seeded.Item1 > request.BeginExclusive && seeded.Item1 <= endInclusive)
                .Select(seeded => new GetUpdatesResponse { Transaction = Transaction(seeded.Item1, seeded.Item2) }),
        ];
    }

    private static Event Creation() => new()
    {
        Created = new ProtoCreatedEvent
        {
            ContractId = "00probe",
            TemplateId = ProbeTemplate,
            CreateArguments = new ProtoRecord(),
            Offset = CreatedOffset,
        },
    };

    private static Event Archival() => new()
    {
        Archived = new ProtoArchivedEvent
        {
            ContractId = "00probe",
            TemplateId = ProbeTemplate,
            Offset = ConsumedOffset,
        },
    };

    private static Event ConsumingExercise() => new()
    {
        Exercised = new ProtoExercisedEvent
        {
            ContractId = "00probe",
            TemplateId = ProbeTemplate,
            Choice = "Archive",
            ChoiceArgument = new ProtoValue { Unit = new Google.Protobuf.WellKnownTypes.Empty() },
            ExerciseResult = new ProtoValue { Unit = new Google.Protobuf.WellKnownTypes.Empty() },
            Consuming = true,
            Offset = ConsumedOffset,
        },
    };

    private static Transaction Transaction(long offset, Event seeded)
    {
        var transaction = new Transaction
        {
            UpdateId = $"u-{offset}",
            Offset = offset,
            SynchronizerId = Synchronizer,
        };
        transaction.Events.Add(seeded);
        return transaction;
    }

    private static GetActiveContractsResponse ActiveContract(string contractId, ProtoIdentifier templateId, long offset) => new()
    {
        ActiveContract = new ActiveContract
        {
            CreatedEvent = new ProtoCreatedEvent
            {
                ContractId = contractId,
                TemplateId = templateId,
                CreateArguments = new ProtoRecord(),
                Offset = offset,
            },
            SynchronizerId = Synchronizer,
        },
    };

    private static AsyncUnaryCall<TResponse> UnaryCall<TResponse>(TResponse response) =>
        new(
            Task.FromResult(response),
            Task.FromResult(new Metadata()),
            () => Status.DefaultSuccess,
            () => new Metadata(),
            () => { });

    private static AsyncServerStreamingCall<TResponse> ServerStream<TResponse>(
        IReadOnlyList<TResponse> responses,
        Exception? afterItemsException) =>
        new(
            new FakeStreamReader<TResponse>(responses, afterItemsException),
            Task.FromResult(new Metadata()),
            () => Status.DefaultSuccess,
            () => new Metadata(),
            () => { });
}

/// <summary>The Daml marker the conformance scenario's snapshot and streams are filtered to.</summary>
/// <param name="Owner">The party the probe contract is issued to.</param>
public sealed record GrpcConformanceProbe(string Owner) : ITemplate
{
    /// <inheritdoc cref="ITemplate" />
    public static RuntimeIdentifier TemplateId { get; } = new("conformance-pkg", "Conformance.Probe", "Probe");

    /// <inheritdoc cref="ITemplate" />
    public static string PackageId => "conformance-pkg";

    /// <inheritdoc cref="ITemplate" />
    public static string PackageName => "conformance-package";

    /// <inheritdoc cref="ITemplate" />
    public static Version PackageVersion { get; } = new(0, 1, 0);

    /// <inheritdoc cref="ITemplate" />
    public static DamlTypeDescriptor DamlTypeId { get; } = new(TemplateId, DamlTypeKind.Template, PackageName);

    /// <inheritdoc cref="ITemplate" />
    public DamlRecord ToRecord() => DamlRecord.Create(DamlField.Create("owner", new DamlParty(Owner)));
}
