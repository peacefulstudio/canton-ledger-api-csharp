// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Net;
using System.Text.Json;
using AwesomeAssertions;
using Daml.Ledger.Abstractions;
using Daml.Runtime;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Microsoft.Extensions.Options;
using Xunit;
using RuntimeCommands = Daml.Runtime.Commands;
using RuntimeIdentifier = Daml.Runtime.Data.Identifier;

namespace Canton.Ledger.Rest.Client.Tests;

public sealed class RestLedgerClientTrafficCostTests : IDisposable
{
    private static readonly Party Alice = new("party::alice");

    private static readonly DateTimeOffset EstimatedAt =
        new(2026, 8, 15, 9, 30, 0, TimeSpan.Zero);

    private readonly List<StubHttpClientFactory> _factories = [];

    public void Dispose()
    {
        foreach (var factory in _factories)
        {
            factory.Dispose();
        }
    }

    private sealed record TestTemplate : ITemplate
    {
        public static RuntimeIdentifier TemplateId { get; } = new("pkg", "Module", "Template");
        public static string PackageId => "pkg";
        public static string PackageName => "pkg-name";
        public static Version PackageVersion { get; } = new(0, 1, 0);
        public static DamlTypeDescriptor DamlTypeId { get; } = new(TemplateId, DamlTypeKind.Template, PackageName);
        public DamlRecord ToRecord() => new(TemplateId, [new DamlField("owner", Alice.ToDamlValue())]);
    }

    private RestLedgerClient ClientWith(RecordingHttpHandler transport, string? userId = "test-user")
    {
        var factory = new StubHttpClientFactory(transport);
        _factories.Add(factory);
        return new RestLedgerClient(factory, Options.Create(new RestLedgerClientOptions
        {
            HttpAddress = "http://localhost:7575",
            UserId = userId,
        }));
    }

    private static RecordingHttpHandler TransportServing(string body) =>
        new RecordingHttpHandler().WithResponse(HttpStatusCode.OK, body);

    private static RuntimeCommands.CommandsSubmission SingleCreateSubmission() =>
        RuntimeCommands.CommandsSubmission.Single(
            RuntimeCommands.CreateCommand.For(new TestTemplate()), Alice);

    [Fact]
    public async Task EstimateTrafficCostAsync_projects_every_cost_component_and_the_estimation_timestamp()
    {
        var client = ClientWith(TransportServing(
            """
            {
              "costEstimation": {
                "estimationTimestamp": "2026-08-15T09:30:00Z",
                "confirmationRequestTrafficCostEstimation": "3000",
                "confirmationResponseTrafficCostEstimation": "1096",
                "totalTrafficCostEstimation": "4096"
              }
            }
            """));

        var estimate = await client.EstimateTrafficCostAsync(
            SingleCreateSubmission(), cancellationToken: TestContext.Current.CancellationToken);

        estimate.Should().NotBeNull();
        estimate!.EstimatedAt.Should().Be(EstimatedAt);
        estimate.ConfirmationRequestCost.Should().Be(3000L);
        estimate.ConfirmationResponseCost.Should().Be(1096L);
        estimate.TotalCost.Should().Be(4096L);
    }

    [Fact]
    public async Task EstimateTrafficCostAsync_reads_the_costs_the_participant_serves_as_raw_JSON_numbers()
    {
        var client = ClientWith(TransportServing(
            """
            {
              "costEstimation": {
                "confirmationRequestTrafficCostEstimation": 3000,
                "confirmationResponseTrafficCostEstimation": 1096,
                "totalTrafficCostEstimation": 4096
              }
            }
            """));

        var estimate = await client.EstimateTrafficCostAsync(
            SingleCreateSubmission(), cancellationToken: TestContext.Current.CancellationToken);

        estimate.Should().NotBeNull();
        estimate!.ConfirmationRequestCost.Should().Be(3000L);
        estimate.ConfirmationResponseCost.Should().Be(1096L);
        estimate.TotalCost.Should().Be(4096L);
    }

    [Fact]
    public async Task EstimateTrafficCostAsync_returns_null_when_participant_omits_cost_estimation()
    {
        var client = ClientWith(TransportServing("{}"));

        var estimate = await client.EstimateTrafficCostAsync(
            SingleCreateSubmission(), cancellationToken: TestContext.Current.CancellationToken);

        estimate.Should().BeNull();
    }

    [Fact]
    public async Task EstimateTrafficCostAsync_reports_a_zero_valued_estimation_as_a_zero_cost_estimate()
    {
        var client = ClientWith(TransportServing(
            """
            {
              "costEstimation": {
                "confirmationRequestTrafficCostEstimation": "0",
                "confirmationResponseTrafficCostEstimation": "0",
                "totalTrafficCostEstimation": "0"
              }
            }
            """));

        var estimate = await client.EstimateTrafficCostAsync(
            SingleCreateSubmission(), cancellationToken: TestContext.Current.CancellationToken);

        estimate.Should().NotBeNull();
        estimate!.ConfirmationRequestCost.Should().Be(0L);
        estimate.ConfirmationResponseCost.Should().Be(0L);
        estimate.TotalCost.Should().Be(0L);
    }

