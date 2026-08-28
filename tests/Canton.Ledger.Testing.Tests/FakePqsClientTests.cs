// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Canton.Ledger.Abstractions;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Xunit;

namespace Canton.Ledger.Testing.Tests;

public class FakePqsClientTests
{
    private static readonly Party Alice = new("alice");

    [Fact]
    public async Task QueryAsync_returns_the_staged_contracts_for_the_template_type()
    {
        var contract = new Contract<DemoHolding>(new ContractId<DemoHolding>("cid1"), new DemoHolding(Alice, 42m));
        var client = FakePqsClient.Create().WithQueryResults(contract).Build();

        var results = await client.QueryAsync<DemoHolding>(TestContext.Current.CancellationToken);

        results.Should().ContainSingle().Which.Should().Be(contract);
    }

    [Fact]
    public async Task QueryAsync_for_unstaged_type_throws_descriptive_NotSupportedException()
    {
        var client = FakePqsClient.Create()
            .WithQueryResults(new Contract<DemoHolding>(new ContractId<DemoHolding>("cid1"), new DemoHolding(Alice, 42m)))
            .Build();

        var act = () => client.QueryAsync<OtherHolding>();

        (await act.Should().ThrowAsync<NotSupportedException>())
            .Which.Message.Should().Contain("WithQueryResults").And.Contain("OtherHolding");
    }

    [Fact]
    public async Task QueryAsync_with_filter_returns_the_same_staged_contracts_ignoring_the_filter()
    {
        var contract = new Contract<DemoHolding>(new ContractId<DemoHolding>("cid1"), new DemoHolding(Alice, 42m));
        var client = FakePqsClient.Create().WithQueryResults(contract).Build();
        var filter = Filter.Field<DemoHolding>(h => h.Owner, "bob");

        var results = await client.QueryAsync<DemoHolding>(filter, TestContext.Current.CancellationToken);

        results.Should().ContainSingle().Which.Should().Be(contract);
    }

