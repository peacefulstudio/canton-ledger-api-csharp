// Copyright (c) 2026 Peaceful Studio OÜ. All rights reserved.

using FluentAssertions;
using Npgsql;
using Xunit;

namespace Canton.Ledger.Pqs.Client.Tests;

public class PqsClientTests
{
    [Fact]
    public void DefaultJsonSerializerOptions_can_deserialize_camel_case_payload()
    {
        var json = """{"initiator":"alice","counterparty":"bob","numSwaps":"42","status":"Active"}""";

        var result = System.Text.Json.JsonSerializer.Deserialize<FilterTests.SampleTemplate>(
            json, PqsClient.DefaultJsonSerializerOptions);

        result.Should().NotBeNull();
        result!.Initiator.Should().Be("alice");
        result.Counterparty.Should().Be("bob");
        result.NumSwaps.Should().Be(42);
        result.Status.Should().Be("Active");
    }

    [Fact]
    public void IsTemplateNotFoundError_returns_true_for_matching_exception()
    {
        var ex = CreatePostgresException("P0001", "Identifier not found: test-package:Module:Template");
        PqsClient.IsTemplateNotFoundError(ex).Should().BeTrue();
    }

    [Fact]
    public void IsTemplateNotFoundError_returns_false_for_different_sql_state()
    {
        var ex = CreatePostgresException("42P01", "Identifier not found: test");
        PqsClient.IsTemplateNotFoundError(ex).Should().BeFalse();
    }

    [Fact]
    public void IsTemplateNotFoundError_returns_false_for_different_message()
    {
        var ex = CreatePostgresException("P0001", "Some other error");
        PqsClient.IsTemplateNotFoundError(ex).Should().BeFalse();
    }

    [Fact]
    public void BuildFilteredQuery_simple_field_produces_correct_query()
    {
        var filter = Filter.Field<FilterTests.SampleTemplate>(t => t.Initiator, "party1");
        var (sql, parameters) = PqsClient.BuildFilteredQuery(filter);

        sql.Should().Contain("FROM active(@templateId)");
        sql.Should().Contain("WHERE payload->>'initiator' = @p0");
        parameters.Should().ContainSingle()
            .Which.Should().Be(("@p0", "party1"));
    }

    [Fact]
    public void BuildFilteredQuery_or_filter_produces_correct_query()
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

    private static PostgresException CreatePostgresException(string sqlState, string messageText)
    {
        return new PostgresException(messageText, severity: "ERROR", invariantSeverity: "ERROR", sqlState: sqlState);
    }
}
