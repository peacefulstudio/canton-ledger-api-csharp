// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Canton.Ledger.Abstractions;
using System.Data;
using System.Data.Common;
using AwesomeAssertions;
using Npgsql;
using Xunit;

namespace Canton.Ledger.Pqs.Client.Tests;

[Collection("PqsClient global ActivitySource")]
public class PqsClientPagingTests
{
    private const string PagedActiveSql =
        "SELECT contract_id, payload FROM active(@typeId) ORDER BY contract_id LIMIT @pageLimit OFFSET @pageOffset";

    private sealed class RecordingPqsServer
    {
        private readonly IReadOnlyList<(string ContractId, string Payload)> _rows;

        public RecordingPqsServer(params (string ContractId, string Payload)[] rows) => _rows = rows;

        public List<(string Sql, IReadOnlyDictionary<string, object?> Parameters)> Commands { get; } = [];

        public int RowsServed { get; private set; }

        public Task<DbDataReader> ExecuteAsync(NpgsqlCommand command, CancellationToken cancellationToken)
        {
            var parameters = command.Parameters
                .Cast<NpgsqlParameter>()
                .ToDictionary(p => p.ParameterName, p => p.Value);
            Commands.Add((command.CommandText, parameters));

            IEnumerable<(string ContractId, string Payload)> served = _rows;
            if (command.CommandText.Contains("OFFSET @pageOffset", StringComparison.Ordinal)
                && parameters.TryGetValue("@pageOffset", out var offset))
            {
                served = served.Skip((int)offset!);
            }

            if (command.CommandText.Contains("LIMIT @pageLimit", StringComparison.Ordinal)
                && parameters.TryGetValue("@pageLimit", out var limit))
            {
                served = served.Take((int)limit!);
            }

            var table = new DataTable();
            table.Columns.Add("contract_id", typeof(string));
            table.Columns.Add("payload", typeof(string));
            foreach (var (contractId, payload) in served)
                table.Rows.Add(contractId, payload);

            RowsServed = table.Rows.Count;
            return Task.FromResult<DbDataReader>(table.CreateDataReader());
        }
    }

    private static PqsClient ClientBackedBy(RecordingPqsServer server) =>
        new(
            new PqsClientOptions { ConnectionString = "Host=localhost;Database=pqs" },
            _ => ValueTask.FromResult(new NpgsqlConnection()),
            logger: null,
            executeReaderAsync: server.ExecuteAsync);

    private static RecordingPqsServer ServerWithSampleTemplates(int count) =>
        new([.. Enumerable.Range(1, count).Select(i => (
            $"00cid{i}",
            $$"""{"initiator":"alice","counterparty":"bob","numSwaps":"{{i}}","status":"Active"}"""))]);

    [Theory]
    [InlineData(2, 0, new[] { "00cid1", "00cid2" })]
    [InlineData(2, 2, new[] { "00cid3", "00cid4" })]
    [InlineData(2, 4, new[] { "00cid5" })]
    [InlineData(2, 6, new string[] { })]
    public async Task QueryAsync_with_page_sends_LIMIT_OFFSET_to_PQS_and_fetches_only_the_page(
        int limit, int offset, string[] expectedContractIds)
    {
        var server = ServerWithSampleTemplates(count: 5);
        var client = ClientBackedBy(server);

        var page = await client.QueryAsync<FilterTests.SampleTemplate>(
            new PqsPage(limit, offset), TestContext.Current.CancellationToken);

        page.Select(c => c.Id.Value).Should().Equal(expectedContractIds);
        server.RowsServed.Should().Be(expectedContractIds.Length);
        var (sql, parameters) = server.Commands.Should().ContainSingle().Subject;
        sql.Should().Be(PagedActiveSql);
        parameters["@pageLimit"].Should().Be(limit);
        parameters["@pageOffset"].Should().Be(offset);
    }

