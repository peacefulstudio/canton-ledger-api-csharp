// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Canton.Ledger.Abstractions;
using Canton.Ledger.Grpc.Client;
using Canton.Ledger.Rest.Client;
using Canton.Ledger.Testing;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Microsoft.Extensions.DependencyInjection;
using RichTypes;
using Xunit;
using RuntimeCommands = Daml.Runtime.Commands;

namespace Canton.Ledger.Client.Parity.Tests;

/// <summary>
/// Behavioural parity for argument validation on the neutral <see cref="ICantonLedgerClient"/>
/// surface. A caller passing <see langword="null"/> to a member that declares a non-nullable
/// reference parameter must get the same diagnosis whatever transport is behind the interface: an
/// <see cref="ArgumentNullException"/> naming the offending parameter, raised before a
/// <see cref="Task"/> exists so the failure is attributable to the call site rather than surfacing
/// as a faulted task at some later await.
/// </summary>
/// <remarks>
/// These rows need no participant: a guard fires ahead of every transport call, so the gRPC and
/// REST clients here are pointed at addresses nothing listens on and never reach them. That is the
/// point — a row that needed a live ledger could not run in the unit lane, which is how this
/// divergence survived unnoticed.
/// </remarks>
public sealed class LedgerClientArgumentGuardParityTests
{
    private const string Grpc = "gRPC";
    private const string Rest = "REST";
    private const string Fake = "Fake";

    private static readonly Party Alice = new("party::alice");

    private static readonly RuntimeCommands.SubmitterInfo Submitter =
        new(new HashSet<Party> { Alice }, new HashSet<Party>());

    public static TheoryData<string> Transports() => new() { Grpc, Rest, Fake };

    [Theory]
    [MemberData(nameof(Transports))]
    public void SubmitAsync_rejects_a_null_submission_synchronously(string transport) =>
        AssertRejectsNull(transport, "submission", client => client.SubmitAsync(null!));

    [Theory]
    [MemberData(nameof(Transports))]
    public void SubmitReassignmentAsync_rejects_a_null_submission_synchronously(string transport) =>
        AssertRejectsNull(transport, "submission", client => client.SubmitReassignmentAsync(null!));

    [Theory]
    [MemberData(nameof(Transports))]
    public void SubmitAndWaitAsync_rejects_a_null_submission_synchronously(string transport) =>
        AssertRejectsNull(transport, "submission", client => client.SubmitAndWaitAsync(null!, Submitter));

    [Theory]
    [MemberData(nameof(Transports))]
    public void SubmitAndWaitAsync_rejects_a_null_submission_synchronously_on_the_submitterless_overload(
        string transport) =>
        AssertRejectsNull(transport, "submission", SubmitAndWaitWithoutSubmitter);

    [Theory]
    [MemberData(nameof(Transports))]
    public void TrySubmitAndWaitForTransactionAsync_rejects_a_null_submission_synchronously(string transport) =>
        AssertRejectsNull(
            transport, "submission", client => client.TrySubmitAndWaitForTransactionAsync(null!, Submitter));

    [Theory]
    [MemberData(nameof(Transports))]
    public void TrySubmitAndWaitForTransactionAsync_rejects_a_null_submission_synchronously_on_the_submitterless_overload(
        string transport) =>
        AssertRejectsNull(transport, "submission", TrySubmitAndWaitForTransactionWithoutSubmitter);

    [Theory]
    [MemberData(nameof(Transports))]
    public void TrySubmitAndWaitForTransactionTreeAsync_rejects_a_null_submission_synchronously(string transport) =>
        AssertRejectsNull(
            transport, "submission", client => client.TrySubmitAndWaitForTransactionTreeAsync(null!, Submitter));

    [Theory]
    [MemberData(nameof(Transports))]
    public void TrySubmitAndWaitForReassignmentAsync_rejects_a_null_submission_synchronously(string transport) =>
        AssertRejectsNull(
            transport, "submission", client => client.TrySubmitAndWaitForReassignmentAsync<Marker>(null!));

    [Theory]
    [MemberData(nameof(Transports))]
    public void EstimateTrafficCostAsync_rejects_a_null_submission_synchronously(string transport) =>
        AssertRejectsNull(transport, "submission", client => client.EstimateTrafficCostAsync(null!));

    [Theory]
    [MemberData(nameof(Transports))]
    public void TryExerciseAsync_rejects_a_null_command_synchronously(string transport) =>
        AssertRejectsNull(
            transport, "command", client => client.TryExerciseAsync<ContractId<Marker>>(null!, Submitter));

    [Theory]
    [MemberData(nameof(Transports))]
    public void TryCreateAsync_rejects_a_null_payload_synchronously(string transport) =>
        AssertRejectsNull(transport, "payload", client => client.TryCreateAsync<Marker>(null!, Submitter));

    [Theory]
    [MemberData(nameof(Transports))]
    public void GetUpdateByIdAsync_rejects_a_null_updateId_synchronously(string transport) =>
        AssertRejectsNull(transport, "updateId", client => client.GetUpdateByIdAsync(null!, Submitter));

    private static object SubmitAndWaitWithoutSubmitter(ICantonLedgerClient client) => client switch
    {
        LedgerClient grpc => grpc.SubmitAndWaitAsync(null!),
        RestLedgerClient rest => rest.SubmitAndWaitAsync(null!),
        FakeLedgerClient fake => fake.SubmitAndWaitAsync(null!),
        _ => throw new ArgumentOutOfRangeException(nameof(client), client, "Unknown transport."),
    };

    private static object TrySubmitAndWaitForTransactionWithoutSubmitter(ICantonLedgerClient client) => client switch
    {
        LedgerClient grpc => grpc.TrySubmitAndWaitForTransactionAsync(null!),
        RestLedgerClient rest => rest.TrySubmitAndWaitForTransactionAsync(null!),
        FakeLedgerClient fake => fake.TrySubmitAndWaitForTransactionAsync(null!),
        _ => throw new ArgumentOutOfRangeException(nameof(client), client, "Unknown transport."),
    };

    private static void AssertRejectsNull(
        string transport,
        string expectedParamName,
        Func<ICantonLedgerClient, object> call)
    {
        using var transportScope = NewClient(transport);
        var client = transportScope.Client;

        var act = () => { _ = call(client); };

        act.Should().Throw<ArgumentNullException>(
                "{0} must diagnose a null argument the same way every other transport does, before a task exists",
                transport)
            .Which.ParamName.Should().Be(expectedParamName);
    }

    private static ClientScope NewClient(string transport) => transport switch
    {
        Grpc => ClientScope.ForGrpc(),
        Rest => ClientScope.ForRest(),
        Fake => new ClientScope(FakeLedgerClient.Create().Build(), null),
        _ => throw new ArgumentOutOfRangeException(nameof(transport), transport, "Unknown transport."),
    };

    private sealed class ClientScope(ICantonLedgerClient client, IDisposable? owned) : IDisposable
    {
        public ICantonLedgerClient Client { get; } = client;

        public static ClientScope ForGrpc()
        {
            var services = new ServiceCollection()
                .AddLedgerClient(options => options.GrpcAddress = "http://127.0.0.1:1")
                .BuildServiceProvider();
            return new ClientScope(services.GetRequiredService<ICantonLedgerClient>(), services);
        }

        public static ClientScope ForRest()
        {
            var services = new ServiceCollection().AddHttpClient().BuildServiceProvider();
            var client = new RestLedgerClient(services.GetRequiredService<IHttpClientFactory>(), options: null);
            return new ClientScope(client, services);
        }

        public void Dispose() => owned?.Dispose();
    }
}
