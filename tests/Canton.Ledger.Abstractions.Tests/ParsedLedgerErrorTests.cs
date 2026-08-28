// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Daml.Runtime.Outcomes;
using Xunit;

namespace Canton.Ledger.Abstractions.Tests;

public class ParsedLedgerErrorTests
{
    [Theory]
    [InlineData("1", DamlErrorCategory.TransientServerFailure)]
    [InlineData("2", DamlErrorCategory.ContentionOnSharedResources)]
    [InlineData("3", DamlErrorCategory.DeadlineExceededRequestStateUnknown)]
    [InlineData("4", DamlErrorCategory.SystemInternalAssumptionViolated)]
    [InlineData("5", DamlErrorCategory.MaliciousOrFaultyBehaviour)]
    [InlineData("6", DamlErrorCategory.AuthInterceptorInvalidAuthenticationCredentials)]
    [InlineData("7", DamlErrorCategory.AuthorizationChecksFailed)]
    [InlineData("8", DamlErrorCategory.InvalidIndependentOfSystemState)]
    [InlineData("9", DamlErrorCategory.InvalidGivenCurrentSystemStateOther)]
    [InlineData("10", DamlErrorCategory.InvalidGivenCurrentSystemStateResourceExists)]
    [InlineData("11", DamlErrorCategory.InvalidGivenCurrentSystemStateResourceMissing)]
    [InlineData("12", DamlErrorCategory.InvalidGivenCurrentSystemStateSeekDifferentResource)]
    [InlineData("13", DamlErrorCategory.BackgroundProcessDegradationWarning)]
    [InlineData("14", DamlErrorCategory.InternalUnsupportedOperation)]
    public void MapCategory_maps_the_documented_numeric_category_ids_participants_send(
        string wireCategoryId, DamlErrorCategory expected)
    {
        ParsedLedgerError.MapCategory(wireCategoryId).Should().Be(expected);
    }

    [Theory]
    [InlineData("TransientServerFailure", DamlErrorCategory.TransientServerFailure)]
    [InlineData("transientserverfailure", DamlErrorCategory.TransientServerFailure)]
    [InlineData("CONTENTIONONSHAREDRESOURCES", DamlErrorCategory.ContentionOnSharedResources)]
    public void MapCategory_accepts_the_category_name_case_insensitively(
        string raw, DamlErrorCategory expected)
    {
        ParsedLedgerError.MapCategory(raw).Should().Be(expected);
    }

    [Theory]
    [InlineData("15")]
    [InlineData("50")]
    [InlineData("999")]
    [InlineData("-1")]
    [InlineData("2147483647")]
    public void MapCategory_returns_Unknown_for_a_numeric_id_outside_the_defined_categories(string raw)
    {
        ParsedLedgerError.MapCategory(raw).Should().Be(DamlErrorCategory.Unknown);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("TotallyMadeUpCategory")]
    public void MapCategory_returns_Unknown_for_an_absent_or_unrecognised_category(string? raw)
    {
        ParsedLedgerError.MapCategory(raw).Should().Be(DamlErrorCategory.Unknown);
    }

    [Theory]
    [InlineData("TransientServerFailure,ContentionOnSharedResources")]
    [InlineData("SystemInternalAssumptionViolated,TransientServerFailure")]
    [InlineData("systeminternalassumptionviolated,transientserverfailure")]
    [InlineData("InvalidIndependentOfSystemState,TransientServerFailure")]
    [InlineData("AuthorizationChecksFailed,TransientServerFailure")]
    [InlineData("Unknown,MaliciousOrFaultyBehaviour")]
    [InlineData("ContentionOnSharedResources, TransientServerFailure")]
    [InlineData("9,9")]
    [InlineData("8,")]
    [InlineData(",8")]
    public void MapCategory_returns_Unknown_for_a_category_list_rather_than_oring_its_members(
        string raw)
    {
        ParsedLedgerError.MapCategory(raw).Should().Be(DamlErrorCategory.Unknown);
    }

    [Fact]
    public void Untyped_carries_no_error_id_category_or_metadata()
    {
        var parsed = ParsedLedgerError.Untyped("service unavailable", 503);

        parsed.Category.Should().Be(DamlErrorCategory.Unknown);
        parsed.ErrorId.Should().BeEmpty();
        parsed.Message.Should().Be("service unavailable");
        parsed.Metadata.Should().BeEmpty();
        parsed.StatusCode.Should().Be(503);
    }

    [Fact]
    public void Untyped_substitutes_an_empty_message_for_a_null_one()
    {
        ParsedLedgerError.Untyped(null, 500).Message.Should().BeEmpty();
    }
}
