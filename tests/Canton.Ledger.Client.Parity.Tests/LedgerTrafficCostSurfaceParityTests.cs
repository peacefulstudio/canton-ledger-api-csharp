// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Reflection;
using AwesomeAssertions;
using Canton.Ledger.Abstractions;
using Canton.Ledger.Grpc.Client;
using Canton.Ledger.Rest.Client;
using Canton.Ledger.Testing;
using Xunit;
using RuntimeCommands = Daml.Runtime.Commands;

namespace Canton.Ledger.Client.Parity.Tests;

/// <summary>
/// Surface parity for traffic-cost estimation. Every client that serves it does so as an
/// <see cref="ICantonLedgerClient"/> member, so the one signature and the one shared
/// <see cref="TrafficCostEstimate"/> are enforced by the compiler rather than by convention. What
/// these rows guard is that the arrangement stays that way: that the capability is reachable through
/// the DI-registered interface without downcasting to a concrete client, and that no client grows a
/// same-named twin alongside the interface member. The behaviour behind the signature is covered
/// behaviourally by <see cref="LedgerTrafficCostParityTests"/> and by twin tests in the
/// per-transport unit suites.
/// </summary>
public sealed class LedgerTrafficCostSurfaceParityTests
{
    private const string MethodName = "EstimateTrafficCostAsync";

    public static TheoryData<Type> ClientsServingTrafficCostEstimation() =>
        new() { typeof(LedgerClient), typeof(RestLedgerClient), typeof(FakeLedgerClient) };

    [Theory]
    [MemberData(nameof(ClientsServingTrafficCostEstimation))]
    public void EstimateTrafficCostAsync_returns_the_shared_TrafficCostEstimate_on_every_transport(Type client)
    {
        var estimating = Estimator(client);

        estimating.ReturnType.Should().Be<Task<TrafficCostEstimate?>>();
        typeof(TrafficCostEstimate).Assembly.Should().BeSameAs(typeof(ICantonLedgerClient).Assembly);
    }

    [Theory]
    [MemberData(nameof(ClientsServingTrafficCostEstimation))]
    public void EstimateTrafficCostAsync_takes_the_same_submission_deadline_and_token_on_every_transport(Type client)
    {
        var parameters = Estimator(client).GetParameters();

        parameters.Select(parameter => parameter.ParameterType).Should().Equal(
            typeof(RuntimeCommands.CommandsSubmission), typeof(TimeSpan?), typeof(CancellationToken));
        parameters.Skip(1).Should().OnlyContain(parameter => parameter.IsOptional);
    }

    [Theory]
    [MemberData(nameof(ClientsServingTrafficCostEstimation))]
    public void EstimateTrafficCostAsync_is_reachable_through_ICantonLedgerClient_without_downcasting(Type client)
    {
        var declared = typeof(ICantonLedgerClient).GetMethod(MethodName);
        declared.Should().NotBeNull();
        client.Should().BeAssignableTo<ICantonLedgerClient>();

        var implementation = client.GetInterfaceMap(typeof(ICantonLedgerClient));
        var declaredIndex = Array.IndexOf(implementation.InterfaceMethods, declared);
        object implementingMethod = implementation.TargetMethods[declaredIndex];

        implementingMethod.Should().Be(
            Estimator(client),
            "the client's public method is the interface implementation itself, not a same-named twin beside it");
    }

    private static MethodInfo Estimator(Type client) =>
        client.GetMethod(MethodName, BindingFlags.Public | BindingFlags.Instance)
        ?? throw new InvalidOperationException($"{client.Name} declares no public {MethodName}.");
}
