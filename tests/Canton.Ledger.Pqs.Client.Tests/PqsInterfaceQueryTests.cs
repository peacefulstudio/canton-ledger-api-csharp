// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System;
using AwesomeAssertions;
using Daml.Runtime;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Xunit;

namespace Canton.Ledger.Pqs.Client.Tests;

public class PqsInterfaceQueryTests
{
    [Fact]
    public void GetDamlTypeId_builds_the_package_name_qualified_id_for_a_template()
    {
        PqsClient.GetDamlTypeId<FilterTests.SampleTemplate>()
            .Should().Be("test-package:Test.Module:SampleTemplate");
    }

    [Fact]
    public void GetDamlTypeId_builds_the_package_name_qualified_id_for_an_interface()
    {
        PqsClient.GetDamlTypeId<ISampleInterface>()
            .Should().Be("test-interface-package:Test.Module:SampleInterface");
    }

    [Fact]
    public void GetDamlTypeId_throws_when_the_package_name_is_empty()
    {
        var act = () => PqsClient.GetDamlTypeId<BlankPackageType>();

        act.Should().Throw<InvalidOperationException>()
            .Which.Message.Should().Contain(typeof(BlankPackageType).FullName!);
    }

    [Fact]
    public void DeserializeInterfaceContract_maps_contract_id_and_view_payload()
    {
        const string payload = """{"amount":"123.45"}""";

        var contract = PqsClient.DeserializeInterfaceContract<ISampleInterface, SampleView>(
            "00cid", payload, PqsClient.DefaultJsonSerializerOptions);

        contract.Id.Value.Should().Be("00cid");
        contract.View.Amount.Should().Be(123.45m);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("  null  ")]
    public void DeserializeInterfaceContract_throws_InvalidOperationException_for_null_payload(string payloadJson)
    {
        var act = () => PqsClient.DeserializeInterfaceContract<ISampleInterface, SampleView>(
            "00cid", payloadJson, PqsClient.DefaultJsonSerializerOptions);

        act.Should().Throw<InvalidOperationException>()
            .Which.Message.Should()
                .Contain("00cid")
                .And.Contain(typeof(SampleView).FullName!);
    }
}

internal interface ISampleInterface : IDamlInterface, IHasView<SampleView>
{
    static Identifier IDamlInterface.InterfaceId => InterfaceId;

    public static new Identifier InterfaceId { get; } = new("ipkg456", "Test.Module", "SampleInterface");

    static string IDamlInterface.PackageId => "ipkg456";

    static string IDamlInterface.PackageName => "test-interface-package";

    static Version IDamlInterface.PackageVersion => new(0, 1, 0);

    static DamlTypeDescriptor IDamlType.DamlTypeId =>
        new(new Identifier("ipkg456", "Test.Module", "SampleInterface"), DamlTypeKind.Interface, "test-interface-package");
}

internal sealed record SampleView([property: DamlFieldAttribute("amount")] decimal Amount) : IDamlRecord
{
    public DamlRecord ToRecord() => DamlRecord.Create(DamlField.Create("amount", new DamlNumeric(Amount)));

    public static SampleView FromRecord(DamlRecord record) =>
        new(record.GetRequiredField("amount").As<DamlNumeric>().Value);
}

internal sealed record BlankPackageType : IDamlType
{
    public static DamlTypeDescriptor DamlTypeId =>
        new(new Identifier("hash", "Test.Module", "BlankPackageType"), DamlTypeKind.Template, string.Empty);
}