    [Fact]
    public async Task EstimateTrafficCostAsync_keeps_the_costs_when_the_participant_omits_the_timestamp()
    {
        var client = ClientWith(TransportServing(
            """{"costEstimation": {"totalTrafficCostEstimation": "512"}}"""));

        var estimate = await client.EstimateTrafficCostAsync(
            SingleCreateSubmission(), cancellationToken: TestContext.Current.CancellationToken);

        estimate.Should().NotBeNull();
        estimate!.EstimatedAt.Should().BeNull();
        estimate.TotalCost.Should().Be(512L);
    }

    [Fact]
    public async Task EstimateTrafficCostAsync_throws_when_a_cost_exceeds_the_signed_range()
    {
        var client = ClientWith(TransportServing(
            """{"costEstimation": {"totalTrafficCostEstimation": "9223372036854775808"}}"""));

        var act = () => client.EstimateTrafficCostAsync(
            SingleCreateSubmission(), cancellationToken: TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*total traffic cost of 9223372036854775808 bytes*");
    }

    [Fact]
    public async Task EstimateTrafficCostAsync_throws_when_a_cost_is_not_a_whole_number_of_bytes()
    {
        var client = ClientWith(TransportServing(
            """{"costEstimation": {"totalTrafficCostEstimation": "4096.5"}}"""));

        var act = () => client.EstimateTrafficCostAsync(
            SingleCreateSubmission(), cancellationToken: TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*not a whole number of bytes*");
    }

    [Fact]
    public async Task EstimateTrafficCostAsync_sends_the_submission_to_the_prepare_route_and_asks_for_cost_estimation()
    {
        var transport = TransportServing("{}");
        var client = ClientWith(transport);

        await client.EstimateTrafficCostAsync(
            SingleCreateSubmission(), cancellationToken: TestContext.Current.CancellationToken);

        transport.LastRequest!.Method.Should().Be(HttpMethod.Post);
        transport.LastRequest.RequestUri!.PathAndQuery.Should().Be("/v2/interactive-submission/prepare");

        using var body = JsonDocument.Parse(transport.LastRequestBody!);
        body.RootElement.TryGetProperty("estimateTrafficCost", out var hints).Should().BeTrue();
        hints.TryGetProperty("disabled", out _).Should().BeFalse();
        body.RootElement.GetProperty("actAs").EnumerateArray()
            .Select(party => party.GetString()).Should().Equal("party::alice");
        body.RootElement.GetProperty("commands").GetArrayLength().Should().Be(1);
        body.RootElement.GetProperty("userId").GetString().Should().Be("test-user");
        body.RootElement.GetProperty("commandId").GetString().Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task EstimateTrafficCostAsync_omits_the_synchronizer_id_when_the_submission_names_none()
    {
        var transport = TransportServing("{}");
        var client = ClientWith(transport);

        await client.EstimateTrafficCostAsync(
            SingleCreateSubmission(), cancellationToken: TestContext.Current.CancellationToken);

        using var body = JsonDocument.Parse(transport.LastRequestBody!);
        body.RootElement.TryGetProperty("synchronizerId", out _).Should().BeFalse();
    }

    [Fact]
    public async Task EstimateTrafficCostAsync_refuses_a_raw_JSON_number_cost_that_exceeds_the_signed_range()
    {
        var client = ClientWith(TransportServing(
            """{"costEstimation": {"totalTrafficCostEstimation": 9223372036854775808}}"""));

        var act = () => client.EstimateTrafficCostAsync(
            SingleCreateSubmission(), cancellationToken: TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<JsonException>();
    }

    [Fact]
    public async Task EstimateTrafficCostAsync_throws_rather_than_reporting_no_estimation_when_the_body_is_null()
    {
        var client = ClientWith(TransportServing("null"));

        var act = () => client.EstimateTrafficCostAsync(
            SingleCreateSubmission(), cancellationToken: TestContext.Current.CancellationToken);

        (await act.Should().ThrowAsync<LedgerOperationException>())
            .Which.Message.Should().Contain("no prepared submission was present");
    }

    [Fact]
    public async Task EstimateTrafficCostAsync_throws_a_LedgerOperationException_on_a_non_success_response()
    {
        var client = ClientWith(new RecordingHttpHandler().WithResponse(HttpStatusCode.ServiceUnavailable, "{}"));

        var act = () => client.EstimateTrafficCostAsync(
            SingleCreateSubmission(), cancellationToken: TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<LedgerOperationException>();
    }

    [Fact]
    public async Task EstimateTrafficCostAsync_rejects_a_null_submission()
    {
        var client = ClientWith(TransportServing("{}"));

        var act = () => client.EstimateTrafficCostAsync(
            null!, cancellationToken: TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }
}
