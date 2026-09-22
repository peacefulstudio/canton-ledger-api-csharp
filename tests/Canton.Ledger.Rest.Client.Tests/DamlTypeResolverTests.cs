// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Runtime.Serialization;
using System.Text.Json;
using AwesomeAssertions;
using Canton.Ledger.Abstractions;
using Daml.Codegen.Testing.Conformance.ContractKeys;
using Daml.Codegen.Testing.Conformance.RichTypes;
using Daml.Runtime;
using Daml.Runtime.Commands;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Xunit;
using RuntimeIdentifier = Daml.Runtime.Data.Identifier;

namespace Canton.Ledger.Rest.Client.Tests;

public class DamlTypeResolverTests
{
    private sealed record SplitArgument([property: DamlFieldAttribute("quantity")] long Quantity) : IDamlRecord<SplitArgument>
    {
        public DamlRecord ToRecord() => DamlRecord.Create(DamlField.Create("quantity", new DamlInt64(Quantity)));

        public static DamlRecord __ReadDamlLfJson(JsonElement json, DamlLfJsonDecodeContext context) =>
            TestRecordReader.Read(json, context, ("quantity", DamlLfJsonDecoders.ReadInt64));

        public static SplitArgument FromRecord(DamlRecord record) =>
            new(record.GetRequiredField("quantity").As<DamlInt64>().Value);
    }

    private sealed record SplittableMarker(Party Owner)
        : ITemplate, IDamlRecord<SplittableMarker>, IHasKey<SplittableMarker, Party>
    {
        public static RuntimeIdentifier TemplateId { get; } = new("late-pkg", "Late.Token", "Splittable");
        public static string PackageId => "late-pkg";
        public static string PackageName => "late-token";
        public static Version PackageVersion { get; } = new(0, 1, 0);
        public static DamlTypeDescriptor DamlTypeId { get; } = new(TemplateId, DamlTypeKind.Template, PackageName);

        public static KeyDescriptor<SplittableMarker, Party> Key { get; } = new()
        {
            KeyEncoder = owner => owner.ToDamlValue(),
            KeyDecoder = value => Party.FromDamlValue(value.As<DamlParty>()),
            KeyJsonReader = DamlLfJsonDecoders.ReadParty,
        };

        public static Choice<SplittableMarker, SplitArgument, long> ChoiceSplit { get; } = new()
        {
            Name = new ChoiceName("Split"),
            Consuming = true,
            ArgumentEncoder = argument => argument.ToRecord(),
            ResultDecoder = result => result.As<DamlInt64>().Value,
            ArgumentDecoder = value => SplitArgument.FromRecord(value.As<DamlRecord>()),
            ArgumentJsonReader = SplitArgument.__ReadDamlLfJson,
            ResultJsonReader = DamlLfJsonDecoders.ReadInt64,
        };

        public DamlRecord ToRecord() => DamlRecord.Create(DamlField.Create("owner", Owner.ToDamlValue()));

        public static DamlRecord __ReadDamlLfJson(JsonElement json, DamlLfJsonDecodeContext context) =>
            TestRecordReader.Read(json, context, ("owner", DamlLfJsonDecoders.ReadParty));

        public static SplittableMarker FromRecord(DamlRecord record) =>
            new(Party.FromDamlValue(record.GetRequiredField("owner").As<DamlParty>()));
    }

    private sealed record OtherPackageSplittableMarker : ITemplate, IDamlRecord<OtherPackageSplittableMarker>
    {
        public static RuntimeIdentifier TemplateId { get; } = new("other-pkg", "Late.Token", "Splittable");
        public static string PackageId => "other-pkg";
        public static string PackageName => "late-token";
        public static Version PackageVersion { get; } = new(0, 2, 0);
        public static DamlTypeDescriptor DamlTypeId { get; } = new(TemplateId, DamlTypeKind.Template, PackageName);
        public DamlRecord ToRecord() => new(TemplateId, []);

        public static DamlRecord __ReadDamlLfJson(JsonElement json, DamlLfJsonDecodeContext context) =>
            TestRecordReader.Read(json, context);
        public static OtherPackageSplittableMarker FromRecord(DamlRecord record) => new();
    }

