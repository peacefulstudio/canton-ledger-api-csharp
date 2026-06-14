// Copyright © 2026 Peaceful Studio OÜ. All rights reserved.

using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using FluentAssertions;
using Npgsql;
using Xunit;

namespace Canton.Ledger.Pqs.Client.Tests;

public class FilterTests
{
    [Fact]
    public void Field_generates_correct_sql()
    {
        var filter = Filter.Field<SampleTemplate>(t => t.Initiator, "party::123");

        using var cmd = new NpgsqlCommand();
        var paramIndex = 0;
        var sql = filter.ToSqlClause(cmd, ref paramIndex);

        sql.Should().Be("payload->>'initiator' = @p0");
        cmd.Parameters["@p0"].Value.Should().Be("party::123");
        paramIndex.Should().Be(1);
    }

    [Fact]
    public void Field_value_type_generates_correct_sql()
    {
        var filter = Filter.Field<SampleTemplate>(t => t.NumSwaps, "5");

        using var cmd = new NpgsqlCommand();
        var paramIndex = 0;
        var sql = filter.ToSqlClause(cmd, ref paramIndex);

        sql.Should().Be("payload->>'numSwaps' = @p0");
        cmd.Parameters["@p0"].Value.Should().Be("5");
    }

    [Fact]
    public void Field_throws_for_null_selector()
    {
        var act = () => Filter.Field<SampleTemplate>(null!, "alice");
        act.Should().Throw<ArgumentNullException>().WithParameterName("selector");
    }

