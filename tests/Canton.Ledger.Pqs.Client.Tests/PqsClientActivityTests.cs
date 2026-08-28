// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Canton.Ledger.Abstractions;
using System.Diagnostics;
using AwesomeAssertions;
using Canton.Ledger.Kernel.Telemetry;
using Daml.Runtime.Contracts;
using Xunit;

namespace Canton.Ledger.Pqs.Client.Tests;

[Collection("PqsClient global ActivitySource")]
public class PqsClientActivityTests
{
    private static PqsClient CreateClient() =>
        new(new PqsClientOptions { ConnectionString = "not a valid connection string" });


    [Fact]
    public async Task QueryAsync_tags_the_PqsQuery_activity_with_daml_template_id_and_records_the_error()
    {
        using var capture = ActivityCapture.Of(PqsClient.ActivitySourceName);

        var client = CreateClient();

        var act = async () => await client.QueryAsync<FilterTests.SampleTemplate>(TestContext.Current.CancellationToken);
        await act.Should().ThrowAsync<ArgumentException>();

        var activity = capture.Activities.Should().ContainSingle(a => a.OperationName == "PqsQuery").Subject;
        activity.GetTagItem(PqsClientActivityTags.DamlTemplateId).Should().Be(
            TemplateExtensions.GetTemplateId<FilterTests.SampleTemplate>());
        activity.Status.Should().Be(ActivityStatusCode.Error);
        activity.GetTagItem(ActivityExtensions.ErrorType).Should().Be(typeof(ArgumentException).FullName);
    }

    [Fact]
    public async Task QueryAsync_tags_the_PqsQuery_activity_with_the_interface_id_and_records_the_error()
    {
        using var capture = ActivityCapture.Of(PqsClient.ActivitySourceName);

        var client = CreateClient();

        var act = async () => await client.QueryAsync<ISampleInterface, SampleView>(
            TestContext.Current.CancellationToken);
        await act.Should().ThrowAsync<ArgumentException>();

        var activity = capture.Activities.Should().ContainSingle(a => a.OperationName == "PqsQuery").Subject;
        activity.GetTagItem(PqsClientActivityTags.DamlTemplateId).Should().Be(
            PqsClient.GetDamlTypeId<ISampleInterface>());
        activity.Status.Should().Be(ActivityStatusCode.Error);
        activity.GetTagItem(ActivityExtensions.ErrorType).Should().Be(typeof(ArgumentException).FullName);
    }

    [Fact]
    public async Task QueryOneAsync_tags_the_PqsQueryOne_activity_with_daml_template_id_and_records_the_error()
    {
        using var capture = ActivityCapture.Of(PqsClient.ActivitySourceName);

        var client = CreateClient();
        var filter = Filter.Field<FilterTests.SampleTemplate>(t => t.Initiator, $"party::{Guid.NewGuid():N}");

        var act = async () => await client.QueryOneAsync<FilterTests.SampleTemplate>(
            filter, TestContext.Current.CancellationToken);
        await act.Should().ThrowAsync<ArgumentException>();

        var activity = capture.Activities.Should().ContainSingle(a => a.OperationName == "PqsQueryOne").Subject;
        activity.GetTagItem(PqsClientActivityTags.DamlTemplateId).Should().Be(
            TemplateExtensions.GetTemplateId<FilterTests.SampleTemplate>());
        activity.Status.Should().Be(ActivityStatusCode.Error);
        activity.GetTagItem(ActivityExtensions.ErrorType).Should().Be(typeof(ArgumentException).FullName);
    }

    [Fact]
    public async Task ExistsAsync_tags_the_PqsExists_activity_with_daml_template_id_and_records_the_error()
    {
        using var capture = ActivityCapture.Of(PqsClient.ActivitySourceName);

        var client = CreateClient();
        var contractId = new ContractId<FilterTests.SampleTemplate>("00contract123");

        var act = async () => await client.ExistsAsync(contractId, TestContext.Current.CancellationToken);
        await act.Should().ThrowAsync<ArgumentException>();

        var activity = capture.Activities.Should().ContainSingle(a => a.OperationName == "PqsExists").Subject;
        activity.GetTagItem(PqsClientActivityTags.DamlTemplateId).Should().Be(
            TemplateExtensions.GetTemplateId<FilterTests.SampleTemplate>());
        activity.Status.Should().Be(ActivityStatusCode.Error);
        activity.GetTagItem(ActivityExtensions.ErrorType).Should().Be(typeof(ArgumentException).FullName);
    }
}
