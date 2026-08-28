// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Reflection;
using System.Text.Json.Serialization;
using AwesomeAssertions;
using Xunit;

namespace Canton.Ledger.Rest.Client.Tests;

public sealed class WireDurationSitesTests
{
    public static TheoryData<Type, string> Sites()
    {
        var data = new TheoryData<Type, string>();
        foreach (var owner in WireDurationSites.ByOwner)
        {
            foreach (var jsonName in owner.Value)
            {
                data.Add(owner.Key, jsonName);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Sites))]
    public void ByOwner_names_a_string_property_that_still_exists(Type owner, string jsonName)
    {
        var property = owner
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .SingleOrDefault(candidate =>
                candidate.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name == jsonName);

        property.Should().NotBeNull($"{owner.Name} must still declare a property named '{jsonName}'");
        property.PropertyType.Should().Be<string>();
    }

    [Fact]
    public void ByOwner_claims_no_property_that_WireInt64Sites_already_claims()
    {
        foreach (var owner in WireDurationSites.ByOwner)
        {
            if (WireInt64Sites.ByOwner.TryGetValue(owner.Key, out var int64Names))
                owner.Value.Should().NotIntersectWith(int64Names);
        }
    }

    [Fact]
    public void ByOwner_omits_MinLedgerTime_whose_served_envelope_nests_the_bound_under_a_oneOf()
    {
        WireDurationSites.ByOwner.Keys.Should().NotContain(typeof(Canton.Ledger.Rest.Client.Raw.MinLedgerTime));
    }
}
