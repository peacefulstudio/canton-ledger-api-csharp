// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Runtime;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;

namespace Canton.Ledger.Testing.Tests;

internal sealed record DemoHolding(
    [property: DamlFieldAttribute("owner")] Party Owner,
    [property: DamlFieldAttribute("amount")] decimal Amount) : ITemplate
{
    public static Identifier TemplateId { get; } = new("test-pkg", "MiniDemo.Holding", "Holding");
    public static string PackageId => "test-pkg";
    public static string PackageName => "test-package";
    public static Version PackageVersion { get; } = new(0, 1, 0);
    public static DamlTypeDescriptor DamlTypeId { get; } = new(TemplateId, DamlTypeKind.Template, PackageName);

    public DamlRecord ToRecord() => DamlRecord.Create(
        DamlField.Create("owner", new DamlParty((string)Owner)),
        DamlField.Create("amount", new DamlNumeric(Amount)));

    public static DemoHolding FromRecord(DamlRecord record) => new(
        new Party(((DamlParty)record.GetRequiredField("owner")).Value),
        ((DamlNumeric)record.GetRequiredField("amount")).Value);
}

internal sealed record OtherHolding(Party Owner) : ITemplate
{
    public static Identifier TemplateId { get; } = new("test-pkg", "MiniDemo.OtherHolding", "OtherHolding");
    public static string PackageId => "test-pkg";
    public static string PackageName => "test-package";
    public static Version PackageVersion { get; } = new(0, 1, 0);
    public static DamlTypeDescriptor DamlTypeId { get; } = new(TemplateId, DamlTypeKind.Template, PackageName);

    public DamlRecord ToRecord() => DamlRecord.Create(DamlField.Create("owner", new DamlParty((string)Owner)));

    public static OtherHolding FromRecord(DamlRecord record) =>
        new(new Party(((DamlParty)record.GetRequiredField("owner")).Value));
}

internal interface IDemoHoldingView : IDamlInterface, IHasView<DemoHoldingView>
{
    static Identifier IDamlInterface.InterfaceId => InterfaceId;
    public static new Identifier InterfaceId { get; } = new("test-ipkg", "MiniDemo.IHolding", "IHolding");
    static string IDamlInterface.PackageId => "test-ipkg";
    static string IDamlInterface.PackageName => "test-interface-package";
    static Version IDamlInterface.PackageVersion => new(0, 1, 0);

    static DamlTypeDescriptor IDamlType.DamlTypeId =>
        new(new Identifier("test-ipkg", "MiniDemo.IHolding", "IHolding"), DamlTypeKind.Interface, "test-interface-package");
}

internal sealed record DemoHoldingView([property: DamlFieldAttribute("amount")] decimal Amount) : IDamlRecord
{
    public DamlRecord ToRecord() => DamlRecord.Create(DamlField.Create("amount", new DamlNumeric(Amount)));

    public static DemoHoldingView FromRecord(DamlRecord record) =>
        new(record.GetRequiredField("amount").As<DamlNumeric>().Value);
}

internal interface IKeyedHoldingView : IDamlInterface, IHasView<KeyedHoldingView>
{
    static Identifier IDamlInterface.InterfaceId => InterfaceId;
    public static new Identifier InterfaceId { get; } = new("test-kpkg", "MiniDemo.IKeyed", "IKeyed");
    static string IDamlInterface.PackageId => "test-kpkg";
    static string IDamlInterface.PackageName => "test-keyed-package";
    static Version IDamlInterface.PackageVersion => new(0, 1, 0);

    static DamlTypeDescriptor IDamlType.DamlTypeId =>
        new(new Identifier("test-kpkg", "MiniDemo.IKeyed", "IKeyed"), DamlTypeKind.Interface, "test-keyed-package");
}

internal sealed record KeyedHoldingView([property: DamlFieldAttribute("amount")] decimal Amount) : IDamlRecord
{
    public DamlRecord ToRecord() => DamlRecord.Create(DamlField.Create("amount", new DamlNumeric(Amount)));

    public static KeyedHoldingView FromRecord(DamlRecord record) =>
        new(((DamlNumeric)record.Fields.ToDictionary(field => field.Label, field => field.Value)["amount"]).Value);
}
