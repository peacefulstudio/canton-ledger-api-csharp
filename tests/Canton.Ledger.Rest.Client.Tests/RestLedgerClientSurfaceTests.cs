// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Reflection;
using Canton.Ledger.Abstractions;
using AwesomeAssertions;
using Xunit;

namespace Canton.Ledger.Rest.Client.Tests;

/// <summary>
/// Mirrors over <c>RestLedgerClient</c> the surface gate the gRPC client keeps over <c>LedgerClient</c>:
/// a capability Canton serves on both transports belongs on <see cref="ICantonLedgerClient"/>, and the gate
/// is what stops one from being added to a single client instead. <c>RestLedgerClient</c> is internal, so a
/// public member that no transport-neutral interface declares is not merely awkward to reach — it is
/// reachable by nobody at all, carried and maintained for no caller.
/// </summary>
public class RestLedgerClientSurfaceTests
{
    /// <summary>
    /// Signatures exempt from the gate because they are extra overloads of capabilities
    /// <see cref="ICantonLedgerClient"/> already declares: the neutral contract carries only the form that
    /// takes an explicit submitter, and these drop that parameter for a submission that already names one.
    /// The capability itself is on the interface and served on every transport, so the transport-parity rule
    /// the gate enforces is satisfied; only the convenience shape is concrete-only, exactly as the gRPC gate
    /// records for the same two members.
    /// </summary>
    private static readonly string[] OverloadsOfCapabilitiesTheInterfaceAlreadyDeclares =
    [
        "SubmitAndWaitAsync(CommandsSubmission, TimeSpan?, CancellationToken)",
        "TrySubmitAndWaitForTransactionAsync(CommandsSubmission, TimeSpan?, CancellationToken)",
    ];

    [Fact]
    public void Every_public_RestLedgerClient_member_is_declared_on_a_neutral_interface_or_named_in_a_concrete_only_allowlist()
    {
        var declaredOnTheInterface = RestLedgerClientMembersImplementingANeutralInterface();
        var exempt = ConcreteOnlyAllowlist();

        var stranded = string.Join(", ", PublicRestLedgerClientMembers()
            .Where(member => !declaredOnTheInterface.Contains(member))
            .Select(Describe)
            .Where(signature => !exempt.Contains(signature))
            .Order());

        stranded.Should().BeEmpty(
            "RestLedgerClient is internal, so a public member no transport-neutral interface declares is reachable "
            + "by nobody outside this assembly — stranded surface that is carried, documented and tested while no "
            + "consumer can call it; either declare it on ICantonLedgerClient or on a capability interface in "
            + "Canton.Ledger.Abstractions and implement it on every transport, or add its signature to the "
            + "concrete-only allowlist at the top of this file so the exemption is a decision a reviewer can see");
    }

    [Fact]
    public void Every_concrete_only_allowlist_entry_still_matches_a_public_RestLedgerClient_member()
    {
        var declared = PublicRestLedgerClientMembers().Select(Describe).ToHashSet(StringComparer.Ordinal);

        var stale = string.Join(", ", ConcreteOnlyAllowlist().Except(declared).Order());

        stale.Should().BeEmpty(
            "an allowlist entry that matches no RestLedgerClient member exempts nothing and hides the fact that the "
            + "member it was written for has been renamed, promoted, or removed");
    }

    private static HashSet<string> ConcreteOnlyAllowlist() =>
    [
        .. OverloadsOfCapabilitiesTheInterfaceAlreadyDeclares,
    ];

    private static MethodInfo[] PublicRestLedgerClientMembers() =>
        typeof(RestLedgerClient).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);

    private static HashSet<MethodInfo> RestLedgerClientMembersImplementingANeutralInterface() =>
    [
        .. new[] { typeof(ICantonLedgerClient), typeof(IUnboundedStreamingCapability) }
            .Concat(typeof(ICantonLedgerClient).GetInterfaces())
            .SelectMany(contract => typeof(RestLedgerClient).GetInterfaceMap(contract).TargetMethods),
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
