// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Reflection;
using Canton.Ledger.Abstractions;
using Daml.Ledger.Abstractions;
using AwesomeAssertions;
using NSubstitute;
using Xunit;

namespace Canton.Ledger.Grpc.Client.Tests;

public class ICantonLedgerClientTests
{
    private static readonly string[] NotLedgerCapabilitiesButGrpcTransportPlumbing =
    [
        "CreateCallInvoker()",
    ];

    private static readonly string[] OverloadsOfCapabilitiesTheInterfaceAlreadyDeclares =
    [
        "SubmitAndWaitAsync(CommandsSubmission, TimeSpan?, CancellationToken)",
        "TrySubmitAndWaitForTransactionAsync(CommandsSubmission, TimeSpan?, CancellationToken)",
    ];

    private static readonly string[] CapabilitiesAwaitingAPromotionDecision =
    [
        "TryExerciseForCreatedAsync<TMarker>(ExerciseCommand, SubmitterInfo, String, TimeSpan?, CancellationToken)",
    ];

    [Fact]
    public void ICantonLedgerClient_extends_ILedgerClient()
    {
        typeof(ICantonLedgerClient).Should().BeAssignableTo<ILedgerClient>();
    }

    [Fact]
    public void Every_public_LedgerClient_member_is_declared_on_ICantonLedgerClient_or_named_in_a_concrete_only_allowlist()
    {
        var declaredOnTheInterface = LedgerClientMembersImplementingICantonLedgerClient();
        var exempt = ConcreteOnlyAllowlist();

        var unreachable = string.Join(", ", PublicLedgerClientMembers()
            .Where(member => !declaredOnTheInterface.Contains(member))
            .Select(Describe)
            .Where(signature => !exempt.Contains(signature))
            .Order());

        unreachable.Should().BeEmpty(
            "a public LedgerClient member ICantonLedgerClient does not declare is reachable only by downcasting past "
            + "the DI-registered abstraction, so either declare it on the interface and implement it on every "
            + "transport, or add its signature to one of the concrete-only allowlists at the top of this file so the "
            + "exemption is a decision a reviewer can see");
    }

    [Fact]
    public void Every_concrete_only_allowlist_entry_still_matches_a_public_LedgerClient_member()
    {
        var declared = PublicLedgerClientMembers().Select(Describe).ToHashSet(StringComparer.Ordinal);

        var stale = string.Join(", ", ConcreteOnlyAllowlist().Except(declared).Order());

        stale.Should().BeEmpty(
            "an allowlist entry that matches no LedgerClient member exempts nothing and hides the fact that the "
            + "member it was written for has been renamed, promoted, or removed");
    }

    [Theory]
    [InlineData(nameof(ICantonLedgerClient.SubmitAsync))]
    [InlineData(nameof(ICantonLedgerClient.CompletionStreamAsync))]
    [InlineData(nameof(ICantonLedgerClient.GetConnectedSynchronizersAsync))]
    [InlineData(nameof(ICantonLedgerClient.GetLedgerApiVersionAsync))]
    [InlineData(nameof(ICantonLedgerClient.GetUpdateByOffsetAsync))]
    [InlineData(nameof(ICantonLedgerClient.GetUpdateByIdAsync))]
    [InlineData(nameof(ICantonLedgerClient.TrySubmitAndWaitForTransactionTreeAsync))]
    [InlineData(nameof(ICantonLedgerClient.EstimateTrafficCostAsync))]
    public void ICantonLedgerClient_declares_the_operation_that_is_absent_from_ILedgerClient(string operation)
    {
        typeof(ICantonLedgerClient).GetMethods().Should().Contain(m => m.Name == operation,
            "the operation must be reachable through the DI-registered interface without downcasting to LedgerClient");
        typeof(ILedgerClient).GetMethods().Should().NotContain(m => m.Name == operation,
            "the operation is absent from the upstream ILedgerClient — that absence is the bug ICantonLedgerClient fixes");
    }

    [Fact]
    public async Task ICantonLedgerClient_is_mockable_without_the_concrete_LedgerClient()
    {
        ICantonLedgerClient client = Substitute.For<ICantonLedgerClient>();
        client.GetLedgerApiVersionAsync(Arg.Any<CancellationToken>()).Returns("3.5.9");

        var version = await client.GetLedgerApiVersionAsync(TestContext.Current.CancellationToken);

        version.Should().Be("3.5.9");
    }

    private static HashSet<string> ConcreteOnlyAllowlist() =>
    [
        .. NotLedgerCapabilitiesButGrpcTransportPlumbing,
        .. OverloadsOfCapabilitiesTheInterfaceAlreadyDeclares,
        .. CapabilitiesAwaitingAPromotionDecision,
    ];

    private static MethodInfo[] PublicLedgerClientMembers() =>
        typeof(LedgerClient).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);

    private static HashSet<MethodInfo> LedgerClientMembersImplementingICantonLedgerClient() =>
    [
        .. new[] { typeof(ICantonLedgerClient) }
            .Concat(typeof(ICantonLedgerClient).GetInterfaces())
            .SelectMany(contract => typeof(LedgerClient).GetInterfaceMap(contract).TargetMethods),
    ];

    private static string Describe(MethodInfo member)
    {
        var typeArguments = member.IsGenericMethodDefinition
            ? $"<{string.Join(", ", member.GetGenericArguments().Select(argument => argument.Name))}>"
            : string.Empty;
        var parameters = string.Join(", ", member.GetParameters().Select(p => Describe(p.ParameterType)));
        return $"{member.Name}{typeArguments}({parameters})";
    }

    private static string Describe(Type type)
    {
        var underlying = Nullable.GetUnderlyingType(type);
        if (underlying is not null)
            return $"{Describe(underlying)}?";

        if (!type.IsGenericType)
            return type.Name;

        var name = type.Name[..type.Name.IndexOf('`', StringComparison.Ordinal)];
        return $"{name}<{string.Join(", ", type.GetGenericArguments().Select(Describe))}>";
    }
}
