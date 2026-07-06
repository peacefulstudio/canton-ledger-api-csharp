// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Concurrent;
using System.Diagnostics;
using AwesomeAssertions;
using Daml.Runtime.Contracts;
using Xunit;

namespace Canton.Ledger.Pqs.Client.Tests;

public class PqsClientActivityTests
{
    private static PqsClient CreateClient() =>
        new(new PqsClientOptions { ConnectionString = "not a valid connection string" });

    private static (ActivityListener Listener, ConcurrentQueue<Activity> SharedActivities) ListenToPqsClient()
    {
        var activities = new ConcurrentQueue<Activity>();
        var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == PqsClient.ActivitySourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStarted = activities.Enqueue
        };
        return (listener, activities);
    }

    [Fact]
    public async Task QueryAsync_tags_the_PqsQuery_activity_with_daml_template_id_and_records_the_error()
    {
        var (listener, sharedActivities) = ListenToPqsClient();
        using var _ = listener;
        ActivitySource.AddActivityListener(listener);

        var client = CreateClient();

        var act = async () => await client.QueryAsync<FilterTests.SampleTemplate>(TestContext.Current.CancellationToken);
        await act.Should().ThrowAsync<ArgumentException>();

        var activity = sharedActivities.Should().ContainSingle(a => a.OperationName == "PqsQuery").Subject;
        activity.GetTagItem(PqsClientActivityTags.DamlTemplateId).Should().Be(
            TemplateExtensions.GetTemplateId<FilterTests.SampleTemplate>());
        activity.Status.Should().Be(ActivityStatusCode.Error);
        activity.GetTagItem(ActivityHelper.ErrorType).Should().Be(typeof(ArgumentException).FullName);
    }

    [Fact]
    public async Task QueryOneAsync_tags_the_PqsQueryOne_activity_with_daml_template_id_and_records_the_error()
    {
        var (listener, sharedActivities) = ListenToPqsClient();
        using var _ = listener;
        ActivitySource.AddActivityListener(listener);

        var client = CreateClient();
        var filter = Filter.Field<FilterTests.SampleTemplate>(t => t.Initiator, $"party::{Guid.NewGuid():N}");

        var act = async () => await client.QueryOneAsync<FilterTests.SampleTemplate>(
            filter, TestContext.Current.CancellationToken);
        await act.Should().ThrowAsync<ArgumentException>();

        var activity = sharedActivities.Should().ContainSingle(a => a.OperationName == "PqsQueryOne").Subject;
        activity.GetTagItem(PqsClientActivityTags.DamlTemplateId).Should().Be(
            TemplateExtensions.GetTemplateId<FilterTests.SampleTemplate>());
        activity.Status.Should().Be(ActivityStatusCode.Error);
        activity.GetTagItem(ActivityHelper.ErrorType).Should().Be(typeof(ArgumentException).FullName);
    }

    [Fact]
    public async Task ExistsAsync_tags_the_PqsExists_activity_with_daml_template_id_and_records_the_error()
    {
        var (listener, sharedActivities) = ListenToPqsClient();
        using var _ = listener;
        ActivitySource.AddActivityListener(listener);

        var client = CreateClient();
        var contractId = new ContractId<FilterTests.SampleTemplate>("00contract123");

        var act = async () => await client.ExistsAsync(contractId, TestContext.Current.CancellationToken);
        await act.Should().ThrowAsync<ArgumentException>();

        var activity = sharedActivities.Should().ContainSingle(a => a.OperationName == "PqsExists").Subject;
        activity.GetTagItem(PqsClientActivityTags.DamlTemplateId).Should().Be(
            TemplateExtensions.GetTemplateId<FilterTests.SampleTemplate>());
        activity.Status.Should().Be(ActivityStatusCode.Error);
        activity.GetTagItem(ActivityHelper.ErrorType).Should().Be(typeof(ArgumentException).FullName);
    }
}
