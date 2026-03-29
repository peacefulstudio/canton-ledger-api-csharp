// Copyright (c) 2026 Peaceful Studio OÜ. All rights reserved.

using Canton.Ledger.Pqs.Client;
using Daml.Codegen.CSharp.Runtime.Contracts;
using Daml.Codegen.CSharp.Runtime.Data;
using FluentAssertions;
using Npgsql;
using Xunit;

namespace Canton.Ledger.Pqs.Client.Tests;

public class FilterTests
{
    // ──────────────────────────────────────────────────────────────
    // Filter.Field — type-safe field equality
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public void field_generates_correct_sql()
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
    public void field_value_type_generates_correct_sql()
    {
        var filter = Filter.Field<SampleTemplate>(t => t.NumSwaps, "5");

        using var cmd = new NpgsqlCommand();
        var paramIndex = 0;
        var sql = filter.ToSqlClause(cmd, ref paramIndex);

        sql.Should().Be("payload->>'numSwaps' = @p0");
        cmd.Parameters["@p0"].Value.Should().Be("5");
    }

    // ──────────────────────────────────────────────────────────────
    // Filter.Or
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public void or_two_filters_generates_correct_sql()
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
    public void or_single_filter_returns_that_filter()
    {
        var inner = Filter.Field<SampleTemplate>(t => t.Initiator, "alice");
        var result = Filter.Or(inner);
        result.Should().BeSameAs(inner);
    }

    [Fact]
    public void or_empty_throws()
    {
        var act = () => Filter.Or();
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void or_null_throws()
    {
        var act = () => Filter.Or(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    // ──────────────────────────────────────────────────────────────
    // Filter.And
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public void and_two_filters_generates_correct_sql()
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
    public void and_single_filter_returns_that_filter()
    {
        var inner = Filter.Field<SampleTemplate>(t => t.Initiator, "alice");
        var result = Filter.And(inner);
        result.Should().BeSameAs(inner);
    }

    [Fact]
    public void and_empty_throws()
    {
        var act = () => Filter.And();
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void and_null_throws()
    {
        var act = () => Filter.And(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    // ──────────────────────────────────────────────────────────────
    // Three-filter composition
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public void or_three_filters_generates_correct_sql()
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

    // ──────────────────────────────────────────────────────────────
    // Nested composition
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public void nested_or_in_and_generates_correct_sql()
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
    public void nested_and_in_or_generates_correct_sql()
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

    // ──────────────────────────────────────────────────────────────
    // BuildFilteredQuery (integration with PqsClient)
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public void build_filtered_query_generates_full_query()
    {
        var filter = Filter.Field<SampleTemplate>(t => t.Initiator, "alice");
        var (sql, parameters) = PqsClient.BuildFilteredQuery(filter);

        sql.Should().Be("SELECT contract_id, payload FROM active(@templateId) WHERE payload->>'initiator' = @p0");
        parameters.Should().ContainSingle()
            .Which.Should().Be(("@p0", "alice"));
    }

    // ──────────────────────────────────────────────────────────────
    // Test template — minimal ITemplate for unit tests
    // ──────────────────────────────────────────────────────────────

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
