// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using AwesomeAssertions;
using Xunit;

namespace Canton.Ledger.Rest.Client.Tests;

public class RawSurfaceContractTests
{
    private const string RawNamespace = "Canton.Ledger.Rest.Client.Raw";
    private const string ExperimentalDiagnosticId = "CANTONREST001";

    public static TheoryData<Type> RawInterfaces()
    {
        var data = new TheoryData<Type>();
        foreach (var rawInterface in DiscoverRawInterfaces())
            data.Add(rawInterface);
        return data;
    }

    [Fact]
    public void The_raw_surface_exposes_every_declared_ledger_api_interface()
    {
        DiscoverRawInterfaces().Should().HaveCount(19);
    }

    [Theory]
    [MemberData(nameof(RawInterfaces))]
    public void Every_raw_interface_lives_in_the_Raw_namespace_and_is_experimental(Type rawInterface)
    {
        rawInterface.Namespace.Should().Be(RawNamespace);
        rawInterface.GetCustomAttributesData()
            .Should().Contain(a => a.AttributeType == typeof(ExperimentalAttribute)
                && (string)a.ConstructorArguments[0].Value! == ExperimentalDiagnosticId);
    }

    private static IReadOnlyList<Type> DiscoverRawInterfaces()
    {
#pragma warning disable CANTONREST001
        var rawSurfaceAssembly = typeof(Canton.Ledger.Rest.Client.Raw.IStateServiceApi).Assembly;
#pragma warning restore CANTONREST001
        return rawSurfaceAssembly.GetTypes()
            .Where(t => t is { IsInterface: true, IsPublic: true } && t.Namespace == RawNamespace)
            .ToList();
    }
}
