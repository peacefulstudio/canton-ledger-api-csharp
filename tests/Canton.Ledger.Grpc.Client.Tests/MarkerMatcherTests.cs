// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Canton.Ledger.Testing.Helpers;
using Daml.Runtime;
using Daml.Runtime.Contracts;
using Xunit;

namespace Canton.Ledger.Grpc.Client.Tests;

public class MarkerMatcherTests
{
    [Fact]
    public void ResolveMarkerIdentity_throws_when_marker_is_neither_interface_nor_template()
    {
        var act = () => MarkerMatcher<NeitherMarker>.StreamFilterIdentifier();

        act.Should().Throw<TypeInitializationException>()
            .WithInnerException<InvalidOperationException>()
            .WithMessage("*is neither IDamlInterface nor ITemplate*");
    }

    [Fact]
    public void StreamFilterIdentifier_resolves_a_marker_whose_statics_are_explicit_interface_implementations()
    {
        var filter = MarkerMatcher<IViewedInterfaceMarker>.StreamFilterIdentifier();

        filter.PackageId.Should().Be("#viewed-token-api");
        filter.ModuleName.Should().Be("Token.Api");
        filter.EntityName.Should().Be("IViewedHolding");
    }

    [Fact]
    public void IsInterface_is_true_for_a_marker_whose_statics_are_explicit_interface_implementations()
    {
        MarkerMatcher<IViewedInterfaceMarker>.IsInterface.Should().BeTrue();
    }

    internal sealed record NeitherMarker : IDamlType
    {
        public static DamlTypeDescriptor DamlTypeId =>
            throw new NotSupportedException(
                "NeitherMarker is a degenerate test double: it implements IDamlType but is neither a template nor an interface.");
    }
}
