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

    [Fact]
    public void ClassifiedCategory_is_null_for_an_Unstructured_failure_nothing_classified()
    {
        new ParsedLedgerError.Unstructured("network down", 503).ClassifiedCategory.Should().BeNull();
    }

    [Fact]
    public void ClassifiedCategory_keeps_the_category_an_Unstructured_failure_recovered()
    {
        var recovered = new ParsedLedgerError.Unstructured(
            "a security-sensitive error has been received",
            401,
            DamlErrorCategory.AuthInterceptorInvalidAuthenticationCredentials);

        recovered.ClassifiedCategory.Should()
            .Be(DamlErrorCategory.AuthInterceptorInvalidAuthenticationCredentials);
    }

    [Fact]
    public void ClassifiedCategory_reduces_a_Structured_error_the_classifier_did_not_recognise_to_null()
    {
        var structured = new ParsedLedgerError.Structured(
            DamlErrorCategory.Unknown, "SOMETHING_NEW", "boom", new Dictionary<string, string>(), 500);

        structured.ClassifiedCategory.Should().BeNull();
    }

    [Fact]
    public void ClassifiedCategory_keeps_the_category_a_Structured_error_named()
    {
        var structured = new ParsedLedgerError.Structured(
            DamlErrorCategory.ContentionOnSharedResources,
            "DUPLICATE_COMMAND",
            "already submitted",
            new Dictionary<string, string>(),
            409);

        structured.ClassifiedCategory.Should().Be(DamlErrorCategory.ContentionOnSharedResources);
    }

    [Fact]
    public void ReportedErrorId_is_null_for_an_Unstructured_failure()
    {
        new ParsedLedgerError.Unstructured("network down", 503).ReportedErrorId.Should().BeNull();
    }

    [Fact]
    public void ReportedErrorId_keeps_the_error_id_a_Structured_error_named()
    {
        var structured = new ParsedLedgerError.Structured(
            DamlErrorCategory.ContentionOnSharedResources,
            "STALE_STREAM_AUTHORIZATION",
            "the stream was aborted because the user's rights changed",
            new Dictionary<string, string>(),
            409);

        structured.ReportedErrorId.Should().Be("STALE_STREAM_AUTHORIZATION");
    }

    [Fact]
    public void ReportedErrorId_reduces_a_Structured_error_that_named_no_id_to_null()
    {
        var structured = new ParsedLedgerError.Structured(
            DamlErrorCategory.ContentionOnSharedResources,
            string.Empty,
            "boom",
            new Dictionary<string, string>(),
            409);

        structured.ReportedErrorId.Should().BeNull();
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
    public void Unstructured_carries_the_transport_status_and_message_and_nothing_else()
    {
        var parsed = new ParsedLedgerError.Unstructured("service unavailable", 503);

        parsed.Category.Should().BeNull();
        parsed.Message.Should().Be("service unavailable");
        parsed.StatusCode.Should().Be(503);
    }

    [Fact]
    public void Unstructured_substitutes_an_empty_message_for_a_null_one()
    {
        new ParsedLedgerError.Unstructured(null, 500).Message.Should().BeEmpty();
    }
}