    private sealed record SamePackageSplittableMarker : ITemplate, IDamlRecord<SamePackageSplittableMarker>
    {
        public static RuntimeIdentifier TemplateId { get; } = new("late-pkg", "Late.Token", "Splittable");
        public static string PackageId => "late-pkg";
        public static string PackageName => "late-token";
        public static Version PackageVersion { get; } = new(0, 1, 0);
        public static DamlTypeDescriptor DamlTypeId { get; } = new(TemplateId, DamlTypeKind.Template, PackageName);
        public DamlRecord ToRecord() => new(TemplateId, []);

        public static DamlRecord __ReadDamlLfJson(JsonElement json, DamlLfJsonDecodeContext context) =>
            TestRecordReader.Read(json, context);
        public static SamePackageSplittableMarker FromRecord(DamlRecord record) => new();
    }

    private sealed record EntityV1Marker : ITemplate, IDamlRecord<EntityV1Marker>
    {
        public static RuntimeIdentifier TemplateId { get; } = new("pkgv1", "M", "E");
        public static string PackageId => "pkgv1";
        public static string PackageName => "entity";
        public static Version PackageVersion { get; } = new(1, 0, 0);
        public static DamlTypeDescriptor DamlTypeId { get; } = new(TemplateId, DamlTypeKind.Template, PackageName);
        public DamlRecord ToRecord() => new(TemplateId, []);

        public static DamlRecord __ReadDamlLfJson(JsonElement json, DamlLfJsonDecodeContext context) =>
            TestRecordReader.Read(json, context);
        public static EntityV1Marker FromRecord(DamlRecord record) => new();
    }

    private sealed record EntityV2Marker : ITemplate, IDamlRecord<EntityV2Marker>
    {
        public static RuntimeIdentifier TemplateId { get; } = new("pkgv2", "M", "E");
        public static string PackageId => "pkgv2";
        public static string PackageName => "entity";
        public static Version PackageVersion { get; } = new(2, 0, 0);
        public static DamlTypeDescriptor DamlTypeId { get; } = new(TemplateId, DamlTypeKind.Template, PackageName);
        public DamlRecord ToRecord() => new(TemplateId, []);

        public static DamlRecord __ReadDamlLfJson(JsonElement json, DamlLfJsonDecodeContext context) =>
            TestRecordReader.Read(json, context);
        public static EntityV2Marker FromRecord(DamlRecord record) => new();
    }

    private sealed record SplittableView;

    private sealed record SplittableInterface : IDamlInterface, IHasView<SplittableView>
    {
        public static RuntimeIdentifier InterfaceId { get; } = new("late-pkg", "Late.Token", "ISplittable");
        public static string PackageId => "late-pkg";
        public static string PackageName => "late-token";
        public static Version PackageVersion { get; } = new(0, 1, 0);
        public static DamlTypeDescriptor DamlTypeId { get; } = new(InterfaceId, DamlTypeKind.Interface, PackageName);

        public static Choice<SplittableInterface, SplitArgument, long> ChoiceSplit { get; } = new()
        {
            Name = new ChoiceName("Split"),
            Consuming = false,
            ArgumentEncoder = argument => argument.ToRecord(),
            ResultDecoder = result => result.As<DamlInt64>().Value,
            ArgumentDecoder = value => SplitArgument.FromRecord(value.As<DamlRecord>()),
            ArgumentJsonReader = SplitArgument.__ReadDamlLfJson,
            ResultJsonReader = DamlLfJsonDecoders.ReadInt64,
        };

        public DamlRecord ToRecord() => new(InterfaceId, []);
    }

    private sealed record OtherPackageSplittableInterface : IDamlInterface, IHasView<SplittableView>
    {
        public static RuntimeIdentifier InterfaceId { get; } = new("other-pkg", "Late.Token", "ISplittable");
        public static string PackageId => "other-pkg";
        public static string PackageName => "late-token";
        public static Version PackageVersion { get; } = new(0, 2, 0);
        public static DamlTypeDescriptor DamlTypeId { get; } = new(InterfaceId, DamlTypeKind.Interface, PackageName);
        public DamlRecord ToRecord() => new(InterfaceId, []);
    }

    private sealed record SamePackageSplittableInterface : IDamlInterface, IHasView<SplittableView>
    {
        public static RuntimeIdentifier InterfaceId { get; } = new("late-pkg", "Late.Token", "ISplittable");
        public static string PackageId => "late-pkg";
        public static string PackageName => "late-token";
        public static Version PackageVersion { get; } = new(0, 1, 0);
        public static DamlTypeDescriptor DamlTypeId { get; } = new(InterfaceId, DamlTypeKind.Interface, PackageName);
        public DamlRecord ToRecord() => new(InterfaceId, []);
    }

