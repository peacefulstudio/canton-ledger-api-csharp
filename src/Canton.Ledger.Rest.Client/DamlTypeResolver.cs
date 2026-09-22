// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Reflection;
using System.Text.Json;
using Canton.Ledger.Abstractions;
using Daml.Runtime.Commands;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Serialization;
using RuntimeIdentifier = Daml.Runtime.Data.Identifier;

namespace Canton.Ledger.Rest.Client;

internal delegate DamlRecord DamlLfRecordReader(JsonElement json, DamlLfJsonDecodeContext context);

internal sealed record TemplateReaders(Type Template, DamlLfRecordReader CreateArgument, IKeyDescriptor? Key);

/// <summary>
/// Resolves what a Daml-LF JSON payload decodes through — a template's create-argument reader and key
/// descriptor, a template choice's descriptor, an interface's view type and choice descriptors — from the
/// generated code loaded in the process. Readers are captured once, when a type is indexed; decoding never reflects.
/// </summary>
/// <remarks>
/// An identifier resolves to the generated type declaring exactly that package id; failing that, to the one
/// generated type declaring the same module and entity under another package id. More than one candidate
/// refuses with <see cref="TemplateTypeRequiredException"/> rather than picking. A lookup that does not land on
/// exactly one type declaring the identifier's own package id rescans the type source first when the
/// generation has moved since the last scan, so a generated assembly loaded after the first read still
/// resolves, and an exact match loaded later wins over a same-named type from another package; while the
/// generation stands still, no lookup rescans.
/// </remarks>
/// <param name="typeSource">The types to index.</param>
/// <param name="generation">A value that changes whenever <paramref name="typeSource"/> may yield different types.</param>
internal sealed class DamlTypeResolver(Func<IEnumerable<Type>> typeSource, Func<long> generation)
{
    private const BindingFlags PublicStatic = BindingFlags.Public | BindingFlags.Static;

    private static long loadedAssemblyGeneration;

    private readonly Lock rebuild = new();

    private volatile GeneratedTypeIndex? index;

    public static DamlTypeResolver Loaded { get; } = CreateLoaded();

    private static DamlTypeResolver CreateLoaded()
    {
        AppDomain.CurrentDomain.AssemblyLoad += (_, _) => Interlocked.Increment(ref loadedAssemblyGeneration);
        return new DamlTypeResolver(
            () => AppDomain.CurrentDomain.GetAssemblies().SelectMany(LoadableTypes),
            () => Interlocked.Read(ref loadedAssemblyGeneration));
    }

    public TemplateReaders? TemplateFor(RuntimeIdentifier templateId) =>
        Resolve(templateId, current => current.Templates)?.Readers;

    public IChoice? ChoiceFor(RuntimeIdentifier templateId, string choiceName) =>
        Resolve(templateId, current => current.Templates, choiceName)?.Choices.GetValueOrDefault(choiceName);

    public IChoice? InterfaceChoiceFor(RuntimeIdentifier interfaceId, string choiceName) =>
        Resolve(interfaceId, current => current.Interfaces, choiceName)?.Choices.GetValueOrDefault(choiceName);

    public Type? ViewTypeFor(RuntimeIdentifier interfaceId) =>
        Resolve(interfaceId, current => current.Interfaces)?.ViewType;

    public TemplateReaders RequireTemplate(RuntimeIdentifier templateId) =>
        TemplateFor(templateId) ?? throw new TemplateTypeRequiredException(Display(templateId));

    public IChoice RequireChoice(RuntimeIdentifier templateId, string choiceName) =>
        ChoiceFor(templateId, choiceName) ?? throw new TemplateTypeRequiredException(Display(templateId), choiceName);

    public Type RequireViewType(RuntimeIdentifier interfaceId) =>
        ViewTypeFor(interfaceId) ?? throw new TemplateTypeRequiredException(Display(interfaceId));

    public IChoice RequireInterfaceChoice(RuntimeIdentifier interfaceId, string choiceName) =>
        InterfaceChoiceFor(interfaceId, choiceName) ?? throw new TemplateTypeRequiredException(Display(interfaceId), choiceName);

