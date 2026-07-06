// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Daml.Runtime.Data;
using Xunit;

namespace Canton.Ledger.Pqs.Client.Tests;

public class DamlFieldNameTests
{
    [Fact]
    public void Resolve_reads_wire_name_from_metadata()
    {
        DamlFieldName.Resolve<CleanFields>(x => x.Owner).Should().Be("owner");
    }

    [Fact]
    public void Resolve_returns_snake_case_wire_name_not_transformed_property()
    {
        DamlFieldName.Resolve<DivergentFields>(x => x.CreatedAt).Should().Be("created_at");
    }

    [Fact]
    public void Resolve_returns_reserved_word_escape_wire_name()
    {
        DamlFieldName.Resolve<DivergentFields>(x => x.Operator).Should().Be("operator");
    }

    [Fact]
    public void Resolve_reads_wire_name_for_value_type_property_with_boxing()
    {
        DamlFieldName.Resolve<CleanFields>(x => x.Count).Should().Be("count");
    }

    [Fact]
    public void Resolve_throws_when_DamlFieldAttribute_absent()
    {
        var act = () => DamlFieldName.Resolve<MissingMetadata>(x => x.Unmarked);
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Unmarked*")
            .WithMessage("*regenerate*");
    }

    [Fact]
    public void Resolve_throws_for_null_expression()
    {
        var act = () => DamlFieldName.Resolve<CleanFields>(null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("expression");
    }

    [Fact]
    public void Resolve_throws_on_nested_property_access()
    {
        var act = () => DamlFieldName.Resolve<NestedFields>(x => x.Inner.Owner);
        act.Should().Throw<ArgumentException>()
            .WithMessage("*Nested property access*");
    }

    [Fact]
    public void Resolve_throws_on_method_call()
    {
        var act = () => DamlFieldName.Resolve<CleanFields>(x => x.Owner.ToUpperInvariant());
        act.Should().Throw<ArgumentException>()
            .WithMessage("*simple property access*");
    }

    private sealed record CleanFields(
        [property: DamlFieldAttribute("owner")] string Owner,
        [property: DamlFieldAttribute("count")] long Count);

    private sealed record DivergentFields(
        [property: DamlFieldAttribute("created_at")] DateTimeOffset CreatedAt,
        [property: DamlFieldAttribute("operator")] string Operator);

    private sealed record MissingMetadata(string Unmarked);

    private sealed record InnerFields([property: DamlFieldAttribute("owner")] string Owner);

    private sealed record NestedFields([property: DamlFieldAttribute("inner")] InnerFields Inner);
}