    private static DamlTypeResolver Unchanging(Func<IEnumerable<Type>> typeSource) => new(typeSource, () => 0L);

    [Fact]
    public void TemplateFor_switches_from_a_fallback_match_to_the_exact_package_loaded_after_it()
    {
        var loadedTypes = new List<Type> { typeof(EntityV1Marker) };
        var generation = 0L;
        var resolver = new DamlTypeResolver(() => loadedTypes.ToArray(), () => generation);
        var v2Id = new RuntimeIdentifier("pkgv2", "M", "E");

        resolver.TemplateFor(v2Id)!.Template.Should().Be<EntityV1Marker>();
        loadedTypes.Add(typeof(EntityV2Marker));
        generation++;

        resolver.TemplateFor(v2Id)!.Template.Should().Be<EntityV2Marker>();
    }

    [Fact]
    public void TemplateFor_enumerates_the_type_source_once_across_a_burst_of_misses_while_the_generation_stands_still()
    {
        var enumerations = 0;
        IEnumerable<Type> CountedTypes()
        {
            enumerations++;
            yield return typeof(SplittableMarker);
        }

        var resolver = new DamlTypeResolver(CountedTypes, () => 7L);

        for (var miss = 0; miss < 1000; miss++)
        {
            resolver.TemplateFor(new RuntimeIdentifier($"unknown-pkg-{miss}", "Unknown.Module", "Unknown")).Should().BeNull();
        }

        enumerations.Should().Be(1);
    }

    [Fact]
    public void TemplateFor_resolves_a_template_loaded_after_a_lookup_missed_it()
    {
        var loadedTypes = new List<Type>();
        var generation = 0L;
        var resolver = new DamlTypeResolver(() => loadedTypes.ToArray(), () => generation);
        var splittableId = new RuntimeIdentifier("late-pkg", "Late.Token", "Splittable");

        resolver.TemplateFor(splittableId).Should().BeNull();
        loadedTypes.Add(typeof(SplittableMarker));
        generation++;

        var resolved = resolver.TemplateFor(splittableId)!;
        resolved.Template.Should().Be<SplittableMarker>();
        resolved.Key.Should().BeSameAs(SplittableMarker.Key);
    }

    [Fact]
    public void TemplateFor_prefers_the_template_declaring_the_exact_package_id_over_a_same_named_one()
    {
        var resolver = Unchanging(() => [typeof(SplittableMarker), typeof(OtherPackageSplittableMarker)]);

        resolver.TemplateFor(new RuntimeIdentifier("other-pkg", "Late.Token", "Splittable"))
            !.Template.Should().Be<OtherPackageSplittableMarker>();
    }

    [Fact]
    public void TemplateFor_resolves_the_one_template_declaring_the_module_and_entity_under_another_package_id()
    {
        var resolver = Unchanging(() => [typeof(SplittableMarker)]);

        var resolved = resolver.TemplateFor(new RuntimeIdentifier("upgraded-pkg", "Late.Token", "Splittable"))!;

        resolved.Template.Should().Be<SplittableMarker>();
        resolved.Key.Should().BeSameAs(SplittableMarker.Key);
    }

    [Fact]
    public void TemplateFor_refuses_a_module_and_entity_declared_by_templates_of_two_other_packages()
    {
        var resolver = Unchanging(() => [typeof(SplittableMarker), typeof(OtherPackageSplittableMarker)]);

        var refusal = FluentActions.Invoking(() => resolver.TemplateFor(new RuntimeIdentifier("upgraded-pkg", "Late.Token", "Splittable")))
            .Should().Throw<TemplateTypeRequiredException>().Which;

        refusal.ConflictingTypes.Should().Equal(
            "Canton.Ledger.Rest.Client.Tests.DamlTypeResolverTests+OtherPackageSplittableMarker",
            "Canton.Ledger.Rest.Client.Tests.DamlTypeResolverTests+SplittableMarker");
        refusal.Message.Should().Be(
            "'upgraded-pkg:Late.Token:Splittable' is claimed by more than one loaded generated type (Canton.Ledger.Rest.Client.Tests.DamlTypeResolverTests+OtherPackageSplittableMarker, Canton.Ledger.Rest.Client.Tests.DamlTypeResolverTests+SplittableMarker); load exactly one assembly generated for its Daml package before reading this payload over the JSON Ledger API.");
    }