    private TEntry? Resolve<TEntry>(
        RuntimeIdentifier identifier,
        Func<GeneratedTypeIndex, IdentifierTable<TEntry>> table,
        string? choiceName = null)
        where TEntry : class
    {
        var match = table(CurrentIndex()).Find(identifier);

        return match.Conflicting.Count > 0
            ? throw new TemplateTypeRequiredException(
                Display(identifier),
                choiceName,
                match.Conflicting.Select(type => type.FullName ?? type.Name).Order(StringComparer.Ordinal).ToArray())
            : match.Found;
    }

    private GeneratedTypeIndex CurrentIndex()
    {
        var observedGeneration = generation();
        if (index is { } current && current.Generation == observedGeneration)
        {
            return current;
        }

        lock (rebuild)
        {
            if (index is { } rebuiltMeanwhile && rebuiltMeanwhile.Generation == observedGeneration)
            {
                return rebuiltMeanwhile;
            }

            var rebuilt = GeneratedTypeIndex.Build(typeSource(), observedGeneration);
            index = rebuilt;
            return rebuilt;
        }
    }

    private static string Display(RuntimeIdentifier identifier) =>
        $"{identifier.PackageId}:{identifier.ModuleName}:{identifier.EntityName}";

    private static IEnumerable<Type> LoadableTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException partiallyLoaded)
        {
            return partiallyLoaded.Types.OfType<Type>();
        }
    }

    private sealed record TemplateEntry(TemplateReaders Readers, IReadOnlyDictionary<string, IChoice> Choices);

    private sealed record InterfaceEntry(Type ViewType, IReadOnlyDictionary<string, IChoice> Choices);

    private sealed record IdentifierMatch<TEntry>(TEntry? Found, IReadOnlyList<Type> Conflicting, bool SamePackage)
        where TEntry : class
    {
        public static IdentifierMatch<TEntry> Missing { get; } = new(null, [], false);

        public static IdentifierMatch<TEntry> Among(List<(Type Declaring, TEntry Entry)> candidates, bool samePackage) =>
            candidates.Count == 1
                ? new(candidates[0].Entry, [], samePackage)
                : new(null, candidates.Select(candidate => candidate.Declaring).ToArray(), samePackage);
    }

    private sealed class IdentifierTable<TEntry>
        where TEntry : class
    {
        private readonly Dictionary<RuntimeIdentifier, List<(Type Declaring, TEntry Entry)>> byIdentifier = [];
        private readonly Dictionary<(string ModuleName, string EntityName), List<(Type Declaring, TEntry Entry)>> byModuleAndEntity = [];

        public void Add(RuntimeIdentifier identifier, Type declaring, TEntry entry)
        {
            CandidatesAt(byIdentifier, identifier).Add((declaring, entry));
            CandidatesAt(byModuleAndEntity, (identifier.ModuleName, identifier.EntityName)).Add((declaring, entry));
        }

        public IdentifierMatch<TEntry> Find(RuntimeIdentifier identifier) =>
            byIdentifier.TryGetValue(identifier, out var samePackage)
                ? IdentifierMatch<TEntry>.Among(samePackage, samePackage: true)
                : byModuleAndEntity.TryGetValue((identifier.ModuleName, identifier.EntityName), out var sameModuleAndEntity)
                    ? IdentifierMatch<TEntry>.Among(sameModuleAndEntity, samePackage: false)
                    : IdentifierMatch<TEntry>.Missing;

        private static List<(Type Declaring, TEntry Entry)> CandidatesAt<TKey>(
            Dictionary<TKey, List<(Type Declaring, TEntry Entry)>> table, TKey key)
            where TKey : notnull
        {
            if (!table.TryGetValue(key, out var candidates))
            {
                candidates = [];
                table[key] = candidates;
            }

            return candidates;
        }
    }

    private sealed record GeneratedTypeIndex(IdentifierTable<TemplateEntry> Templates, IdentifierTable<InterfaceEntry> Interfaces, long Generation)
    {
        public static GeneratedTypeIndex Build(IEnumerable<Type> types, long generation)
        {
            var templates = new IdentifierTable<TemplateEntry>();
            var interfaces = new IdentifierTable<InterfaceEntry>();

            foreach (var type in types)
            {
                if (type.ContainsGenericParameters) continue;

                if (!type.IsInterface
                    && typeof(ITemplate).IsAssignableFrom(type)
                    && typeof(IDamlRecord<>).MakeGenericType(type).IsAssignableFrom(type)
                    && StaticValue<RuntimeIdentifier>(type, nameof(ITemplate.TemplateId)) is { } templateId)
                {
                    var readers = new TemplateReaders(type, CreateArgumentReaderOf(type), KeyDescriptorOf(type));
                    templates.Add(templateId, type, new TemplateEntry(readers, ChoiceDescriptorsOf(type)));
                }
                else if (typeof(IDamlInterface).IsAssignableFrom(type)
                    && StaticValue<RuntimeIdentifier>(type, nameof(IDamlInterface.InterfaceId)) is { } interfaceId
                    && ClosingArgument(type, typeof(IHasView<>), 0) is { } viewType)
                {
                    interfaces.Add(interfaceId, type, new InterfaceEntry(viewType, ChoiceDescriptorsOf(type)));
                }
            }

            return new GeneratedTypeIndex(templates, interfaces, generation);
        }

        private static Dictionary<string, IChoice> ChoiceDescriptorsOf(Type choiceOwner)
        {
            var choices = new Dictionary<string, IChoice>();
            foreach (var property in choiceOwner.GetProperties(PublicStatic))
            {
                if (typeof(IChoice).IsAssignableFrom(property.PropertyType) && ReflectiveCall.Unwrapped(() => property.GetValue(null)) is IChoice descriptor)
                {
                    choices.TryAdd(descriptor.Name.Value, descriptor);
                }
            }

            return choices;
        }

        private static DamlLfRecordReader CreateArgumentReaderOf(Type template) =>
            (DamlLfRecordReader)ReflectiveCall.Unwrapped(() => CreateArgumentReaderMethod.MakeGenericMethod(template).Invoke(null, null))!;

        private static IKeyDescriptor? KeyDescriptorOf(Type template) =>
            ClosingArgument(template, typeof(IHasKey<,>), 1) is { } keyType
                ? (IKeyDescriptor)ReflectiveCall.Unwrapped(() => KeyDescriptorMethod.MakeGenericMethod(template, keyType).Invoke(null, null))!
                : null;

        private static DamlLfRecordReader CreateArgumentReader<TTemplate>()
            where TTemplate : IDamlRecord<TTemplate> =>
            TTemplate.__ReadDamlLfJson;

        private static IKeyDescriptor KeyDescriptor<TTemplate, TKey>()
            where TTemplate : ITemplate, IHasKey<TTemplate, TKey> =>
            TTemplate.Key;

        private static MethodInfo CreateArgumentReaderMethod { get; } = PrivateStaticMethod(nameof(CreateArgumentReader));

        private static MethodInfo KeyDescriptorMethod { get; } = PrivateStaticMethod(nameof(KeyDescriptor));

        private static MethodInfo PrivateStaticMethod(string name) =>
            typeof(GeneratedTypeIndex).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static)!;

        private static T? StaticValue<T>(Type type, string propertyName)
            where T : class =>
            type.GetProperty(propertyName, PublicStatic) is { GetMethod.IsAbstract: false } property
                ? ReflectiveCall.Unwrapped(() => property.GetValue(null)) as T
                : null;

        private static Type? ClosingArgument(Type type, Type openInterface, int argumentPosition) =>
            type.GetInterfaces()
                .FirstOrDefault(candidate => candidate.IsGenericType && candidate.GetGenericTypeDefinition() == openInterface)
                ?.GetGenericArguments()[argumentPosition];
    }
}