    [Fact]
    public async Task QueryAsync_with_filter_and_page_appends_paging_after_the_WHERE_clause()
    {
        var server = ServerWithSampleTemplates(count: 3);
        var client = ClientBackedBy(server);
        var filter = Filter.Field<FilterTests.SampleTemplate>(t => t.Initiator, "alice");

        var page = await client.QueryAsync<FilterTests.SampleTemplate>(
            filter, new PqsPage(limit: 2, offset: 1), TestContext.Current.CancellationToken);

        page.Select(c => c.Id.Value).Should().Equal("00cid2", "00cid3");
        server.RowsServed.Should().Be(2);
        var (sql, parameters) = server.Commands.Should().ContainSingle().Subject;
        sql.Should().Be(
            "SELECT contract_id, payload FROM active(@typeId) WHERE payload->>'initiator' = @p0 " +
            "ORDER BY contract_id LIMIT @pageLimit OFFSET @pageOffset");
        parameters["@p0"].Should().Be("alice");
        parameters["@pageLimit"].Should().Be(2);
        parameters["@pageOffset"].Should().Be(1);
    }

    [Fact]
    public async Task QueryAsync_interface_with_page_sends_LIMIT_OFFSET_to_PQS()
    {
        var server = new RecordingPqsServer(
            ("00cid1", """{"amount":"1.5"}"""),
            ("00cid2", """{"amount":"2.5"}"""),
            ("00cid3", """{"amount":"3.5"}"""));
        var client = ClientBackedBy(server);

        var page = await client.QueryAsync<ISampleInterface, SampleView>(
            new PqsPage(limit: 2, offset: 1), TestContext.Current.CancellationToken);

        page.Select(c => c.View.Amount).Should().Equal(2.5m, 3.5m);
        server.RowsServed.Should().Be(2);
        var (sql, parameters) = server.Commands.Should().ContainSingle().Subject;
        sql.Should().Be(PagedActiveSql);
        parameters["@typeId"].Should().Be(PqsClient.GetDamlTypeId<ISampleInterface>());
        parameters["@pageLimit"].Should().Be(2);
        parameters["@pageOffset"].Should().Be(1);
    }

    [Fact]
    public async Task QueryAsync_without_page_fetches_the_unbounded_result_set()
    {
        var server = ServerWithSampleTemplates(count: 5);
        var client = ClientBackedBy(server);

        var result = await client.QueryAsync<FilterTests.SampleTemplate>(TestContext.Current.CancellationToken);

        result.Should().HaveCount(5);
        server.RowsServed.Should().Be(5);
        server.Commands.Should().ContainSingle().Which.Sql.Should().NotContain("LIMIT");
    }

    [Fact]
    public async Task QueryAsync_with_page_returns_empty_when_the_template_is_not_found()
    {
        var client = new PqsClient(
            new PqsClientOptions { ConnectionString = "Host=localhost;Database=pqs" },
            _ => throw new PostgresException(
                "Identifier not found: pkg123:Test.Module:SampleTemplate",
                severity: "ERROR",
                invariantSeverity: "ERROR",
                sqlState: "P0001"),
            logger: null);

        var result = await client.QueryAsync<FilterTests.SampleTemplate>(
            new PqsPage(limit: 10), TestContext.Current.CancellationToken);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task QueryAsync_with_page_throws_for_null_page()
    {
        var client = ClientBackedBy(ServerWithSampleTemplates(count: 1));

        var act = () => client.QueryAsync<FilterTests.SampleTemplate>(page: null!);

        await act.Should().ThrowAsync<ArgumentNullException>().WithParameterName("page");
    }

    [Fact]
    public async Task QueryAsync_with_filter_and_page_throws_for_null_filter()
    {
        var client = ClientBackedBy(ServerWithSampleTemplates(count: 1));

        var act = () => client.QueryAsync<FilterTests.SampleTemplate>(filter: null!, new PqsPage(limit: 10));

        await act.Should().ThrowAsync<ArgumentNullException>().WithParameterName("filter");
    }

    [Fact]
    public async Task QueryAsync_with_filter_and_page_throws_for_null_page()
    {
        var client = ClientBackedBy(ServerWithSampleTemplates(count: 1));
        var filter = Filter.Field<FilterTests.SampleTemplate>(t => t.Initiator, "alice");

        var act = () => client.QueryAsync<FilterTests.SampleTemplate>(filter, page: null!);

        await act.Should().ThrowAsync<ArgumentNullException>().WithParameterName("page");
    }

    [Fact]
    public async Task QueryAsync_interface_with_page_throws_for_null_page()
    {
        var client = ClientBackedBy(ServerWithSampleTemplates(count: 1));

        var act = () => client.QueryAsync<ISampleInterface, SampleView>(page: null!);

        await act.Should().ThrowAsync<ArgumentNullException>().WithParameterName("page");
    }
}
