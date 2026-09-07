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

    [Fact]
    public void ICantonLedgerClient_extends_ILedgerClient()
    {
        typeof(ICantonLedgerClient).Should().BeAssignableTo<ILedgerClient>();
    }

    [Fact]
    public void Every_public_LedgerClient_member_is_declared_on_a_neutral_interface_or_named_in_a_concrete_only_allowlist()
    {
        var declaredOnTheInterface = LedgerClientMembersImplementingANeutralInterface();
        var exempt = ConcreteOnlyAllowlist();

        var unreachable = string.Join(", ", PublicLedgerClientMembers()
            .Where(member => !declaredOnTheInterface.Contains(member))
            .Select(Describe)
            .Where(signature => !exempt.Contains(signature))
            .Order());

        unreachable.Should().BeEmpty(
            "a public LedgerClient member no transport-neutral interface declares is stranded off the shipped "
            + "surface — LedgerClient is internal, so no consumer can name it to reach that member, and the "
            + "capability is reachable by nobody rather than merely reachable past the abstraction. Either declare "
            + "it on ICantonLedgerClient or on a capability interface in Canton.Ledger.Abstractions and implement it "
            + "on every transport, or add its signature to one of the concrete-only allowlists at the top of this "
            + "file so the exemption is a decision a reviewer can see");
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
    [InlineData(nameof(ICantonLedgerClient.GetUpdateTreeByOffsetAsync))]
    [InlineData(nameof(ICantonLedgerClient.TrySubmitAndWaitForTransactionTreeAsync))]
    [InlineData(nameof(ICantonLedgerClient.EstimateTrafficCostAsync))]
    public void ICantonLedgerClient_declares_the_operation_that_is_absent_from_ILedgerClient(string operation)
    {
        typeof(ICantonLedgerClient).GetMethods().Should().Contain(m => m.Name == operation,
            "the operation must be declared on the DI-registered interface or it is stranded off the shipped "
            + "surface — LedgerClient is internal, so no consumer can name it to reach the operation there");
        typeof(ILedgerClient).GetMethods().Should().NotContain(m => m.Name == operation,
            "the operation is absent from the upstream ILedgerClient — that absence is the bug ICantonLedgerClient fixes");
    }

    [Theory]
    [InlineData(nameof(ICantonLedgerClient.SubmitAsync))]
    [InlineData(nameof(ICantonLedgerClient.SubmitReassignmentAsync))]
    [InlineData(nameof(ICantonLedgerClient.GetConnectedSynchronizersAsync))]
    [InlineData(nameof(ICantonLedgerClient.GetLedgerApiVersionAsync))]
    [InlineData(nameof(ICantonLedgerClient.GetUpdateByOffsetAsync))]
    [InlineData(nameof(ICantonLedgerClient.GetUpdateByIdAsync))]
    [InlineData(nameof(ICantonLedgerClient.GetUpdateTreeByOffsetAsync))]
    public void ICantonLedgerClient_gives_every_unary_operation_an_optional_timeout_before_the_cancellation_token(
        string operation)
    {
        var parameters = DeclaredParametersOf(operation);

        parameters[^1].ParameterType.Should().Be<CancellationToken>();
        parameters[^2].ParameterType.Should().Be<TimeSpan?>(
            "a unary operation carries a bare per-call deadline immediately before the trailing token, the position "
            + "all five unary members upstream declares on ILedgerReader and ILedgerWriter use");
        parameters[^2].IsOptional.Should().BeTrue("omitting the deadline must keep the configured default");
        parameters[^2].DefaultValue.Should().BeNull();
    }

    [Theory]
    [InlineData(nameof(ICantonLedgerClient.QueryActiveAsync))]
    [InlineData(nameof(ICantonLedgerClient.CompletionStreamAsync))]
    public void ICantonLedgerClient_leaves_the_stream_shaped_operations_without_a_timeout(string operation)
    {
        DeclaredParametersOf(operation).Should().NotContain(p => p.ParameterType == typeof(TimeSpan?),
            "a subscription and a drained snapshot both inherit the streamer's no-timeout contract, matching the "
            + "eight streaming members upstream declares on ILedgerStreamer; a caller wanting a deadline on either "
            + "reaches for CancellationTokenSource.CancelAfter instead");
    }

    private static ParameterInfo[] DeclaredParametersOf(string operation) =>
        typeof(ICantonLedgerClient).GetMethods().Single(m => m.Name == operation).GetParameters();

    [Fact]
    public async Task ICantonLedgerClient_is_mockable_without_the_concrete_LedgerClient()
    {
        ICantonLedgerClient client = Substitute.For<ICantonLedgerClient>();
        client.GetLedgerApiVersionAsync(cancellationToken: Arg.Any<CancellationToken>()).Returns("3.5.9");

        var version = await client.GetLedgerApiVersionAsync(cancellationToken: TestContext.Current.CancellationToken);

        version.Should().Be("3.5.9");
    }

    private static HashSet<string> ConcreteOnlyAllowlist() =>
    [
        .. NotLedgerCapabilitiesButGrpcTransportPlumbing,
        .. OverloadsOfCapabilitiesTheInterfaceAlreadyDeclares,
    ];

    private static MethodInfo[] PublicLedgerClientMembers() =>
        typeof(LedgerClient).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);

    private static HashSet<MethodInfo> LedgerClientMembersImplementingANeutralInterface() =>
    [
        .. new[] { typeof(ICantonLedgerClient), typeof(IUnboundedStreamingCapability) }
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
