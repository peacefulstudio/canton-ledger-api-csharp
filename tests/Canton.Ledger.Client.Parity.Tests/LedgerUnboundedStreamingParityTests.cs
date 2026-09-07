// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Canton.Ledger.Abstractions;
using Canton.Ledger.Grpc.Client;
using Canton.Ledger.Rest.Client;
using Canton.Ledger.Testing;
using Daml.Runtime;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using RuntimeCommands = Daml.Runtime.Commands;
using RuntimeIdentifier = Daml.Runtime.Data.Identifier;

namespace Canton.Ledger.Client.Parity.Tests;

/// <summary>
/// Surface parity for the open-ended-tail capability probe. A consumer holding the DI-registered
/// <see cref="ICantonLedgerClient"/> must be able to learn whether an unbounded
/// <c>SubscribeAsync(toOffset: null)</c> will throw without naming a concrete client, and the answer
/// must agree with what the call actually does. These rows assert the pairing, not the constant:
/// every client is reached through the container as the neutral interface, so no row names a
/// concrete client type.
/// </summary>
public sealed class LedgerUnboundedStreamingParityTests
{
    private static readonly Party Alice = new("party::alice");
    private static readonly RuntimeCommands.SubmitterInfo AliceSubmitter =
        new(new HashSet<Party> { Alice }, new HashSet<Party>());

    public static TheoryData<string> RegisteredTransports() => ["rest", "grpc", "fake"];

    [Theory]
    [MemberData(nameof(RegisteredTransports))]
    public void The_probe_is_reachable_from_the_registered_interface_without_naming_a_concrete_client(
        string transport)
    {
        using var provider = ContainerFor(transport);

        var client = provider.GetRequiredService<ICantonLedgerClient>();

        client.Should().BeAssignableTo<IUnboundedStreamingCapability>(
            "a capability reachable only by downcasting to a concrete client is not reachable through DI");
    }

    [Theory]
    [MemberData(nameof(RegisteredTransports))]
    public async Task The_probe_agrees_with_whether_an_unbounded_subscription_throws(string transport)
    {
        using var provider = ContainerFor(transport);
        var client = provider.GetRequiredService<ICantonLedgerClient>();
        var probe = (IUnboundedStreamingCapability)client;

        var unboundedSubscription = () => client.SubscribeAsync<Holding>(
            AliceSubmitter, toOffset: null, cancellationToken: TestContext.Current.CancellationToken);

        if (!probe.SupportsUnboundedStreaming)
        {
            unboundedSubscription.Should().Throw<NotSupportedException>();
            return;
        }

        unboundedSubscription.Should().NotThrow();
        await FirstStepOf(unboundedSubscription()).Should().NotThrowAsync<NotSupportedException>();
    }

    [Fact]
    public void The_probe_is_declared_on_a_transport_neutral_interface_rather_than_on_ICantonLedgerClient()
    {
        typeof(IUnboundedStreamingCapability).Assembly.Should().BeSameAs(typeof(ICantonLedgerClient).Assembly);
        typeof(ICantonLedgerClient).Should().NotBeAssignableTo<IUnboundedStreamingCapability>(
            "widening the client contract would break every external implementor");
    }

    private static Func<Task> FirstStepOf<TEvent>(IAsyncEnumerable<TEvent> subscription) => async () =>
    {
        await using var reader = subscription.GetAsyncEnumerator(TestContext.Current.CancellationToken);
        await reader.MoveNextAsync();
    };

    private static ServiceProvider ContainerFor(string transport)
    {
        var services = new ServiceCollection();
        switch (transport)
        {
            case "rest":
                services.AddRestLedgerClient(options => options.HttpAddress = "http://localhost:7575");
                break;
            case "grpc":
                services.AddLedgerClient(options => options.GrpcAddress = "https://localhost:5001");
                break;
            default:
                services.AddSingleton<ICantonLedgerClient>(
                    FakeLedgerClient.Create().WithContractEvents<Holding>().Build());
                break;
        }

        return services.BuildServiceProvider();
    }

    private sealed record Holding : ITemplate, IDamlRecord<Holding>
    {
        public static RuntimeIdentifier TemplateId { get; } = new("pkg", "Module", "Holding");
        public static string PackageId => "pkg";
        public static string PackageName => "pkg-name";
        public static Version PackageVersion { get; } = new(0, 1, 0);
        public static DamlTypeDescriptor DamlTypeId { get; } = new(TemplateId, DamlTypeKind.Template, PackageName);
        public DamlRecord ToRecord() => new(TemplateId, [new DamlField("owner", Alice.ToDamlValue())]);

        public static Holding FromRecord(DamlRecord record) =>
            new();
    }
}
