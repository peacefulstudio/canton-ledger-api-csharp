// Copyright © 2026 Peaceful Studio OÜ. All rights reserved.

using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Npgsql;
using Xunit;

namespace Canton.Ledger.Pqs.Client.Tests;

public class PqsClientTests
{
    private static PqsClientOptions ValidOptions() => new()
    {
        ConnectionString = "Host=localhost;Database=pqs"
    };

    [Fact]
    public void Constructor_throws_when_IOptions_is_null()
    {
        var act = () => new PqsClient((IOptions<PqsClientOptions>)null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("options");
    }

    [Fact]
    public void Constructor_throws_when_PqsClientOptions_is_null()
    {
        var act = () => new PqsClient((PqsClientOptions)null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("options");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t")]
    public void Constructor_throws_when_ConnectionString_is_empty_or_whitespace(string connectionString)
    {
        var options = new PqsClientOptions { ConnectionString = connectionString };

        var act = () => new PqsClient(options);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Constructor_with_IOptions_throws_when_ConnectionString_is_empty()
    {
        var options = Options.Create(new PqsClientOptions { ConnectionString = "" });

        var act = () => new PqsClient(options);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Constructor_succeeds_with_valid_options()
    {
        var act = () => new PqsClient(ValidOptions());
        act.Should().NotThrow();
    }

    [Fact]
    public void Constructor_with_IOptions_succeeds_with_valid_options()
    {
        var act = () => new PqsClient(Options.Create(ValidOptions()));
        act.Should().NotThrow();
    }

    [Fact]
    public void ActivitySourceName_matches_full_type_name()
    {
        PqsClient.ActivitySourceName.Should().Be(typeof(PqsClient).FullName);
        PqsClient.ActivitySourceName.Should().Be("Canton.Ledger.Pqs.Client.PqsClient");
    }

    [Fact]
    public void DefaultJsonSerializerOptions_is_read_only()
    {
        PqsClient.DefaultJsonSerializerOptions.IsReadOnly.Should().BeTrue();
    }

    [Fact]
    public void DefaultJsonSerializerOptions_mutation_throws()
    {
        var options = PqsClient.DefaultJsonSerializerOptions;

        var act = () => options.PropertyNameCaseInsensitive = false;

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void DefaultJsonSerializerOptions_returns_same_instance()
    {
        PqsClient.DefaultJsonSerializerOptions
            .Should().BeSameAs(PqsClient.DefaultJsonSerializerOptions);
    }

    [Fact]
    public void DefaultJsonSerializerOptions_can_deserialize_camel_case_payload()
    {
        var json = """{"initiator":"alice","counterparty":"bob","numSwaps":"42","status":"Active"}""";

        var result = JsonSerializer.Deserialize<FilterTests.SampleTemplate>(
            json, PqsClient.DefaultJsonSerializerOptions);

        result.Should().NotBeNull();
        result!.Initiator.Should().Be("alice");
        result.Counterparty.Should().Be("bob");
        result.NumSwaps.Should().Be(42);
        result.Status.Should().Be("Active");
    }

    [Fact]
    public void DefaultJsonSerializerOptions_deserializes_numeric_from_json_string()
    {
        var json = """{"price":"1.5"}""";

        var result = JsonSerializer.Deserialize<NumericPayload>(json, PqsClient.DefaultJsonSerializerOptions);

        result.Should().NotBeNull();
        result!.Price.Should().Be(1.5m);
    }

    [Fact]
    public void DefaultJsonSerializerOptions_deserializes_enum_from_string()
    {
        var json = """{"side":"Sell"}""";

        var result = JsonSerializer.Deserialize<EnumPayload>(json, PqsClient.DefaultJsonSerializerOptions);

        result.Should().NotBeNull();
        result!.Side.Should().Be(OrderSide.Sell);
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
    public void IsTemplateNotFoundError_returns_false_when_prefix_does_not_match_at_start()
    {
        var ex = CreatePostgresException("P0001", "Some error: Identifier not found: x");
        PqsClient.IsTemplateNotFoundError(ex).Should().BeFalse();
    }

    [Fact]
    public void BuildFilteredQuery_simple_field_produces_correct_query()
    {
        var filter = Filter.Field<FilterTests.SampleTemplate>(t => t.Initiator, "party1");
        var (sql, parameters) = PqsClient.BuildFilteredQuery(filter);

        sql.Should().Be("SELECT contract_id, payload FROM active(@templateId) WHERE payload->>'initiator' = @p0");
        parameters.Should().ContainSingle().Which.Should().Be(("@p0", "party1"));
    }

    [Fact]
    public void BuildFilteredQuery_or_filter_produces_correct_query()
    {
        var filter = Filter.Or(
            Filter.Field<FilterTests.SampleTemplate>(t => t.Initiator, "alice"),
            Filter.Field<FilterTests.SampleTemplate>(t => t.Counterparty, "bob"));

        var (sql, parameters) = PqsClient.BuildFilteredQuery(filter);

        sql.Should().Be(
            "SELECT contract_id, payload FROM active(@templateId) " +
            "WHERE (payload->>'initiator' = @p0 OR payload->>'counterparty' = @p1)");
        parameters.Should().HaveCount(2);
        parameters[0].Should().Be(("@p0", "alice"));
        parameters[1].Should().Be(("@p1", "bob"));
    }

    [Fact]
    public void BuildFilteredQuery_and_filter_produces_correct_query()
    {
        var filter = Filter.And(
            Filter.Field<FilterTests.SampleTemplate>(t => t.Initiator, "alice"),
            Filter.Field<FilterTests.SampleTemplate>(t => t.Status, "Active"));

        var (sql, parameters) = PqsClient.BuildFilteredQuery(filter);

        sql.Should().Be(
            "SELECT contract_id, payload FROM active(@templateId) " +
            "WHERE (payload->>'initiator' = @p0 AND payload->>'status' = @p1)");
        parameters.Should().HaveCount(2);
        parameters[0].Should().Be(("@p0", "alice"));
        parameters[1].Should().Be(("@p1", "Active"));
    }

    [Fact]
    public void BuildFilteredQuery_nested_filter_assigns_parameters_in_declaration_order()
    {
        var filter = Filter.And(
            Filter.Or(
                Filter.Field<FilterTests.SampleTemplate>(t => t.Initiator, "alice"),
                Filter.Field<FilterTests.SampleTemplate>(t => t.Counterparty, "bob")),
            Filter.Field<FilterTests.SampleTemplate>(t => t.Status, "Active"));

        var (sql, parameters) = PqsClient.BuildFilteredQuery(filter);

        sql.Should().Be(
            "SELECT contract_id, payload FROM active(@templateId) " +
            "WHERE ((payload->>'initiator' = @p0 OR payload->>'counterparty' = @p1) " +
            "AND payload->>'status' = @p2)");
        parameters.Should().HaveCount(3);
        parameters[0].Should().Be(("@p0", "alice"));
        parameters[1].Should().Be(("@p1", "bob"));
        parameters[2].Should().Be(("@p2", "Active"));
    }

    [Fact]
    public void BuildFilteredQuery_preserves_special_characters_as_parameter_values()
    {
        const string nasty = "alice'; DROP TABLE active; --";
        var filter = Filter.Field<FilterTests.SampleTemplate>(t => t.Initiator, nasty);

        var (sql, parameters) = PqsClient.BuildFilteredQuery(filter);

        sql.Should().NotContain(nasty);
        sql.Should().Be("SELECT contract_id, payload FROM active(@templateId) WHERE payload->>'initiator' = @p0");
        parameters.Should().ContainSingle().Which.Should().Be(("@p0", nasty));
    }

    [Fact]
    public async Task QueryAsync_with_filter_throws_for_null_filter()
    {
        var client = new PqsClient(ValidOptions());

        var act = () => client.QueryAsync<FilterTests.SampleTemplate>((PqsFilter)null!);

        await act.Should().ThrowAsync<ArgumentNullException>().WithParameterName("filter");
    }

    [Fact]
    public async Task QueryOneAsync_throws_for_null_filter()
    {
        var client = new PqsClient(ValidOptions());

        var act = () => client.QueryOneAsync<FilterTests.SampleTemplate>(null!);

        await act.Should().ThrowAsync<ArgumentNullException>().WithParameterName("filter");
    }

    private static PostgresException CreatePostgresException(string sqlState, string messageText)
    {
        return new PostgresException(messageText, severity: "ERROR", invariantSeverity: "ERROR", sqlState: sqlState);
    }

    private sealed record NumericPayload(decimal Price);

    private enum OrderSide { Buy, Sell }

    private sealed record EnumPayload(OrderSide Side);
}