    [Fact]
    public void TemplateFor_refuses_a_template_id_claimed_by_two_generated_types()
    {
        var resolver = Unchanging(() => [typeof(SplittableMarker), typeof(SamePackageSplittableMarker)]);

        FluentActions.Invoking(() => resolver.TemplateFor(new RuntimeIdentifier("late-pkg", "Late.Token", "Splittable")))
            .Should().Throw<TemplateTypeRequiredException>()
            .Which.ConflictingTypes.Should().Equal(
                "Canton.Ledger.Rest.Client.Tests.DamlTypeResolverTests+SamePackageSplittableMarker",
                "Canton.Ledger.Rest.Client.Tests.DamlTypeResolverTests+SplittableMarker");
    }

    [Fact]
    public void TemplateFor_rechecks_an_exact_match_after_a_conflicting_same_package_type_loads()
    {
        var loadedTypes = new List<Type> { typeof(SplittableMarker) };
        var generation = 0L;
        var resolver = new DamlTypeResolver(() => loadedTypes.ToArray(), () => generation);
        var splittableId = new RuntimeIdentifier("late-pkg", "Late.Token", "Splittable");

        resolver.TemplateFor(splittableId)!.Template.Should().Be<SplittableMarker>();
        loadedTypes.Add(typeof(SamePackageSplittableMarker));
        generation++;

        FluentActions.Invoking(() => resolver.TemplateFor(splittableId))
            .Should().Throw<TemplateTypeRequiredException>()
            .Which.ConflictingTypes.Should().Equal(
                "Canton.Ledger.Rest.Client.Tests.DamlTypeResolverTests+SamePackageSplittableMarker",
                "Canton.Ledger.Rest.Client.Tests.DamlTypeResolverTests+SplittableMarker");
    }

    [Fact]
    public void ChoiceFor_resolves_the_descriptor_a_template_declares_for_a_choice()
    {
        var resolver = Unchanging(() => [typeof(SplittableMarker)]);

        var descriptor = resolver.ChoiceFor(new RuntimeIdentifier("late-pkg", "Late.Token", "Splittable"), "Split");

        descriptor.Should().BeSameAs(SplittableMarker.ChoiceSplit);
        descriptor!.ArgumentType.Should().Be<SplitArgument>();
        descriptor.ResultType.Should().Be<long>();
    }

    [Fact]
    public void ChoiceFor_names_the_choice_when_the_template_id_is_claimed_by_two_generated_types()
    {
        var resolver = Unchanging(() => [typeof(SplittableMarker), typeof(SamePackageSplittableMarker)]);

        var refusal = FluentActions.Invoking(() => resolver.ChoiceFor(new RuntimeIdentifier("late-pkg", "Late.Token", "Splittable"), "Split"))
            .Should().Throw<TemplateTypeRequiredException>().Which;

        refusal.ChoiceName.Should().Be("Split");
        refusal.Message.Should().Be(
            "choice 'Split' of 'late-pkg:Late.Token:Splittable' is claimed by more than one loaded generated type (Canton.Ledger.Rest.Client.Tests.DamlTypeResolverTests+SamePackageSplittableMarker, Canton.Ledger.Rest.Client.Tests.DamlTypeResolverTests+SplittableMarker); load exactly one assembly generated for its Daml package before reading this payload over the JSON Ledger API.");
    }

    [Fact]
    public void RequireChoice_names_the_template_and_choice_when_no_descriptor_is_loaded()
    {
        var resolver = Unchanging(() => [typeof(SplittableMarker)]);

        var refusal = FluentActions.Invoking(() => resolver.RequireChoice(new RuntimeIdentifier("late-pkg", "Late.Token", "Splittable"), "Merge"))
            .Should().Throw<TemplateTypeRequiredException>().Which;

        refusal.TypeId.Should().Be("late-pkg:Late.Token:Splittable");
        refusal.ChoiceName.Should().Be("Merge");
        refusal.Message.Should().Be(
            "No generated type is loaded for choice 'Merge' of 'late-pkg:Late.Token:Splittable'; load exactly one assembly generated for its Daml package before reading this payload over the JSON Ledger API.");
    }

