// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Xunit;

namespace Canton.Ledger.Rest.Client.Tests;

public class SimpleStylePathFormatterTests
{
    private static (IPartyManagementServiceApi Api, RecordingHttpHandler Transport) BuildApi() =>
        RestApiFactory.Build<IPartyManagementServiceApi>();

    [Fact]
    public async Task GetParties_comma_joins_multiple_parties_in_the_path_segment()
    {
        var (api, transport) = BuildApi();
        transport.WithResponse(System.Net.HttpStatusCode.OK, """{"party_details":[]}""");

        await api.GetParties(
            ["alice::ns1", "bob::ns2"],
            identity_provider_id: "",
            TestContext.Current.CancellationToken);

        transport.LastRequest!.RequestUri!.ToString()
            .Should().Be("http://localhost:7575/v2/parties/alice%3A%3Ans1%2Cbob%3A%3Ans2?identity_provider_id=");
    }

    [Fact]
    public async Task GetParties_serializes_a_single_party_path_segment_unchanged()
    {
        var (api, transport) = BuildApi();
        transport.WithResponse(System.Net.HttpStatusCode.OK, """{"party_details":[]}""");

        await api.GetParties(
            ["alice::ns1"],
            identity_provider_id: "",
            TestContext.Current.CancellationToken);

        transport.LastRequest!.RequestUri!.ToString()
            .Should().Be("http://localhost:7575/v2/parties/alice%3A%3Ans1?identity_provider_id=");
    }

    [Fact]
    public void Format_resolves_general_formats_per_element_inside_a_collection()
    {
        var formatter = new SimpleStylePathParameterFormatter();
        formatter.AddFormat<int>("D4");

        int[] elements = [1, 42];

        var formatted = formatter.Format(
            elements,
            typeof(SimpleStylePathFormatterTests),
            typeof(SimpleStylePathFormatterTests));

        formatted.Should().Be("0001,0042");
    }

    [Fact]
    public void Format_resolves_container_specific_formats_for_collection_elements()
    {
        var formatter = new SimpleStylePathParameterFormatter();
        formatter.AddFormat<SimpleStylePathFormatterTests, int>("D2");

        int[] elements = [1, 42];

        var formatted = formatter.Format(
            elements,
            typeof(SimpleStylePathFormatterTests),
            typeof(SimpleStylePathFormatterTests));

        formatted.Should().Be("01,42");
    }

    [Fact]
    public async Task GetPreferredPackageVersion_still_expands_query_collections_per_element()
    {
        var (api, transport) = RestApiFactory.Build<IInteractiveSubmissionServiceApi>();
        transport.WithResponse(System.Net.HttpStatusCode.OK, "{}");

        await api.GetPreferredPackageVersion(
            ["alice::ns1", "bob::ns2"],
            package_name: "my-pkg",
            synchronizer_id: "",
            vetting_valid_at: null,
            TestContext.Current.CancellationToken);

        transport.LastRequest!.RequestUri!.Query
            .Should().Contain("parties=alice%3A%3Ans1&parties=bob%3A%3Ans2");
    }
}
