// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Runtime;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;

namespace Canton.Ledger.Testing.Tests;

internal sealed record DemoAsset(Party Issuer, Party Owner, string Name, decimal Amount) : ITemplate
{
    public static Identifier TemplateId { get; } = new("test-pkg", "MiniDemo.Asset", "Asset");
    public static string PackageId => "test-pkg";
    public static string PackageName => "test-package";
    public static Version PackageVersion { get; } = new(0, 1, 0);
    public static DamlTypeDescriptor DamlTypeId { get; } = new(TemplateId, DamlTypeKind.Template, PackageName);

    public DamlRecord ToRecord() => DamlRecord.Create(
        DamlField.Create("issuer", new DamlParty((string)Issuer)),
        DamlField.Create("owner", new DamlParty((string)Owner)),
        DamlField.Create("name", new DamlText(Name)),
        DamlField.Create("amount", new DamlNumeric(Amount)));

    public static DemoAsset FromRecord(DamlRecord record)
    {
        var issuer = ((DamlParty)record.GetRequiredField("issuer")).Value;
        var owner = ((DamlParty)record.GetRequiredField("owner")).Value;
        var name = ((DamlText)record.GetRequiredField("name")).Value;
        var amount = ((DamlNumeric)record.GetRequiredField("amount")).Value;
        return new DemoAsset(new Party(issuer), new Party(owner), name, amount);
    }
}

internal sealed record OtherAsset(Party Owner) : ITemplate
{
    public static Identifier TemplateId { get; } = new("test-pkg", "MiniDemo.Other", "Other");
    public static string PackageId => "test-pkg";
    public static string PackageName => "test-package";
    public static Version PackageVersion { get; } = new(0, 1, 0);
    public static DamlTypeDescriptor DamlTypeId { get; } = new(TemplateId, DamlTypeKind.Template, PackageName);

    public DamlRecord ToRecord() => DamlRecord.Create(DamlField.Create("owner", new DamlParty((string)Owner)));
}