    [Fact]
    public void RequireTemplate_names_the_template_when_no_generated_type_is_loaded()
    {
        var resolver = Unchanging(() => []);

        FluentActions.Invoking(() => resolver.RequireTemplate(new RuntimeIdentifier("late-pkg", "Late.Token", "Splittable")))
            .Should().Throw<TemplateTypeRequiredException>()
            .Which.Message.Should().Be(
                "No generated type is loaded for 'late-pkg:Late.Token:Splittable'; load exactly one assembly generated for its Daml package before reading this payload over the JSON Ledger API.");
    }

    [Fact]
    public void Loaded_resolves_the_generated_conformance_template_its_key_and_its_choices()
    {
        var steward = DamlTypeResolver.Loaded.TemplateFor(Steward.TemplateId)!;
        steward.Template.Should().Be<Steward>();
        steward.Key.Should().BeSameAs(Steward.Key);
        DamlTypeResolver.Loaded.ChoiceFor(Account.TemplateId, "Credit").Should().BeSameAs(Account.ChoiceCredit);
        DamlTypeResolver.Loaded.ChoiceFor(Account.TemplateId, "Archive").Should().BeSameAs(Account.ChoiceArchive);
    }

    [Fact]
    public void Loaded_resolves_the_view_type_of_a_generated_conformance_interface()
    {
        DamlTypeResolver.Loaded.ViewTypeFor(IHolding.InterfaceId).Should().Be<HoldingView>();
    }

    [Fact]
    public void InterfaceChoiceFor_resolves_the_descriptor_an_interface_declares_for_a_choice()
    {
        var resolver = Unchanging(() => [typeof(SplittableInterface)]);

        var descriptor = resolver.InterfaceChoiceFor(new RuntimeIdentifier("late-pkg", "Late.Token", "ISplittable"), "Split");

        descriptor.Should().BeSameAs(SplittableInterface.ChoiceSplit);
        descriptor!.ArgumentType.Should().Be<SplitArgument>();
        descriptor.ResultType.Should().Be<long>();
    }

    [Fact]
    public void InterfaceChoiceFor_does_not_resolve_a_choice_through_the_template_table_nor_ChoiceFor_through_an_interface()
    {
        var resolver = Unchanging(() => [typeof(SplittableMarker), typeof(SplittableInterface)]);
        var interfaceId = new RuntimeIdentifier("late-pkg", "Late.Token", "ISplittable");
        var templateId = new RuntimeIdentifier("late-pkg", "Late.Token", "Splittable");

        resolver.InterfaceChoiceFor(templateId, "Split").Should().BeNull();
        resolver.ChoiceFor(interfaceId, "Split").Should().BeNull();
    }

    [Fact]
    public void InterfaceChoiceFor_returns_nothing_for_a_choice_the_interface_does_not_declare()
    {
        var resolver = Unchanging(() => [typeof(SplittableInterface)]);

        resolver.InterfaceChoiceFor(new RuntimeIdentifier("late-pkg", "Late.Token", "ISplittable"), "Merge").Should().BeNull();
    }

    [Fact]
    public void InterfaceChoiceFor_prefers_the_interface_declaring_the_exact_package_id_over_a_same_named_one()
    {
        var resolver = Unchanging(() => [typeof(SplittableInterface), typeof(OtherPackageSplittableInterface)]);

        resolver.ViewTypeFor(new RuntimeIdentifier("other-pkg", "Late.Token", "ISplittable")).Should().Be<SplittableView>();
        resolver.InterfaceChoiceFor(new RuntimeIdentifier("other-pkg", "Late.Token", "ISplittable"), "Split").Should().BeNull();
        resolver.InterfaceChoiceFor(new RuntimeIdentifier("late-pkg", "Late.Token", "ISplittable"), "Split")
            .Should().BeSameAs(SplittableInterface.ChoiceSplit);
    }

    [Fact]
    public void InterfaceChoiceFor_resolves_the_one_interface_declaring_the_module_and_entity_under_another_package_id()
    {
        var resolver = Unchanging(() => [typeof(SplittableInterface)]);

        resolver.InterfaceChoiceFor(new RuntimeIdentifier("upgraded-pkg", "Late.Token", "ISplittable"), "Split")
            .Should().BeSameAs(SplittableInterface.ChoiceSplit);
    }

