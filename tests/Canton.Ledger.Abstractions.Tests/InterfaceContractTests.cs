// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Daml.Runtime;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Xunit;

namespace Canton.Ledger.Abstractions.Tests;

public class InterfaceContractTests
{
    [Fact]
    public void InterfaceContract_still_constructs_positionally_with_its_original_two_arguments()
    {
        var id = new ContractId<ITestInterface>("cid1");
        var view = new TestView(1m);

        var contract = new InterfaceContract<ITestInterface, TestView>(id, view);
        var (deconstructedId, deconstructedView) = contract;

        deconstructedId.Should().Be(id);
        deconstructedView.Should().Be(view);
        contract.Key.Should().BeNull();
    }

    [Fact]
    public void InterfaceContract_carries_the_key_supplied_through_its_init_property()
    {
        var id = new ContractId<ITestInterface>("cid1");
        var view = new TestView(1m);
        var key = new ContractKey(new DamlParty("alice"), new Identifier("pkg", "Module", "Template"));

        var contract = new InterfaceContract<ITestInterface, TestView>(id, view) { Key = key };

        contract.Key.Should().Be(key);
    }

    private interface ITestInterface : IDamlInterface, IHasView<TestView>
    {
        static Identifier IDamlInterface.InterfaceId => InterfaceId;
        public static new Identifier InterfaceId { get; } = new("pkg", "Module", "Interface");
        static string IDamlInterface.PackageId => "pkg";
        static string IDamlInterface.PackageName => "pkg-name";
        static Version IDamlInterface.PackageVersion => new(0, 1, 0);

        static DamlTypeDescriptor IDamlType.DamlTypeId =>
            new(new Identifier("pkg", "Module", "Interface"), DamlTypeKind.Interface, "pkg-name");
    }

    private sealed record TestView(decimal Amount) : IDamlRecord, IDamlRecord<TestView>
    {
        public DamlRecord ToRecord() => DamlRecord.Create(DamlField.Create("amount", new DamlNumeric(Amount)));

        public static TestView FromRecord(DamlRecord record) =>
            new(record.GetRequiredField("amount").As<DamlNumeric>().Value);
    }
}
