// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Runtime.Contracts;
using Daml.Runtime.Data;

namespace Canton.Ledger.ReadmeSnippets.Tests;

// Minimal stand-ins for codegen-generated Daml template types. The README quick start
// references placeholder templates (MyTemplate, Asset, Agreement); these stubs implement
// just enough of the ITemplate surface for the snippet harness to compile against the
// real client API. They are never exercised against a live participant.

public sealed record MyTemplate(string Field1, string Field2) : ITemplate
{
    public static Identifier TemplateId { get; } = new("quickstart", "Quickstart", "MyTemplate");
    public static string PackageId => "quickstart";
    public static string PackageName => "quickstart";
    public static Version PackageVersion { get; } = new(0, 0, 1);
    public static DamlTypeDescriptor DamlTypeId { get; } = new(TemplateId, DamlTypeKind.Template, PackageName);

    public DamlRecord ToRecord() => DamlRecord.Create(
        DamlField.Create("field1", new DamlText(Field1)),
        DamlField.Create("field2", new DamlText(Field2)));
}

public sealed record Asset(
    [property: DamlFieldAttribute("owner")] Party Owner,
    [property: DamlFieldAttribute("name")] string Name,
    [property: DamlFieldAttribute("value")] decimal Value) : ITemplate
{
    public static Identifier TemplateId { get; } = new("quickstart", "Quickstart", "Asset");
    public static string PackageId => "quickstart";
    public static string PackageName => "quickstart";
    public static Version PackageVersion { get; } = new(0, 0, 1);
    public static DamlTypeDescriptor DamlTypeId { get; } = new(TemplateId, DamlTypeKind.Template, PackageName);

    public DamlRecord ToRecord() => DamlRecord.Create(
        DamlField.Create("owner", Owner.ToDamlValue()),
        DamlField.Create("name", new DamlText(Name)),
        DamlField.Create("value", new DamlNumeric(Value)));

    // A codegen-emitted choice argument record. ToRecord() yields the DamlValue the
    // 3-arg ExerciseCommand.For expects for the choice argument.
    public sealed record Transfer(string NewOwner)
    {
        public DamlRecord ToRecord() => DamlRecord.Create(
            DamlField.Create("newOwner", new DamlText(NewOwner)));
    }
}

public sealed record Agreement(
    [property: DamlFieldAttribute("initiator")] string Initiator,
    [property: DamlFieldAttribute("counterparty")] string Counterparty,
    [property: DamlFieldAttribute("status")] string Status) : ITemplate
{
    public static Identifier TemplateId { get; } = new("quickstart", "Quickstart", "Agreement");
    public static string PackageId => "quickstart";
    public static string PackageName => "quickstart";
    public static Version PackageVersion { get; } = new(0, 0, 1);
    public static DamlTypeDescriptor DamlTypeId { get; } = new(TemplateId, DamlTypeKind.Template, PackageName);

    public DamlRecord ToRecord() => DamlRecord.Create(
        DamlField.Create("initiator", new DamlText(Initiator)),
        DamlField.Create("counterparty", new DamlText(Counterparty)),
        DamlField.Create("status", new DamlText(Status)));
}
