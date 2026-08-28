// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Canton.Ledger.Abstractions;
using System.Diagnostics;
using AwesomeAssertions;
using Canton.Ledger.Kernel.Telemetry;
using Daml.Runtime.Contracts;
using Microsoft.Extensions.Logging;
using Npgsql;
using Xunit;

namespace Canton.Ledger.Pqs.Client.Tests;

[Collection("PqsClient global ActivitySource")]
public class PqsClientQueryExecutionTests
{
    private static PqsClient ClientThatOpensWith(
        Func<CancellationToken, ValueTask<NpgsqlConnection>> openConnectionAsync,
        ILogger<PqsClient>? logger = null) =>
        new(new PqsClientOptions { ConnectionString = "Host=localhost;Database=pqs" }, openConnectionAsync, logger);

    private static Func<CancellationToken, ValueTask<NpgsqlConnection>> Throwing(Exception exception) =>
        _ => throw exception;

    private static PostgresException TemplateNotFound() =>
        new(
            "Identifier not found: pkg123:Test.Module:SampleTemplate",
            severity: "ERROR",
            invariantSeverity: "ERROR",
            sqlState: "P0001");


    [Fact]
    public async Task QueryAsync_returns_empty_when_the_template_is_not_found()
    {
        var client = ClientThatOpensWith(Throwing(TemplateNotFound()));

        var result = await client.QueryAsync<FilterTests.SampleTemplate>(TestContext.Current.CancellationToken);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task QueryAsync_returns_empty_when_the_interface_is_not_found()
    {
        var client = ClientThatOpensWith(Throwing(TemplateNotFound()));

        var result = await client.QueryAsync<ISampleInterface, SampleView>(TestContext.Current.CancellationToken);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task QueryOneAsync_returns_null_when_the_template_is_not_found()
    {
        var client = ClientThatOpensWith(Throwing(TemplateNotFound()));
        var filter = Filter.Field<FilterTests.SampleTemplate>(t => t.Initiator, "alice");

        var result = await client.QueryOneAsync<FilterTests.SampleTemplate>(
            filter, TestContext.Current.CancellationToken);

        result.Should().BeNull();
    }

    [Fact]
    public async Task FetchByIdAsync_returns_null_when_the_template_is_not_found()
    {
        var client = ClientThatOpensWith(Throwing(TemplateNotFound()));
        var contractId = new ContractId<FilterTests.SampleTemplate>("00abc123");

        var result = await client.FetchByIdAsync(contractId, TestContext.Current.CancellationToken);

        result.Should().BeNull();
    }

    [Fact]
    public async Task ExistsAsync_returns_false_when_the_template_is_not_found()
    {
        var client = ClientThatOpensWith(Throwing(TemplateNotFound()));
        var contractId = new ContractId<FilterTests.SampleTemplate>("00abc123");

        var result = await client.ExistsAsync(contractId, TestContext.Current.CancellationToken);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task QueryAsync_logs_records_and_rethrows_a_non_cancellation_failure()
    {
        using var capture = ActivityCapture.Of(PqsClient.ActivitySourceName);

        var loggerFactory = new CapturingLoggerFactory();
        var failure = new InvalidOperationException("connection blew up");
        var client = ClientThatOpensWith(Throwing(failure), new Logger<PqsClient>(loggerFactory));

        var act = async () => await client.QueryAsync<FilterTests.SampleTemplate>(
            TestContext.Current.CancellationToken);

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .Which.Should().BeSameAs(failure);

        var activity = capture.Activities.Should().ContainSingle(a => a.OperationName == "PqsQuery").Subject;
        activity.Status.Should().Be(ActivityStatusCode.Error);
        activity.GetTagItem(ActivityExtensions.ErrorType).Should().Be(typeof(InvalidOperationException).FullName);

        loggerFactory.Records.Should().Contain(r =>
            r.Category == typeof(PqsClient).FullName
            && r.Level == LogLevel.Error
            && r.Message.Contains("PQS query failed"));
    }

    [Fact]
    public async Task QueryAsync_propagates_cancellation_without_recording_an_error()
    {
        using var capture = ActivityCapture.Of(PqsClient.ActivitySourceName);

        var loggerFactory = new CapturingLoggerFactory();
        var client = ClientThatOpensWith(
            Throwing(new OperationCanceledException()), new Logger<PqsClient>(loggerFactory));

        var act = async () => await client.QueryAsync<FilterTests.SampleTemplate>(
            TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<OperationCanceledException>();

        var activity = capture.Activities.Should().ContainSingle(a => a.OperationName == "PqsQuery").Subject;
        activity.Status.Should().NotBe(ActivityStatusCode.Error);
        loggerFactory.Records.Should().NotContain(r =>
            r.Level == LogLevel.Error && r.Message.Contains("PQS query failed"));
    }
}
