// Copyright (c) 2026 Peaceful Studio OÜ. All rights reserved.

using Canton.Ledger.Auth;
using Com.Daml.Ledger.Api.V2;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Streams;
using FluentAssertions;
using Grpc.Core;
using Grpc.Net.Client;
using NSubstitute;
using Xunit;
using ProtoArchivedEvent = Com.Daml.Ledger.Api.V2.ArchivedEvent;
using ProtoCreatedEvent = Com.Daml.Ledger.Api.V2.CreatedEvent;
using ProtoExercisedEvent = Com.Daml.Ledger.Api.V2.ExercisedEvent;
using ProtoIdentifier = Com.Daml.Ledger.Api.V2.Identifier;
using ProtoRecord = Com.Daml.Ledger.Api.V2.Record;
using ProtoValue = Com.Daml.Ledger.Api.V2.Value;
using RuntimeIdentifier = Daml.Runtime.Data.Identifier;

namespace Canton.Ledger.Grpc.Client.Tests;

public class LedgerClientSubscribeTests
{
    private const string ActAs = "party::alice";

    private readonly LedgerClientOptions _options;
    private readonly GrpcChannel _channel;
    private readonly CommandService.CommandServiceClient _commandService;
    private readonly UpdateService.UpdateServiceClient _updateService;
    private readonly StateService.StateServiceClient _stateService;
    private readonly ITokenProvider _tokenProvider = new StaticTokenProvider("test-token");

    public LedgerClientSubscribeTests()
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

    private LedgerClient CreateClient() => new(
        _options,
        _channel,
        _commandService,
        _updateService,
        _stateService,
        _tokenProvider);

    [Fact]
    public async Task SubscribeAsync_yields_typed_Created_event()
    {
        var transaction = MakeTransaction(MakeCreatedEvent("00abc", FooBarTemplate, offset: 42L));
        StubGetUpdates(MakeGetUpdatesResponse(transaction));

        var client = CreateClient();
        var events = await CollectAsync(client.SubscribeAsync<FooBar>(ActAs));

        events.Should().ContainSingle();
        var created = events[0].Should().BeOfType<ContractStreamEvent<FooBar>.Created>().Subject;
        created.ContractId.Value.Should().Be("00abc");
        created.Offset.Should().Be(42L);
    }

    [Fact]
    public async Task SubscribeAsync_yields_typed_Archived_event()
    {
        var transaction = MakeTransaction(new Event
        {
            Archived = new ProtoArchivedEvent
            {
                ContractId = "00abc",
                TemplateId = FooBarTemplate,
                Offset = 7L,
            },
        });
        StubGetUpdates(MakeGetUpdatesResponse(transaction));

        var client = CreateClient();
        var events = await CollectAsync(client.SubscribeAsync<FooBar>(ActAs));

        var archived = events.Should().ContainSingle().Subject
            .Should().BeOfType<ContractStreamEvent<FooBar>.Archived>().Subject;
        archived.ContractId.Value.Should().Be("00abc");
        archived.Offset.Should().Be(7L);
    }

    [Fact]
    public async Task SubscribeAsync_yields_typed_Exercised_event()
    {
        var transaction = MakeTransaction(new Event
        {
            Exercised = new ProtoExercisedEvent
            {
                ContractId = "00abc",
                TemplateId = FooBarTemplate,
                Choice = "Accept",
                ChoiceArgument = new ProtoValue { Unit = new Google.Protobuf.WellKnownTypes.Empty() },
                ExerciseResult = new ProtoValue { ContractId = "00new" },
                Consuming = true,
                Offset = 99L,
            },
        });
        StubGetUpdates(MakeGetUpdatesResponse(transaction));

        var client = CreateClient();
        var events = await CollectAsync(client.SubscribeAsync<FooBar>(ActAs));

        var exercised = events.Should().ContainSingle().Subject
            .Should().BeOfType<ContractStreamEvent<FooBar>.Exercised>().Subject;
        exercised.ChoiceName.Should().Be("Accept");
        exercised.Consuming.Should().BeTrue();
        exercised.Offset.Should().Be(99L);
    }

    [Fact]
    public async Task SubscribeAsync_filters_out_events_for_unrelated_templates()
    {
        var transaction = MakeTransaction(
            MakeCreatedEvent("00foo", FooBarTemplate, offset: 1L),
            MakeCreatedEvent("00other", new ProtoIdentifier
            {
                PackageId = "test-pkg",
                ModuleName = "Murmures.Other",
                EntityName = "Other",
            }, offset: 2L),
            MakeCreatedEvent("00foo2", FooBarTemplate, offset: 3L));
        StubGetUpdates(MakeGetUpdatesResponse(transaction));

        var client = CreateClient();
        var events = await CollectAsync(client.SubscribeAsync<FooBar>(ActAs));

        events.Should().HaveCount(2);
        events.OfType<ContractStreamEvent<FooBar>.Created>()
            .Select(c => c.ContractId.Value)
            .Should().Equal("00foo", "00foo2");
    }

