// Copyright (c) 2026 Peaceful Studio OÜ. All rights reserved.

using Canton.Ledger.Pqs.Client;
using FluentAssertions;
using Xunit;

namespace Canton.Ledger.Pqs.Client.Tests;

public class FieldNameResolverTests
{
    // ──────────────────────────────────────────────────────────────
    // ToCamelCase — Daml field name convention
    // ──────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("Initiator", "initiator")]
    [InlineData("Counterparty", "counterparty")]
    [InlineData("AgreementId", "agreementId")]
    [InlineData("SwapsExecuted", "swapsExecuted")]
    [InlineData("BaseAsset", "baseAsset")]
    [InlineData("TotalBaseAmount", "totalBaseAmount")]
    [InlineData("PricePerUnit", "pricePerUnit")]
    [InlineData("SwapIntervalSeconds", "swapIntervalSeconds")]
    [InlineData("EarlyCancellationFeeBps", "earlyCancellationFeeBps")]
    [InlineData("MinSwapsBeforeCancellation", "minSwapsBeforeCancellation")]
    [InlineData("CreatedAt", "createdAt")]
    [InlineData("ExpiresAt", "expiresAt")]
    [InlineData("NumSwaps", "numSwaps")]
    [InlineData("Status", "status")]
    [InlineData("Platform", "platform")]
    [InlineData("MarketId", "marketId")]
    public void to_camel_case_converts_pascal_case_correctly(string input, string expected)
    {
        FieldNameResolver.ToCamelCase(input).Should().Be(expected);
    }

    [Theory]
    [InlineData("", "")]
    [InlineData("a", "a")]
    [InlineData("already", "already")]
    public void to_camel_case_preserves_already_camel_case(string input, string expected)
    {
        FieldNameResolver.ToCamelCase(input).Should().Be(expected);
    }

    [Fact]
    public void to_camel_case_handles_all_caps_acronym()
    {
        FieldNameResolver.ToCamelCase("ID").Should().Be("id");
        FieldNameResolver.ToCamelCase("PQS").Should().Be("pqs");
    }

    [Fact]
    public void to_camel_case_handles_acronym_followed_by_word()
    {
        FieldNameResolver.ToCamelCase("PQSClient").Should().Be("pqsClient");
    }

    [Fact]
    public void to_camel_case_handles_single_uppercase_character()
    {
        FieldNameResolver.ToCamelCase("A").Should().Be("a");
    }

    // ──────────────────────────────────────────────────────────────
    // Expression resolution
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public void resolve_extracts_string_property()
    {
        var result = FieldNameResolver.Resolve<TestTemplate>(t => t.Name);
        result.Should().Be("name");
    }

    [Fact]
    public void resolve_extracts_value_type_property_with_boxing()
    {
        var result = FieldNameResolver.Resolve<TestTemplate>(t => t.Count);
        result.Should().Be("count");
    }

    [Fact]
    public void resolve_throws_on_complex_expression()
    {
        var act = () => FieldNameResolver.Resolve<TestTemplate>(t => t.Name + "suffix");
        act.Should().Throw<ArgumentException>()
            .WithMessage("*simple property access*");
    }

    [Fact]
    public void resolve_throws_on_method_call()
    {
        var act = () => FieldNameResolver.Resolve<TestTemplate>(t => t.Name.ToUpperInvariant());
        act.Should().Throw<ArgumentException>()
            .WithMessage("*simple property access*");
    }

    [Fact]
    public void resolve_throws_on_nested_property_access()
    {
        var act = () => FieldNameResolver.Resolve<NestedTemplate>(t => t.Inner.Name);
        act.Should().Throw<ArgumentException>()
            .WithMessage("*Nested property access*");
    }

    // ──────────────────────────────────────────────────────────────
    // Test helpers
    // ──────────────────────────────────────────────────────────────

    private record TestTemplate(string Name, long Count);
    private record InnerRecord(string Name);
    private record NestedTemplate(InnerRecord Inner);
}
