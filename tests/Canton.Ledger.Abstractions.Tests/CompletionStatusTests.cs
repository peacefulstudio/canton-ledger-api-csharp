// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Xunit;

namespace Canton.Ledger.Abstractions.Tests;

public class CompletionStatusTests
{
    [Fact]
    public void Two_statuses_carrying_equal_metadata_in_separate_maps_are_equal()
    {
        var first = new CompletionStatus(
            11,
            "the contract was not found",
            "CONTRACT_NOT_FOUND",
            new Dictionary<string, string>(StringComparer.Ordinal) { ["category"] = "11" });
        var second = new CompletionStatus(
            11,
            "the contract was not found",
            "CONTRACT_NOT_FOUND",
            new Dictionary<string, string>(StringComparer.Ordinal) { ["category"] = "11" });

        first.Should().Be(second);
        first.GetHashCode().Should().Be(second.GetHashCode());
    }

    [Fact]
    public void Metadata_insertion_order_does_not_change_equality_or_the_hash_code()
    {
        var first = new CompletionStatus(
            11,
            "the contract was not found",
            "CONTRACT_NOT_FOUND",
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["category"] = "11",
                ["definite_answer"] = "false",
            });
        var second = new CompletionStatus(
            11,
            "the contract was not found",
            "CONTRACT_NOT_FOUND",
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["definite_answer"] = "false",
                ["category"] = "11",
            });

        first.Should().Be(second);
        first.GetHashCode().Should().Be(second.GetHashCode());
    }

    [Fact]
    public void A_status_whose_metadata_differs_is_not_equal()
    {
        var first = new CompletionStatus(
            11,
            "the contract was not found",
            "CONTRACT_NOT_FOUND",
            new Dictionary<string, string>(StringComparer.Ordinal) { ["category"] = "11" });
        var second = new CompletionStatus(
            11,
            "the contract was not found",
            "CONTRACT_NOT_FOUND",
            new Dictionary<string, string>(StringComparer.Ordinal) { ["category"] = "8" });

        first.Should().NotBe(second);
    }

    [Fact]
    public void A_status_whose_error_id_differs_is_not_equal()
    {
        var first = new CompletionStatus(3, "no", "CONTRACT_NOT_FOUND", NoMetadata());
        var second = new CompletionStatus(3, "no", "PARTY_NOT_KNOWN_ON_LEDGER", NoMetadata());

        first.Should().NotBe(second);
    }

    [Fact]
    public void A_null_metadata_argument_is_read_as_an_empty_map()
    {
        var status = new CompletionStatus(3, "no", ErrorId: null, Metadata: null!);

        status.Metadata.Should().BeEmpty();
        status.Should().Be(new CompletionStatus(3, "no", ErrorId: null, NoMetadata()));
    }

    [Fact]
    public void A_null_metadata_on_a_with_expression_is_read_as_an_empty_map()
    {
        var status = new CompletionStatus(
            3,
            "no",
            "CONTRACT_NOT_FOUND",
            new Dictionary<string, string>(StringComparer.Ordinal) { ["category"] = "11" });

        var without = status with { Metadata = null! };

        without.Metadata.Should().BeEmpty();
    }

    private static IReadOnlyDictionary<string, string> NoMetadata() =>
        new Dictionary<string, string>(0, StringComparer.Ordinal);
}