    [Fact]
    public async Task SubscribeAsync_passes_fromOffset_to_request()
    {
        GetUpdatesRequest? captured = null;
        StubGetUpdates(MakeGetUpdatesResponse(), capture: r => captured = r);

        var client = CreateClient();
        _ = await CollectAsync(client.SubscribeAsync<FooBar>(ActAs, fromOffset: 123L));

        captured.Should().NotBeNull();
        captured!.BeginExclusive.Should().Be(123L);
    }

    [Fact]
    public async Task SubscribeAsync_filters_request_by_template_id()
    {
        GetUpdatesRequest? captured = null;
        StubGetUpdates(MakeGetUpdatesResponse(), capture: r => captured = r);

        var client = CreateClient();
        _ = await CollectAsync(client.SubscribeAsync<FooBar>(ActAs));

        captured.Should().NotBeNull();
        var filter = captured!.UpdateFormat.IncludeTransactions.EventFormat.FiltersByParty[ActAs];
        filter.Cumulative.Should().ContainSingle();
        var template = filter.Cumulative[0].TemplateFilter;
        template.Should().NotBeNull();
        template.TemplateId.ModuleName.Should().Be("Murmures.Foo");
        template.TemplateId.EntityName.Should().Be("FooBar");
    }

    [Fact]
    public async Task SubscribeAsync_surfaces_RpcException_as_StreamError_event()
    {
        var ex = new RpcException(new Status(StatusCode.Unavailable, "transient down"));
        StubGetUpdatesFailure(ex);

        var client = CreateClient();
        var events = await CollectAsync(client.SubscribeAsync<FooBar>(ActAs));

        var error = events.Should().ContainSingle().Subject
            .Should().BeOfType<ContractStreamEvent<FooBar>.StreamError>().Subject;
        error.StatusCode.Should().Be((int)StatusCode.Unavailable);
        error.Message.Should().Contain("transient");
    }

    [Fact]
    public async Task SubscribeAsync_round_trips_offset_through_Created_event()
    {
        var transaction = MakeTransaction(MakeCreatedEvent("00first", FooBarTemplate, offset: 100L));
        StubGetUpdates(MakeGetUpdatesResponse(transaction));

        var client = CreateClient();
        var events = await CollectAsync(client.SubscribeAsync<FooBar>(ActAs, fromOffset: 50L));

        // Caller recovers the last-seen offset from the event so it can resume later.
        var lastOffset = events.OfType<ContractStreamEvent<FooBar>.Created>().Last().Offset;
        lastOffset.Should().Be(100L);
    }

