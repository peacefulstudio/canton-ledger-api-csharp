// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Runtime;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using RuntimeIdentifier = Daml.Runtime.Data.Identifier;

namespace Canton.Ledger.Testing.Helpers;

/// <summary>
/// The template marker both transports' stream tests subscribe as <c>T</c>. Its identity is what
/// <c>MarkerMatcher</c> matches wire events against, so every transport must describe the same
/// template for a cross-transport comparison to mean anything.
/// </summary>
public sealed record TemplateMarker(string Owner) : ITemplate
{
    /// <summary>The template identity wire events must carry to classify as this marker.</summary>
    public static RuntimeIdentifier TemplateId { get; } = new("tmpl-pkg", "Sample.Token", "Holding");

    /// <inheritdoc />
    public static string PackageId => "tmpl-pkg";

    /// <inheritdoc />
    public static string PackageName => "token-impl";

    /// <inheritdoc />
    public static Version PackageVersion { get; } = new(0, 1, 0);

    /// <inheritdoc />
    public static DamlTypeDescriptor DamlTypeId { get; } = new(TemplateId, DamlTypeKind.Template, PackageName);

    /// <inheritdoc />
    public DamlRecord ToRecord() => DamlRecord.Create(
        DamlField.Create("owner", new DamlParty(Owner)));
}

/// <summary>
/// The interface marker both transports' stream tests subscribe as <c>T</c> when exercising the
/// interface-typed read path.
/// </summary>
public sealed record InterfaceMarker : IDamlInterface
{
    /// <summary>The interface identity wire events must implement to classify as this marker.</summary>
    public static RuntimeIdentifier InterfaceId { get; } = new("iface-pkg", "Token.Api", "IHolding");

    /// <inheritdoc />
    public static string PackageId => "iface-pkg";

    /// <inheritdoc />
    public static string PackageName => "token-api";

    /// <inheritdoc />
    public static Version PackageVersion { get; } = new(0, 1, 0);

    /// <inheritdoc />
    public static DamlTypeDescriptor DamlTypeId { get; } = new(InterfaceId, DamlTypeKind.Interface, PackageName);

    /// <inheritdoc />
    public DamlRecord ToRecord() => DamlRecord.Create();
}

/// <summary>
/// The view record <see cref="IViewedInterfaceMarker"/> projects, shaped exactly as
/// <c>daml-codegen-csharp</c> emits an interface view: a <see cref="DamlFieldAttribute"/> per
/// property and a <c>public static FromRecord(DamlRecord)</c> factory.
/// </summary>
public sealed record ViewedInterfaceView(
    [property: DamlFieldAttribute("amount")] decimal Amount) : IDamlRecord
{
    /// <inheritdoc />
    public DamlRecord ToRecord() => DamlRecord.Create(
        DamlField.Create("amount", new DamlNumeric(Amount)));

    /// <summary>Creates an instance from a DamlRecord.</summary>
    public static ViewedInterfaceView FromRecord(DamlRecord record) =>
        new(record.GetRequiredField("amount").As<DamlNumeric>().Value);
}

/// <summary>
/// The interface marker the typed interface-view read path subscribes as <c>TInterface</c>.
/// Unlike <see cref="InterfaceMarker"/> this is a C# <c>interface</c> carrying an
/// <see cref="IHasView{TView}"/> facet and *explicit* static interface implementations —
/// the shape <c>daml-codegen-csharp</c> actually emits, which is what the read path's
/// reflective marker lookups have to cope with.
/// </summary>
public interface IViewedInterfaceMarker : IDamlInterface, IHasView<ViewedInterfaceView>
{
    static RuntimeIdentifier IDamlInterface.InterfaceId => InterfaceId;

    /// <summary>The interface identity wire events must implement to classify as this marker.</summary>
    public static new RuntimeIdentifier InterfaceId { get; } = new("viewed-pkg", "Token.Api", "IViewedHolding");

    static string IDamlInterface.PackageId => "viewed-pkg";

    static string IDamlInterface.PackageName => "viewed-token-api";

    static Version IDamlInterface.PackageVersion => new(0, 1, 0);

    static DamlTypeDescriptor IDamlType.DamlTypeId =>
        new(InterfaceId, DamlTypeKind.Interface, "viewed-token-api");
}
