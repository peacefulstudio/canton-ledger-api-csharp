// Copyright (c) 2026 Peaceful Studio OÜ. All rights reserved.

using Canton.Ledger.Pqs.Client;
using FluentAssertions;
using Npgsql;
using Xunit;

namespace Canton.Ledger.Pqs.Client.Tests;

public class PqsClientTests
{
    // ──────────────────────────────────────────────────────────────
    // IsTemplateNotFoundError
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public void is_template_not_found_error_returns_true_for_matching_exception()
    {
        var ex = CreatePostgresException("P0001", "Identifier not found: test-package:Module:Template");
        PqsClient.IsTemplateNotFoundError(ex).Should().BeTrue();
    }

    [Fact]
    public void is_template_not_found_error_returns_false_for_different_sql_state()
    {
        var ex = CreatePostgresException("42P01", "Identifier not found: test");
        PqsClient.IsTemplateNotFoundError(ex).Should().BeFalse();
    }

    [Fact]
    public void is_template_not_found_error_returns_false_for_different_message()
    {
        var ex = CreatePostgresException("P0001", "Some other error");
        PqsClient.IsTemplateNotFoundError(ex).Should().BeFalse();
    }

    // ──────────────────────────────────────────────────────────────
    // BuildFilteredQuery
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public void build_filtered_query_simple_field_produces_correct_query()
    {
        var filter = Filter.Field<FilterTests.SampleTemplate>(t => t.Initiator, "party1");
        var (sql, parameters) = PqsClient.BuildFilteredQuery(filter);

        sql.Should().Contain("FROM active(@templateId)");
        sql.Should().Contain("WHERE payload->>'initiator' = @p0");
        parameters.Should().ContainSingle()
            .Which.Should().Be(("@p0", "party1"));
    }

    [Fact]
    public void build_filtered_query_or_filter_produces_correct_query()
    {
        var filter = Filter.Or(
            Filter.Field<FilterTests.SampleTemplate>(t => t.Initiator, "party1"),
            Filter.Field<FilterTests.SampleTemplate>(t => t.Counterparty, "party1"));

        var (sql, parameters) = PqsClient.BuildFilteredQuery(filter);

        sql.Should().Contain("WHERE (payload->>'initiator' = @p0 OR payload->>'counterparty' = @p1)");
        parameters.Should().HaveCount(2);
        parameters[0].Should().Be(("@p0", "party1"));
        parameters[1].Should().Be(("@p1", "party1"));
    }

    // ──────────────────────────────────────────────────────────────
    // Helper
    // ──────────────────────────────────────────────────────────────

    private static PostgresException CreatePostgresException(string sqlState, string messageText)
    {
        return new PostgresException(messageText, severity: "ERROR", invariantSeverity: "ERROR", sqlState: sqlState);
    }
}
