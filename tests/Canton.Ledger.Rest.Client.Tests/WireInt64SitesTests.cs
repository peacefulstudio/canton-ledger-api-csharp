// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Reflection;
using System.Text.Json.Serialization;
using AwesomeAssertions;
using Xunit;

namespace Canton.Ledger.Rest.Client.Tests;

public sealed class WireInt64SitesTests
{
    public static TheoryData<Type, string> Sites()
    {
        var data = new TheoryData<Type, string>();
        foreach (var owner in WireInt64Sites.ByOwner)
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
    public void ByOwner_names_no_Daml_payload_type()
    {
        var payloadTypes = new[]
        {
            typeof(Canton.Ledger.Rest.Client.Raw.Value),
            typeof(Canton.Ledger.Rest.Client.Raw.Record),
        };

        WireInt64Sites.ByOwner.Keys.Should().NotIntersectWith(payloadTypes);
    }

    [Fact]
    public void ByOwner_names_no_request_type()
    {
        WireInt64Sites.ByOwner.Keys.Should()
            .NotContain(type => type.Name.EndsWith("Request", StringComparison.Ordinal));
    }
}