    [Fact]
    public void Field_throws_for_null_value()
    {
        var act = () => Filter.Field<SampleTemplate>(t => t.Initiator, null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("value");
    }

    [Fact]
    public void Field_value_with_sql_metacharacters_is_passed_as_parameter()
    {
        const string nasty = "alice'; DROP TABLE active; --";
        var filter = Filter.Field<SampleTemplate>(t => t.Initiator, nasty);

        using var cmd = new NpgsqlCommand();
        var paramIndex = 0;
        var sql = filter.ToSqlClause(cmd, ref paramIndex);

        sql.Should().Be("payload->>'initiator' = @p0");
        sql.Should().NotContain(nasty);
        cmd.Parameters["@p0"].Value.Should().Be(nasty);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("1leadingDigit")]
    [InlineData("has-dash")]
    [InlineData("has.dot")]
    [InlineData("has space")]
    [InlineData("has;semicolon")]
    [InlineData("has'quote")]
    [InlineData("has\"doublequote")]
    [InlineData("has\\backslash")]
    [InlineData("has/slash")]
    [InlineData("has(paren")]
    [InlineData("name; DROP TABLE active; --")]
    public void FieldEquals_throws_for_unsafe_field_name(string fieldName)
    {
        var filter = new PqsFilter.FieldEquals(fieldName, "value");

        using var cmd = new NpgsqlCommand();
        var paramIndex = 0;
        var act = () => filter.ToSqlClause(cmd, ref paramIndex);

        act.Should().Throw<ArgumentException>().WithMessage($"*'{fieldName}'*");
    }

    [Theory]
    [InlineData("a")]
    [InlineData("Z")]
    [InlineData("_underscore")]
    [InlineData("camelCase")]
    [InlineData("PascalCase")]
    [InlineData("with_underscores")]
    [InlineData("name123")]
    [InlineData("name_123_456")]
    public void FieldEquals_accepts_safe_field_name(string fieldName)
    {
        var filter = new PqsFilter.FieldEquals(fieldName, "value");

        using var cmd = new NpgsqlCommand();
        var paramIndex = 0;
        var sql = filter.ToSqlClause(cmd, ref paramIndex);

        sql.Should().Be($"payload->>'{fieldName}' = @p0");
    }

    [Fact]
    public void Or_two_filters_generates_correct_sql()
    {
        var filter = Filter.Or(
            Filter.Field<SampleTemplate>(t => t.Initiator, "alice"),
            Filter.Field<SampleTemplate>(t => t.Counterparty, "alice"));

        using var cmd = new NpgsqlCommand();
        var paramIndex = 0;
        var sql = filter.ToSqlClause(cmd, ref paramIndex);

        sql.Should().Be("(payload->>'initiator' = @p0 OR payload->>'counterparty' = @p1)");
        cmd.Parameters["@p0"].Value.Should().Be("alice");
        cmd.Parameters["@p1"].Value.Should().Be("alice");
        paramIndex.Should().Be(2);
    }

    [Fact]
    public void Or_single_filter_returns_that_filter()
    {
        var inner = Filter.Field<SampleTemplate>(t => t.Initiator, "alice");
        var result = Filter.Or(inner);
        result.Should().BeSameAs(inner);
    }

    [Fact]
    public void Or_empty_throws()
    {
        var act = () => Filter.Or();
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Or_null_throws()
    {
        var act = () => Filter.Or(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Or_throws_when_element_is_null()
    {
        var validFilter = Filter.Field<SampleTemplate>(t => t.Initiator, "alice");
        var act = () => Filter.Or(validFilter, null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("filters[1]");
    }

    [Fact]
    public void And_two_filters_generates_correct_sql()
    {
        var filter = Filter.And(
            Filter.Field<SampleTemplate>(t => t.Initiator, "alice"),
            Filter.Field<SampleTemplate>(t => t.Status, "Active"));

        using var cmd = new NpgsqlCommand();
        var paramIndex = 0;
        var sql = filter.ToSqlClause(cmd, ref paramIndex);

        sql.Should().Be("(payload->>'initiator' = @p0 AND payload->>'status' = @p1)");
        cmd.Parameters["@p0"].Value.Should().Be("alice");
        cmd.Parameters["@p1"].Value.Should().Be("Active");
    }

    [Fact]
    public void And_single_filter_returns_that_filter()
    {
        var inner = Filter.Field<SampleTemplate>(t => t.Initiator, "alice");
        var result = Filter.And(inner);
        result.Should().BeSameAs(inner);
    }

    [Fact]
    public void And_empty_throws()
    {
        var act = () => Filter.And();
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void And_null_throws()
    {
        var act = () => Filter.And(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void And_throws_when_element_is_null()
    {
        var validFilter = Filter.Field<SampleTemplate>(t => t.Initiator, "alice");
        var act = () => Filter.And(null!, validFilter);
        act.Should().Throw<ArgumentNullException>().WithParameterName("filters[0]");
    }

    [Fact]
    public void Or_three_filters_generates_correct_sql()
    {
        var filter = Filter.Or(
            Filter.Field<SampleTemplate>(t => t.Initiator, "alice"),
            Filter.Field<SampleTemplate>(t => t.Counterparty, "alice"),
            Filter.Field<SampleTemplate>(t => t.Status, "Active"));

        using var cmd = new NpgsqlCommand();
        var paramIndex = 0;
        var sql = filter.ToSqlClause(cmd, ref paramIndex);

        sql.Should().Be("(payload->>'initiator' = @p0 OR payload->>'counterparty' = @p1 OR payload->>'status' = @p2)");
        paramIndex.Should().Be(3);
    }

    [Fact]
    public void And_nested_or_generates_correct_sql()
    {
        var filter = Filter.And(
            Filter.Or(
                Filter.Field<SampleTemplate>(t => t.Initiator, "alice"),
                Filter.Field<SampleTemplate>(t => t.Counterparty, "alice")),
            Filter.Field<SampleTemplate>(t => t.Status, "Active"));

        using var cmd = new NpgsqlCommand();
        var paramIndex = 0;
        var sql = filter.ToSqlClause(cmd, ref paramIndex);

        sql.Should().Be("((payload->>'initiator' = @p0 OR payload->>'counterparty' = @p1) AND payload->>'status' = @p2)");
        paramIndex.Should().Be(3);
    }

    [Fact]
    public void Or_nested_and_generates_correct_sql()
    {
        var filter = Filter.Or(
            Filter.And(
                Filter.Field<SampleTemplate>(t => t.Initiator, "alice"),
                Filter.Field<SampleTemplate>(t => t.Status, "Active")),
            Filter.Field<SampleTemplate>(t => t.Counterparty, "bob"));

        using var cmd = new NpgsqlCommand();
        var paramIndex = 0;
        var sql = filter.ToSqlClause(cmd, ref paramIndex);

        sql.Should().Be("((payload->>'initiator' = @p0 AND payload->>'status' = @p1) OR payload->>'counterparty' = @p2)");
        paramIndex.Should().Be(3);
    }

    [Fact]
    public void BuildFilteredQuery_generates_full_query()
    {
        var filter = Filter.Field<SampleTemplate>(t => t.Initiator, "alice");
        var (sql, parameters) = PqsClient.BuildFilteredQuery(filter);

        sql.Should().Be("SELECT contract_id, payload FROM active(@templateId) WHERE payload->>'initiator' = @p0");
        parameters.Should().ContainSingle()
            .Which.Should().Be(("@p0", "alice"));
    }

    internal sealed record SampleTemplate(
        string Initiator,
        string Counterparty,
        long NumSwaps,
        string Status) : ITemplate
    {
        public static Identifier TemplateId { get; } = new("pkg123", "Test.Module", "SampleTemplate");
        public static string PackageId => "pkg123";
        public static string PackageName => "test-package";
        public static Version PackageVersion { get; } = new(0, 1, 0);

        public DamlRecord ToRecord() => DamlRecord.Create(
            DamlField.Create("initiator", new DamlParty(Initiator)),
            DamlField.Create("counterparty", new DamlParty(Counterparty)),
            DamlField.Create("numSwaps", new DamlInt64(NumSwaps)),
            DamlField.Create("status", new DamlText(Status)));

        public static SampleTemplate FromRecord(DamlRecord record) => new(
            Initiator: record.GetRequiredField("initiator").As<DamlParty>().Value,
            Counterparty: record.GetRequiredField("counterparty").As<DamlParty>().Value,
            NumSwaps: record.GetRequiredField("numSwaps").As<DamlInt64>().Value,
            Status: record.GetRequiredField("status").As<DamlText>().Value);
    }
}