    [Fact]
    public async Task QueryAsync_with_filter_throws_for_null_filter()
    {
        var client = FakePqsClient.Create().WithQueryResults<DemoHolding>().Build();

        var act = () => client.QueryAsync<DemoHolding>(filter: null!);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task QueryOneAsync_returns_the_first_staged_contract()
    {
        var first = new Contract<DemoHolding>(new ContractId<DemoHolding>("cid1"), new DemoHolding(Alice, 1m));
        var second = new Contract<DemoHolding>(new ContractId<DemoHolding>("cid2"), new DemoHolding(Alice, 2m));
        var client = FakePqsClient.Create().WithQueryResults(first, second).Build();
        var filter = Filter.Field<DemoHolding>(h => h.Owner, "alice");

        var result = await client.QueryOneAsync<DemoHolding>(filter, TestContext.Current.CancellationToken);

        result.Should().Be(first);
    }

    [Fact]
    public async Task QueryOneAsync_returns_null_when_staged_results_are_empty()
    {
        var client = FakePqsClient.Create().WithQueryResults<DemoHolding>().Build();
        var filter = Filter.Field<DemoHolding>(h => h.Owner, "alice");

        var result = await client.QueryOneAsync<DemoHolding>(filter, TestContext.Current.CancellationToken);

        result.Should().BeNull();
    }

    [Fact]
    public async Task QueryOneAsync_for_unstaged_type_throws_descriptive_NotSupportedException()
    {
        var client = FakePqsClient.Create().Build();
        var filter = Filter.Field<DemoHolding>(h => h.Owner, "alice");

        var act = () => client.QueryOneAsync<DemoHolding>(filter);

        (await act.Should().ThrowAsync<NotSupportedException>())
            .Which.Message.Should().Contain("WithQueryResults").And.Contain("DemoHolding");
    }

    [Fact]
    public async Task FetchByIdAsync_returns_the_matching_staged_contract()
    {
        var cid = new ContractId<DemoHolding>("cid1");
        var contract = new Contract<DemoHolding>(cid, new DemoHolding(Alice, 42m));
        var client = FakePqsClient.Create().WithQueryResults(contract).Build();

        var result = await client.FetchByIdAsync(cid, TestContext.Current.CancellationToken);

        result.Should().Be(contract);
    }

    [Fact]
    public async Task FetchByIdAsync_returns_null_when_no_staged_contract_matches()
    {
        var contract = new Contract<DemoHolding>(new ContractId<DemoHolding>("cid1"), new DemoHolding(Alice, 42m));
        var client = FakePqsClient.Create().WithQueryResults(contract).Build();

        var result = await client.FetchByIdAsync(new ContractId<DemoHolding>("cid-missing"), TestContext.Current.CancellationToken);

        result.Should().BeNull();
    }

    [Fact]
    public async Task FetchByIdAsync_for_unstaged_type_throws_descriptive_NotSupportedException()
    {
        var client = FakePqsClient.Create().Build();

        var act = () => client.FetchByIdAsync(new ContractId<DemoHolding>("cid1"));

        (await act.Should().ThrowAsync<NotSupportedException>())
            .Which.Message.Should().Contain("WithQueryResults").And.Contain("DemoHolding");
    }

    [Fact]
    public async Task ExistsAsync_returns_true_when_a_staged_contract_matches()
    {
        var cid = new ContractId<DemoHolding>("cid1");
        var contract = new Contract<DemoHolding>(cid, new DemoHolding(Alice, 42m));
        var client = FakePqsClient.Create().WithQueryResults(contract).Build();

        var exists = await client.ExistsAsync(cid, TestContext.Current.CancellationToken);

        exists.Should().BeTrue();
    }

    [Fact]
    public async Task ExistsAsync_returns_false_when_no_staged_contract_matches()
    {
        var contract = new Contract<DemoHolding>(new ContractId<DemoHolding>("cid1"), new DemoHolding(Alice, 42m));
        var client = FakePqsClient.Create().WithQueryResults(contract).Build();

        var exists = await client.ExistsAsync(new ContractId<DemoHolding>("cid-missing"), TestContext.Current.CancellationToken);

        exists.Should().BeFalse();
    }

    [Fact]
    public async Task ExistsAsync_for_unstaged_type_throws_descriptive_NotSupportedException()
    {
        var client = FakePqsClient.Create().Build();

        var act = () => client.ExistsAsync(new ContractId<DemoHolding>("cid1"));

        (await act.Should().ThrowAsync<NotSupportedException>())
            .Which.Message.Should().Contain("WithQueryResults").And.Contain("DemoHolding");
    }

    [Fact]
    public async Task QueryAsync_interface_returns_the_staged_interface_contracts()
    {
        var contract = new InterfaceContract<IDemoHoldingView, DemoHoldingView>(
            new ContractId<IDemoHoldingView>("cid1"), new DemoHoldingView(42m));
        var client = FakePqsClient.Create().WithInterfaceQueryResults(contract).Build();

        var results = await client.QueryAsync<IDemoHoldingView, DemoHoldingView>(TestContext.Current.CancellationToken);

        results.Should().ContainSingle().Which.Should().Be(contract);
    }

    [Fact]
    public async Task QueryAsync_interface_for_unstaged_interface_throws_descriptive_NotSupportedException()
    {
        var client = FakePqsClient.Create().Build();

        var act = () => client.QueryAsync<IDemoHoldingView, DemoHoldingView>();

        (await act.Should().ThrowAsync<NotSupportedException>())
            .Which.Message.Should().Contain("WithInterfaceQueryResults").And.Contain("IDemoHoldingView");
    }

    [Theory]
    [InlineData(2, 0, new[] { "cid1", "cid2" })]
    [InlineData(2, 2, new[] { "cid3", "cid4" })]
    [InlineData(2, 4, new[] { "cid5" })]
    [InlineData(2, 6, new string[] { })]
    public async Task QueryAsync_with_page_returns_the_staged_slice(
        int limit, int offset, string[] expectedContractIds)
    {
        var client = FakePqsClient.Create().WithQueryResults(StagedHoldings(5)).Build();

        var page = await client.QueryAsync<DemoHolding>(
            new PqsPage(limit, offset), TestContext.Current.CancellationToken);

        page.Select(c => c.Id.Value).Should().Equal(expectedContractIds);
    }

    [Fact]
    public async Task QueryAsync_with_page_for_unstaged_type_throws_descriptive_NotSupportedException()
    {
        var client = FakePqsClient.Create().Build();

        var act = () => client.QueryAsync<DemoHolding>(new PqsPage(limit: 2));

        (await act.Should().ThrowAsync<NotSupportedException>())
            .Which.Message.Should().Contain("WithQueryResults").And.Contain("DemoHolding");
    }

    [Fact]
    public async Task QueryAsync_with_page_throws_for_null_page()
    {
        var client = FakePqsClient.Create().WithQueryResults<DemoHolding>().Build();

        var act = () => client.QueryAsync<DemoHolding>(page: null!);

        await act.Should().ThrowAsync<ArgumentNullException>().WithParameterName("page");
    }

    [Fact]
    public async Task QueryAsync_with_filter_and_page_slices_the_staged_contracts_ignoring_the_filter()
    {
        var client = FakePqsClient.Create().WithQueryResults(StagedHoldings(3)).Build();
        var filter = Filter.Field<DemoHolding>(h => h.Owner, "bob");

        var page = await client.QueryAsync<DemoHolding>(
            filter, new PqsPage(limit: 2, offset: 1), TestContext.Current.CancellationToken);

        page.Select(c => c.Id.Value).Should().Equal("cid2", "cid3");
    }

    [Fact]
    public async Task QueryAsync_with_filter_and_page_throws_for_null_page()
    {
        var client = FakePqsClient.Create().WithQueryResults<DemoHolding>().Build();
        var filter = Filter.Field<DemoHolding>(h => h.Owner, "bob");

        var act = () => client.QueryAsync<DemoHolding>(filter, page: null!);

        await act.Should().ThrowAsync<ArgumentNullException>().WithParameterName("page");
    }

    [Fact]
    public async Task QueryAsync_with_filter_and_page_throws_for_null_filter()
    {
        var client = FakePqsClient.Create().WithQueryResults<DemoHolding>().Build();

        var act = () => client.QueryAsync<DemoHolding>(filter: null!, new PqsPage(limit: 10));

        await act.Should().ThrowAsync<ArgumentNullException>().WithParameterName("filter");
    }

    [Fact]
    public async Task QueryAsync_interface_with_page_throws_for_null_page()
    {
        var client = FakePqsClient.Create().Build();

        var act = () => client.QueryAsync<IDemoHoldingView, DemoHoldingView>(page: null!);

        await act.Should().ThrowAsync<ArgumentNullException>().WithParameterName("page");
    }

    [Fact]
    public async Task QueryAsync_interface_with_page_returns_the_staged_slice()
    {
        var first = new InterfaceContract<IDemoHoldingView, DemoHoldingView>(
            new ContractId<IDemoHoldingView>("cid1"), new DemoHoldingView(1m));
        var second = new InterfaceContract<IDemoHoldingView, DemoHoldingView>(
            new ContractId<IDemoHoldingView>("cid2"), new DemoHoldingView(2m));
        var third = new InterfaceContract<IDemoHoldingView, DemoHoldingView>(
            new ContractId<IDemoHoldingView>("cid3"), new DemoHoldingView(3m));
        var client = FakePqsClient.Create().WithInterfaceQueryResults(first, second, third).Build();

        var page = await client.QueryAsync<IDemoHoldingView, DemoHoldingView>(
            new PqsPage(limit: 2, offset: 1), TestContext.Current.CancellationToken);

        page.Should().Equal(second, third);
    }

    [Fact]
    public async Task QueryAsync_interface_with_page_for_unstaged_interface_throws_descriptive_NotSupportedException()
    {
        var client = FakePqsClient.Create().Build();

        var act = () => client.QueryAsync<IDemoHoldingView, DemoHoldingView>(new PqsPage(limit: 2));

        (await act.Should().ThrowAsync<NotSupportedException>())
            .Which.Message.Should().Contain("WithInterfaceQueryResults").And.Contain("IDemoHoldingView");
    }

    private static Contract<DemoHolding>[] StagedHoldings(int count) =>
        [.. Enumerable.Range(1, count).Select(i =>
            new Contract<DemoHolding>(new ContractId<DemoHolding>($"cid{i}"), new DemoHolding(Alice, i)))];

    [Fact]
    public async Task Build_snapshots_staged_results_so_later_builder_mutation_is_ignored()
    {
        var builder = FakePqsClient.Create()
            .WithQueryResults(new Contract<DemoHolding>(new ContractId<DemoHolding>("cid1"), new DemoHolding(Alice, 1m)));
        var client = builder.Build();
        builder.WithQueryResults(
            new Contract<DemoHolding>(new ContractId<DemoHolding>("cid1"), new DemoHolding(Alice, 1m)),
            new Contract<DemoHolding>(new ContractId<DemoHolding>("cid2"), new DemoHolding(Alice, 2m)));

        var results = await client.QueryAsync<DemoHolding>(TestContext.Current.CancellationToken);

        results.Should().ContainSingle();
    }
}
