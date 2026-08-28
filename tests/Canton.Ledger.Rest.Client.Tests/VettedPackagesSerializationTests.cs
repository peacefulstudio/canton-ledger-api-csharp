// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using AwesomeAssertions;
using Canton.Ledger.Rest.Client.Raw;
using Xunit;

namespace Canton.Ledger.Rest.Client.Tests;

public sealed class VettedPackagesSerializationTests
{
    private static VettedPackagesRef PackageRef() => new() { PackageName = "richtypes" };

    [Fact]
    public void VettedPackagesChange_nests_a_vet_change_under_operation()
    {
        var change = new VettedPackagesChange
        {
            Operation = new VettedPackagesChangeOperation
            {
                Vet = new VettedPackagesChange_Vet { Packages = [PackageRef()] },
            },
        };

        var json = JsonSerializer.Serialize(change, RestRefitSettings.SerializerOptions);

        using var document = JsonDocument.Parse(json);
        var operation = document.RootElement.GetProperty("operation");
        operation.EnumerateObject().Select(property => property.Name).Should().Equal("Vet");
        operation.GetProperty("Vet").GetProperty("value").GetProperty("packages")[0]
            .GetProperty("packageName").GetString().Should().Be("richtypes");
    }

    [Fact]
    public void VettedPackagesChange_nests_an_unvet_change_under_operation()
    {
        var change = new VettedPackagesChange
        {
            Operation = new VettedPackagesChangeOperation
            {
                Unvet = new VettedPackagesChange_Unvet { Packages = [PackageRef()] },
            },
        };

        var json = JsonSerializer.Serialize(change, RestRefitSettings.SerializerOptions);

        using var document = JsonDocument.Parse(json);
        var operation = document.RootElement.GetProperty("operation");
        operation.EnumerateObject().Select(property => property.Name).Should().Equal("Unvet");
        operation.GetProperty("Unvet").GetProperty("value").GetProperty("packages")[0]
            .GetProperty("packageName").GetString().Should().Be("richtypes");
    }

    [Fact]
    public void VettedPackagesChange_reads_a_served_vet_change_through_operation()
    {
        var change = JsonSerializer.Deserialize<VettedPackagesChange>(
            """{"operation":{"Vet":{"value":{"packages":[{"packageName":"richtypes"}]}}}}""",
            RestRefitSettings.SerializerOptions);

        change.Should().NotBeNull();
        change.Operation.Should().NotBeNull();
        change.Operation.Vet.Should().NotBeNull();
        change.Operation.Vet.Packages.Should().ContainSingle()
            .Which.PackageName.Should().Be("richtypes");
        change.Operation.Unvet.Should().BeNull();
    }

    [Fact]
    public void VettedPackagesChange_reads_a_served_unvet_change_through_operation()
    {
        var change = JsonSerializer.Deserialize<VettedPackagesChange>(
            """{"operation":{"Unvet":{"value":{"packages":[{"packageName":"richtypes"}]}}}}""",
            RestRefitSettings.SerializerOptions);

        change.Should().NotBeNull();
        change.Operation.Should().NotBeNull();
        change.Operation.Unvet.Should().NotBeNull();
        change.Operation.Unvet.Packages.Should().ContainSingle()
            .Which.PackageName.Should().Be("richtypes");
        change.Operation.Vet.Should().BeNull();
    }

    [Fact]
    public void PriorTopologySerial_nests_a_prior_serial_under_serial()
    {
        var expected = new PriorTopologySerial
        {
            Serial = new PriorTopologySerialSerial { Prior = 7 },
        };

        var json = JsonSerializer.Serialize(expected, RestRefitSettings.SerializerOptions);

        json.Should().Be("""{"serial":{"Prior":{"value":7}}}""");
    }

    [Fact]
    public void PriorTopologySerial_reads_a_served_prior_serial_through_serial()
    {
        var expected = JsonSerializer.Deserialize<PriorTopologySerial>(
            """{"serial":{"Prior":{"value":7}}}""", RestRefitSettings.SerializerOptions);

        expected.Should().NotBeNull();
        expected.Serial.Should().NotBeNull();
        expected.Serial.Prior.Should().Be(7);
    }

    [Fact]
    public void PriorTopologySerial_nests_a_no_prior_serial_under_serial()
    {
        var expected = new PriorTopologySerial
        {
            Serial = new PriorTopologySerialSerial { NoPrior = new PriorTopologySerial_NoPrior() },
        };

        var json = JsonSerializer.Serialize(expected, RestRefitSettings.SerializerOptions);

        json.Should().Be("""{"serial":{"NoPrior":{}}}""");
    }

    [Fact]
    public void PriorTopologySerial_reads_a_served_no_prior_serial_through_serial()
    {
        var expected = JsonSerializer.Deserialize<PriorTopologySerial>(
            """{"serial":{"NoPrior":{}}}""", RestRefitSettings.SerializerOptions);

        expected.Should().NotBeNull();
        expected.Serial.Should().NotBeNull();
        expected.Serial.NoPrior.Should().NotBeNull();
        expected.Serial.Prior.Should().BeNull();
        expected.Serial.AdditionalProperties.Should().BeEmpty();
    }

    [Fact]
    public void PriorTopologySerial_carries_the_served_empty_arm_it_cannot_model()
    {
        var expected = JsonSerializer.Deserialize<PriorTopologySerial>(
            """{"serial":{"Empty":{}}}""", RestRefitSettings.SerializerOptions);

        expected.Should().NotBeNull();
        expected.Serial.Should().NotBeNull();
        expected.Serial.Prior.Should().BeNull();
        expected.Serial.NoPrior.Should().BeNull();
        expected.Serial.AdditionalProperties.Should().ContainKey("Empty");

        var json = JsonSerializer.Serialize(expected, RestRefitSettings.SerializerOptions);

        json.Should().Be("""{"serial":{"Empty":{}}}""");
    }

    [Fact]
    public void UpdateVettedPackagesRequest_nests_every_change_and_the_expected_serial()
    {
        var request = new UpdateVettedPackagesRequest
        {
            Changes =
            [
                new VettedPackagesChange
                {
                    Operation = new VettedPackagesChangeOperation
                    {
                        Vet = new VettedPackagesChange_Vet { Packages = [PackageRef()] },
                    },
                },
            ],
            ExpectedTopologySerial = new PriorTopologySerial
            {
                Serial = new PriorTopologySerialSerial { Prior = 12 },
            },
        };

        var json = JsonSerializer.Serialize(request, RestRefitSettings.SerializerOptions);

        using var document = JsonDocument.Parse(json);
        document.RootElement.GetProperty("changes")[0].GetProperty("operation")
            .EnumerateObject().Select(property => property.Name).Should().Equal("Vet");
        document.RootElement.GetProperty("expectedTopologySerial").GetProperty("serial")
            .GetProperty("Prior").GetProperty("value").GetInt32().Should().Be(12);
    }
}
