// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Runtime.Serialization;
using Canton.Ledger.Abstractions;
using System.Text.Json;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using RuntimeIdentifier = Daml.Runtime.Data.Identifier;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
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
    public void Constructor_throws_when_the_configured_options_value_is_null()
    {
        var act = () => new PqsClient(Options.Create<PqsClientOptions>(null!));
        act.Should().Throw<ArgumentNullException>().WithParameterName("options");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t")]
    public void AddPqsClient_rejects_an_empty_or_whitespace_ConnectionString(string connectionString)
    {
        var services = new ServiceCollection();
        services.AddPqsClient(o => o.ConnectionString = connectionString);

        using var provider = services.BuildServiceProvider();

        var act = () => provider.GetRequiredService<IPqsClient>();

        act.Should().Throw<OptionsValidationException>();
    }

    [Fact]
    public void Constructor_with_IOptions_throws_when_ConnectionString_is_empty()
    {
        var options = Options.Create(new PqsClientOptions { ConnectionString = "" });

        var act = () => new PqsClient(options);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Constructor_with_IOptions_succeeds_with_valid_options()
    {
        var act = () => new PqsClient(Options.Create(ValidOptions()));
        act.Should().NotThrow();
    }

    [Fact]
    public void DeserializeContract_maps_contract_id_and_camel_case_payload()
    {
        const string payload =
            """{"initiator":"alice","counterparty":"bob","numSwaps":"42","status":"Active"}""";

        var contract = PqsClient.DeserializeContract<FilterTests.SampleTemplate>(
            "00abc123", payload);

        contract.Id.Value.Should().Be("00abc123");
        contract.Data.Initiator.Should().Be("alice");
        contract.Data.Counterparty.Should().Be("bob");
        contract.Data.NumSwaps.Should().Be(42);
        contract.Data.Status.Should().Be("Active");
    }

    [Fact]
    public void IsTypeNotFoundError_returns_true_for_matching_exception()
    {
        var ex = CreatePostgresException("P0001", "Identifier not found: test-package:Module:Template");
        PqsClient.IsTypeNotFoundError(ex).Should().BeTrue();
    }

    [Fact]
    public void IsTypeNotFoundError_returns_false_for_different_sql_state()
    {
        var ex = CreatePostgresException("42P01", "Identifier not found: test");
        PqsClient.IsTypeNotFoundError(ex).Should().BeFalse();
    }

    [Fact]
    public void IsTypeNotFoundError_returns_false_for_different_message()
    {
        var ex = CreatePostgresException("P0001", "Some other error");
        PqsClient.IsTypeNotFoundError(ex).Should().BeFalse();
    }

    [Fact]
    public void IsTypeNotFoundError_returns_false_when_prefix_does_not_match_at_start()
    {
        var ex = CreatePostgresException("P0001", "Some error: Identifier not found: x");
        PqsClient.IsTypeNotFoundError(ex).Should().BeFalse();
    }

    [Fact]
    public void BuildFilteredQuery_simple_field_produces_correct_query()
    {
        var filter = Filter.Field<FilterTests.SampleTemplate>(t => t.Initiator, "party1");
        var (sql, parameters) = PqsClient.BuildFilteredQuery(filter);

        sql.Should().Be("SELECT contract_id, payload FROM active(@typeId) WHERE payload->>'initiator' = @p0");
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
            "SELECT contract_id, payload FROM active(@typeId) " +
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
            "SELECT contract_id, payload FROM active(@typeId) " +
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
            "SELECT contract_id, payload FROM active(@typeId) " +
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
        sql.Should().Be("SELECT contract_id, payload FROM active(@typeId) WHERE payload->>'initiator' = @p0");
        parameters.Should().ContainSingle().Which.Should().Be(("@p0", nasty));
    }

    [Fact]
    public async Task QueryAsync_with_filter_throws_for_null_filter()
    {
        var client = new PqsClient(Options.Create(ValidOptions()));

        var act = () => client.QueryAsync<FilterTests.SampleTemplate>((PqsFilter)null!);

        await act.Should().ThrowAsync<ArgumentNullException>().WithParameterName("filter");
    }

    [Fact]
    public async Task QueryOneAsync_throws_for_null_filter()
    {
        var client = new PqsClient(Options.Create(ValidOptions()));

        var act = () => client.QueryOneAsync<FilterTests.SampleTemplate>(null!);

        await act.Should().ThrowAsync<ArgumentNullException>().WithParameterName("filter");
    }

    [Fact]
    public void DeserializeContract_reads_a_bare_string_ContractId()
    {
        const string payload = """{"owner":"alice","target":"00deadbeef"}""";

        var contract = PqsClient.DeserializeContract<ReferencingTemplate>(
            "00abc123", payload);

        contract.Data.Target.Value.Should().Be(
            "00deadbeef",
            "a ContractId is a bare string on the PQS wire");
        contract.Data.Owner.Should().Be("alice");
    }

    internal sealed record ReferencingTemplate(
        [property: DamlField("owner")] string Owner,
        [property: DamlField("target")] ContractId<FilterTests.SampleTemplate> Target) : ITemplate, IDamlRecord<ReferencingTemplate>
    {
        public static RuntimeIdentifier TemplateId { get; } = new("pkg123", "Test.Module", "ReferencingTemplate");
        public static string PackageId => "pkg123";
        public static string PackageName => "test-package";
        public static Version PackageVersion { get; } = new(0, 1, 0);
        public static DamlTypeDescriptor DamlTypeId { get; } = new(TemplateId, DamlTypeKind.Template, PackageName);

        public DamlRecord ToRecord() => DamlRecord.Create(
            DamlField.Create("owner", new DamlParty(Owner)),
            DamlField.Create("target", new DamlContractId(Target.Value)));

        public static DamlRecord __ReadDamlLfJson(JsonElement json, DamlLfJsonDecodeContext context) =>
            PqsRecordReader.Read(
                json,
                context,
                ("owner", DamlLfJsonDecoders.ReadParty),
                ("target", DamlLfJsonDecoders.ReadContractId));

        public static ReferencingTemplate FromRecord(DamlRecord record) => new(
            Owner: record.GetRequiredField("owner").As<DamlParty>().Value,
            Target: new ContractId<FilterTests.SampleTemplate>(
                record.GetRequiredField("target").As<DamlContractId>().Value));
    }

    private static PostgresException CreatePostgresException(string sqlState, string messageText)
    {
        return new PostgresException(messageText, severity: "ERROR", invariantSeverity: "ERROR", sqlState: sqlState);
    }
}