    [Fact]
    public async Task SubscribeAsync_propagates_OperationCanceledException()
    {
        var cts = new CancellationTokenSource();
        cts.Cancel();
        StubGetUpdatesCancellable();

        var client = CreateClient();
        var act = async () => { await foreach (var _ in client.SubscribeAsync<FooBar>(ActAs, cancellationToken: cts.Token)) { } };

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task SubscribeActiveAsync_yields_only_typed_Created_for_matching_template()
    {
        StubGetLedgerEnd(offset: 10L);
        var matching = MakeActiveContract("00foo", FooBarTemplate);
        var unrelated = MakeActiveContract("00other", new ProtoIdentifier
        {
            PackageId = "test-pkg",
            ModuleName = "Murmures.Other",
            EntityName = "Other",
        });
        StubGetActiveContracts(matching, unrelated);

        var client = CreateClient();
        var events = await CollectAsync(client.SubscribeActiveAsync<FooBar>(ActAs));

        events.Should().ContainSingle();
        events[0].ContractId.Value.Should().Be("00foo");
    }

    [Fact]
    public async Task SubscribeActiveAsync_uses_ledger_end_as_active_at_offset()
    {
        StubGetLedgerEnd(offset: 42L);
        GetActiveContractsRequest? captured = null;
        StubGetActiveContracts(captureRequest: r => captured = r);

        var client = CreateClient();
        _ = await CollectAsync(client.SubscribeActiveAsync<FooBar>(ActAs));

        captured.Should().NotBeNull();
        captured!.ActiveAtOffset.Should().Be(42L);
    }

    [Fact]
    public async Task SubscribeActiveAsync_filters_request_by_template_id()
    {
        StubGetLedgerEnd(offset: 0L);
        GetActiveContractsRequest? captured = null;
        StubGetActiveContracts(captureRequest: r => captured = r);

        var client = CreateClient();
        _ = await CollectAsync(client.SubscribeActiveAsync<FooBar>(ActAs));

        captured.Should().NotBeNull();
        var filter = captured!.EventFormat.FiltersByParty[ActAs];
        filter.Cumulative.Should().ContainSingle();
        var template = filter.Cumulative[0].TemplateFilter;
        template.TemplateId.ModuleName.Should().Be("Murmures.Foo");
        template.TemplateId.EntityName.Should().Be("FooBar");
    }

    [Fact]
    public async Task SubscribeAsync_yields_typed_Checkpoint_when_no_transactions_arrive()
    {
        // Quiet-period scenario: only OffsetCheckpoint messages arrive. Without
        // surfacing them, a consumer crashing here would resume from the previous
        // create/archive offset and re-process every transaction in between.
        var first = new GetUpdatesResponse { OffsetCheckpoint = new OffsetCheckpoint { Offset = 50L } };
        var second = new GetUpdatesResponse { OffsetCheckpoint = new OffsetCheckpoint { Offset = 60L } };
        StubGetUpdates(first, second);

        var client = CreateClient();
        var events = await CollectAsync(client.SubscribeAsync<FooBar>(ActAs));

        var checkpoints = events.OfType<ContractStreamEvent<FooBar>.Checkpoint>().ToList();
        checkpoints.Should().HaveCount(2);
        checkpoints[0].Offset.Should().Be(50L);
        checkpoints[1].Offset.Should().Be(60L);
    }

    [Fact]
    public async Task SubscribeAsync_yields_typed_Assigned_event()
    {
        var reassignment = new Reassignment { UpdateId = "u-r", Offset = 200L };
        reassignment.Events.Add(new ReassignmentEvent
        {
            Assigned = new AssignedEvent
            {
                Source = "sync-a",
                Target = "sync-b",
                ReassignmentId = "rid-1",
                CreatedEvent = new ProtoCreatedEvent
                {
                    ContractId = "00abc",
                    TemplateId = FooBarTemplate,
                    CreateArguments = new ProtoRecord(),
                    Offset = 200L,
                },
            },
        });
        StubGetUpdates(new GetUpdatesResponse { Reassignment = reassignment });

        var client = CreateClient();
        var events = await CollectAsync(client.SubscribeAsync<FooBar>(ActAs));

        var assigned = events.Should().ContainSingle().Subject
            .Should().BeOfType<ContractStreamEvent<FooBar>.Assigned>().Subject;
        assigned.ContractId.Value.Should().Be("00abc");
        assigned.Source.Should().Be("sync-a");
        assigned.Target.Should().Be("sync-b");
        assigned.Offset.Should().Be(200L);
    }

    [Fact]
    public async Task SubscribeAsync_yields_typed_Unassigned_event()
    {
        var reassignment = new Reassignment { UpdateId = "u-u", Offset = 201L };
        reassignment.Events.Add(new ReassignmentEvent
        {
            Unassigned = new UnassignedEvent
            {
                ReassignmentId = "rid-2",
                ContractId = "00abc",
                TemplateId = FooBarTemplate,
                Source = "sync-a",
                Target = "sync-b",
                Offset = 201L,
            },
        });
        StubGetUpdates(new GetUpdatesResponse { Reassignment = reassignment });

        var client = CreateClient();
        var events = await CollectAsync(client.SubscribeAsync<FooBar>(ActAs));

        var unassigned = events.Should().ContainSingle().Subject
            .Should().BeOfType<ContractStreamEvent<FooBar>.Unassigned>().Subject;
        unassigned.ContractId.Value.Should().Be("00abc");
        unassigned.Source.Should().Be("sync-a");
        unassigned.Offset.Should().Be(201L);
    }

    [Fact]
    public async Task SubscribeAsync_filters_reassignment_events_by_template_id()
    {
        var unrelatedTemplate = new ProtoIdentifier
        {
            PackageId = "test-pkg",
            ModuleName = "Murmures.Other",
            EntityName = "Other",
        };
        var reassignment = new Reassignment { UpdateId = "u-mixed", Offset = 300L };
        reassignment.Events.Add(new ReassignmentEvent
        {
            Unassigned = new UnassignedEvent
            {
                ContractId = "00unrelated",
                TemplateId = unrelatedTemplate,
                Source = "sync-a",
                Target = "sync-b",
                Offset = 300L,
            },
        });
        reassignment.Events.Add(new ReassignmentEvent
        {
            Unassigned = new UnassignedEvent
            {
                ContractId = "00foo",
                TemplateId = FooBarTemplate,
                Source = "sync-a",
                Target = "sync-b",
                Offset = 301L,
            },
        });
        StubGetUpdates(new GetUpdatesResponse { Reassignment = reassignment });

        var client = CreateClient();
        var events = await CollectAsync(client.SubscribeAsync<FooBar>(ActAs));

        events.Should().ContainSingle();
        events[0].Should().BeOfType<ContractStreamEvent<FooBar>.Unassigned>()
            .Which.ContractId.Value.Should().Be("00foo");
    }

    [Fact]
    public async Task SubscribeActiveAsync_includes_IncompleteUnassigned_entries()
    {
        // Multi-synchronizer ACS view: a contract mid-reassignment must still
        // appear in the snapshot, otherwise consumers see an under-reported
        // active set.
        StubGetLedgerEnd(offset: 0L);
        var incomplete = new GetActiveContractsResponse
        {
            IncompleteUnassigned = new IncompleteUnassigned
            {
                CreatedEvent = new ProtoCreatedEvent
                {
                    ContractId = "00mid",
                    TemplateId = FooBarTemplate,
                    CreateArguments = new ProtoRecord(),
                },
                UnassignedEvent = new UnassignedEvent
                {
                    ContractId = "00mid",
                    TemplateId = FooBarTemplate,
                    Source = "sync-a",
                    Target = "sync-b",
                },
            },
        };
        StubGetActiveContracts(incomplete);

        var client = CreateClient();
        var events = await CollectAsync(client.SubscribeActiveAsync<FooBar>(ActAs));

        events.Should().ContainSingle();
        events[0].ContractId.Value.Should().Be("00mid");
    }

    [Fact]
    public async Task SubscribeActiveAsync_includes_IncompleteAssigned_entries()
    {
        StubGetLedgerEnd(offset: 0L);
        var incomplete = new GetActiveContractsResponse
        {
            IncompleteAssigned = new IncompleteAssigned
            {
                AssignedEvent = new AssignedEvent
                {
                    Source = "sync-a",
                    Target = "sync-b",
                    CreatedEvent = new ProtoCreatedEvent
                    {
                        ContractId = "00mid2",
                        TemplateId = FooBarTemplate,
                        CreateArguments = new ProtoRecord(),
                    },
                },
            },
        };
        StubGetActiveContracts(incomplete);

        var client = CreateClient();
        var events = await CollectAsync(client.SubscribeActiveAsync<FooBar>(ActAs));

        events.Should().ContainSingle();
        events[0].ContractId.Value.Should().Be("00mid2");
    }

    [Fact]
    public async Task GetLedgerEndAsync_returns_offset()
    {
        StubGetLedgerEnd(offset: 12345L);
        var client = CreateClient();

        var offset = await client.GetLedgerEndAsync();

        offset.Should().Be(12345L);
    }

    private static readonly ProtoIdentifier FooBarTemplate = new()
    {
        PackageId = "test-pkg",
        ModuleName = "Murmures.Foo",
        EntityName = "FooBar",
    };

    private static Event MakeCreatedEvent(string contractId, ProtoIdentifier templateId, long offset)
    {
        return new Event
        {
            Created = new ProtoCreatedEvent
            {
                ContractId = contractId,
                TemplateId = templateId,
                CreateArguments = new ProtoRecord(),
                Offset = offset,
            },
        };
    }

    private static Transaction MakeTransaction(params Event[] events)
    {
        var tx = new Transaction { UpdateId = "u-test", Offset = events.Length > 0 ? 1L : 0L };
        tx.Events.Add(events);
        return tx;
    }

    private static GetUpdatesResponse MakeGetUpdatesResponse(Transaction? transaction = null)
    {
        return transaction is null
            ? new GetUpdatesResponse { OffsetCheckpoint = new OffsetCheckpoint() }
            : new GetUpdatesResponse { Transaction = transaction };
    }

    private static GetActiveContractsResponse MakeActiveContract(string contractId, ProtoIdentifier templateId)
    {
        return new GetActiveContractsResponse
        {
            ActiveContract = new ActiveContract
            {
                CreatedEvent = new ProtoCreatedEvent
                {
                    ContractId = contractId,
                    TemplateId = templateId,
                    CreateArguments = new ProtoRecord(),
                },
                SynchronizerId = "sync-1",
            },
        };
    }

    private void StubGetUpdates(
        GetUpdatesResponse response,
        Action<GetUpdatesRequest>? capture = null)
        => StubGetUpdates(capture, response);

    private void StubGetUpdates(params GetUpdatesResponse[] responses)
        => StubGetUpdates(capture: null, responses);

    private void StubGetUpdates(
        Action<GetUpdatesRequest>? capture,
        params GetUpdatesResponse[] responses)
    {
        var reader = new FakeStreamReader<GetUpdatesResponse>(responses);
        var call = MakeServerStreamingCall(reader);

        _updateService
            .GetUpdates(
                Arg.Do<GetUpdatesRequest>(r => capture?.Invoke(r)),
                Arg.Any<Metadata>(),
                Arg.Any<DateTime?>(),
                Arg.Any<CancellationToken>())
            .Returns(call);
    }

    private void StubGetUpdatesFailure(RpcException exception)
    {
        var reader = new FakeStreamReader<GetUpdatesResponse>(Array.Empty<GetUpdatesResponse>(), exception);
        var call = MakeServerStreamingCall(reader);

        _updateService
            .GetUpdates(
                Arg.Any<GetUpdatesRequest>(),
                Arg.Any<Metadata>(),
                Arg.Any<DateTime?>(),
                Arg.Any<CancellationToken>())
            .Returns(call);
    }

    private void StubGetUpdatesCancellable()
    {
        var reader = new FakeStreamReader<GetUpdatesResponse>(
            Array.Empty<GetUpdatesResponse>(),
            new OperationCanceledException());
        var call = MakeServerStreamingCall(reader);

        _updateService
            .GetUpdates(
                Arg.Any<GetUpdatesRequest>(),
                Arg.Any<Metadata>(),
                Arg.Any<DateTime?>(),
                Arg.Any<CancellationToken>())
            .Returns(call);
    }

    private void StubGetLedgerEnd(long offset)
    {
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
    }

    private void StubGetActiveContracts(
        params GetActiveContractsResponse[] responses)
        => StubGetActiveContracts(captureRequest: null, responses);

    private void StubGetActiveContracts(
        Action<GetActiveContractsRequest>? captureRequest,
        params GetActiveContractsResponse[] responses)
    {
        var reader = new FakeStreamReader<GetActiveContractsResponse>(responses);
        var call = MakeServerStreamingCall(reader);

        _stateService
            .GetActiveContracts(
                Arg.Do<GetActiveContractsRequest>(r => captureRequest?.Invoke(r)),
                Arg.Any<Metadata>(),
                Arg.Any<DateTime?>(),
                Arg.Any<CancellationToken>())
            .Returns(call);
    }

    private static AsyncServerStreamingCall<TResponse> MakeServerStreamingCall<TResponse>(
        IAsyncStreamReader<TResponse> reader)
    {
        return new AsyncServerStreamingCall<TResponse>(
            reader,
            Task.FromResult(new Metadata()),
            () => Status.DefaultSuccess,
            () => new Metadata(),
            () => { });
    }

    private static async Task<List<TEvent>> CollectAsync<TEvent>(IAsyncEnumerable<TEvent> source)
    {
        var list = new List<TEvent>();
        await foreach (var item in source)
        {
            list.Add(item);
        }
        return list;
    }

    /// <summary>
    /// In-memory <see cref="IAsyncStreamReader{T}"/> for testing — yields a
    /// fixed sequence then optionally throws on next <c>MoveNext</c>.
    /// </summary>
    private sealed class FakeStreamReader<T> : IAsyncStreamReader<T>
    {
        private readonly IReadOnlyList<T> _items;
        private readonly Exception? _afterItemsException;
        private int _index = -1;
        private T _current = default!;

        public FakeStreamReader(IEnumerable<T> items, Exception? afterItemsException = null)
        {
            _items = items.ToList();
            _afterItemsException = afterItemsException;
        }

        public T Current => _current;

        public Task<bool> MoveNext(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            _index++;
            if (_index < _items.Count)
            {
                _current = _items[_index];
                return Task.FromResult(true);
            }

            if (_afterItemsException is not null)
            {
                return Task.FromException<bool>(_afterItemsException);
            }

            return Task.FromResult(false);
        }
    }

    internal sealed record FooBar(string Owner) : ITemplate
    {
        public static RuntimeIdentifier TemplateId { get; } = new("test-pkg", "Murmures.Foo", "FooBar");
        public static string PackageId => "test-pkg";
        public static string PackageName => "test-package";
        public static Version PackageVersion { get; } = new(0, 1, 0);

        public DamlRecord ToRecord() => DamlRecord.Create(
            DamlField.Create("owner", new DamlParty(Owner)));
    }
}
