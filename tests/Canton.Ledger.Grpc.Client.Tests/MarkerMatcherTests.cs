// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Runtime;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using AwesomeAssertions;
using Xunit;
using RuntimeIdentifier = Daml.Runtime.Data.Identifier;

namespace Canton.Ledger.Grpc.Client.Tests;

public class MarkerMatcherTests
{
    [Fact]
    public void ResolveMarkerIdentity_uses_InterfaceId_for_interface_marker()
    {
        MarkerMatcher<InterfaceMarker>.IsInterface.Should().BeTrue();

        var identifier = MarkerMatcher<InterfaceMarker>.StreamFilterIdentifier();

        identifier.ModuleName.Should().Be("Token.Api");
        identifier.EntityName.Should().Be("IHolding");
    }

    [Fact]
    public void ResolveMarkerIdentity_uses_TemplateId_for_template_marker()
    {
        MarkerMatcher<TemplateMarker>.IsInterface.Should().BeFalse();

        var identifier = MarkerMatcher<TemplateMarker>.StreamFilterIdentifier();

        identifier.ModuleName.Should().Be("Sample.Token");
        identifier.EntityName.Should().Be("Holding");
    }

    [Fact]
    public void ResolveMarkerIdentity_throws_when_marker_is_neither_interface_nor_template()
    {
        var act = () => MarkerMatcher<NeitherMarker>.StreamFilterIdentifier();

        act.Should().Throw<TypeInitializationException>()
            .WithInnerException<InvalidOperationException>()
            .WithMessage("*is neither IDamlInterface nor ITemplate*");
    }

    internal sealed record InterfaceMarker : IDamlInterface
    {
        public static RuntimeIdentifier InterfaceId { get; } = new("iface-pkg", "Token.Api", "IHolding");
        public static string PackageId => "iface-pkg";
        public static string PackageName => "token-api";
        public static Version PackageVersion { get; } = new(0, 1, 0);
        public static DamlTypeDescriptor DamlTypeId { get; } = new(InterfaceId, DamlTypeKind.Interface, PackageName);

        public DamlRecord ToRecord() => DamlRecord.Create();
    }

    internal sealed record TemplateMarker(string Owner) : ITemplate
    {
        public static RuntimeIdentifier TemplateId { get; } = new("tmpl-pkg", "Sample.Token", "Holding");
        public static string PackageId => "tmpl-pkg";
        public static string PackageName => "token-impl";
        public static Version PackageVersion { get; } = new(0, 1, 0);
        public static DamlTypeDescriptor DamlTypeId { get; } = new(TemplateId, DamlTypeKind.Template, PackageName);

        public DamlRecord ToRecord() => DamlRecord.Create(
            DamlField.Create("owner", new DamlParty(Owner)));
    }

    internal sealed record NeitherMarker : IDamlType
    {
        public static DamlTypeDescriptor DamlTypeId =>
            throw new NotSupportedException(
                "NeitherMarker is a degenerate test double: it implements IDamlType but is neither a template nor an interface.");
    }
}