    [Fact]
    public void InterfaceChoiceFor_refuses_a_module_and_entity_declared_by_interfaces_of_two_other_packages()
    {
        var resolver = Unchanging(() => [typeof(SplittableInterface), typeof(OtherPackageSplittableInterface)]);

        var refusal = FluentActions.Invoking(() => resolver.InterfaceChoiceFor(new RuntimeIdentifier("upgraded-pkg", "Late.Token", "ISplittable"), "Split"))
            .Should().Throw<TemplateTypeRequiredException>().Which;

        refusal.ChoiceName.Should().Be("Split");
        refusal.ConflictingTypes.Should().Equal(
            "Canton.Ledger.Rest.Client.Tests.DamlTypeResolverTests+OtherPackageSplittableInterface",
            "Canton.Ledger.Rest.Client.Tests.DamlTypeResolverTests+SplittableInterface");
        refusal.Message.Should().Be(
            "choice 'Split' of 'upgraded-pkg:Late.Token:ISplittable' is claimed by more than one loaded generated type (Canton.Ledger.Rest.Client.Tests.DamlTypeResolverTests+OtherPackageSplittableInterface, Canton.Ledger.Rest.Client.Tests.DamlTypeResolverTests+SplittableInterface); load exactly one assembly generated for its Daml package before reading this payload over the JSON Ledger API.");
    }

    [Fact]
    public void InterfaceChoiceFor_refuses_an_interface_id_claimed_by_two_generated_types()
    {
        var resolver = Unchanging(() => [typeof(SplittableInterface), typeof(SamePackageSplittableInterface)]);

        FluentActions.Invoking(() => resolver.InterfaceChoiceFor(new RuntimeIdentifier("late-pkg", "Late.Token", "ISplittable"), "Split"))
            .Should().Throw<TemplateTypeRequiredException>()
            .Which.ConflictingTypes.Should().Equal(
                "Canton.Ledger.Rest.Client.Tests.DamlTypeResolverTests+SamePackageSplittableInterface",
                "Canton.Ledger.Rest.Client.Tests.DamlTypeResolverTests+SplittableInterface");
    }

    [Fact]
    public void RequireInterfaceChoice_names_the_interface_and_choice_when_no_descriptor_is_loaded()
    {
        var resolver = Unchanging(() => [typeof(SplittableInterface)]);

        var refusal = FluentActions.Invoking(() => resolver.RequireInterfaceChoice(new RuntimeIdentifier("late-pkg", "Late.Token", "ISplittable"), "Merge"))
            .Should().Throw<TemplateTypeRequiredException>().Which;

        refusal.TypeId.Should().Be("late-pkg:Late.Token:ISplittable");
        refusal.ChoiceName.Should().Be("Merge");
        refusal.Message.Should().Be(
            "No generated type is loaded for choice 'Merge' of 'late-pkg:Late.Token:ISplittable'; load exactly one assembly generated for its Daml package before reading this payload over the JSON Ledger API.");
    }

    [Fact]
    public void RequireInterfaceChoice_names_the_interface_and_choice_when_no_interface_is_loaded()
    {
        var resolver = Unchanging(() => []);

        FluentActions.Invoking(() => resolver.RequireInterfaceChoice(new RuntimeIdentifier("late-pkg", "Late.Token", "ISplittable"), "Split"))
            .Should().Throw<TemplateTypeRequiredException>()
            .Which.Message.Should().Be(
                "No generated type is loaded for choice 'Split' of 'late-pkg:Late.Token:ISplittable'; load exactly one assembly generated for its Daml package before reading this payload over the JSON Ledger API.");
    }

    [Fact]
    public void Loaded_resolves_the_choices_of_a_generated_conformance_interface()
    {
        DamlTypeResolver.Loaded.InterfaceChoiceFor(IHolding.InterfaceId, "Split").Should().BeSameAs(IHolding.ChoiceSplit);
        DamlTypeResolver.Loaded.InterfaceChoiceFor(IHolding.InterfaceId, "Describe").Should().BeSameAs(IHolding.ChoiceDescribe);
        DamlTypeResolver.Loaded.InterfaceChoiceFor(IHolding.InterfaceId, "Reissue").Should().BeSameAs(IHolding.ChoiceReissue);
        DamlTypeResolver.Loaded.InterfaceChoiceFor(IHolding.InterfaceId, "Archive").Should().BeSameAs(IHolding.ChoiceArchive);
    }
}
