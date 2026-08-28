// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Frozen;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Canton.Ledger.Rest.Client;

/// <summary>
/// A generated <c>oneof</c> wrapper registered with <see cref="WrappedOneOfJsonConverterFactory"/>,
/// together with the wire names of the arms Canton encodes <em>without</em> the <c>value</c> level.
/// Every arm not named here is <c>value</c>-wrapped.
/// <para>
/// An arm whose bare payload can arrive as an object carrying a single property named <c>value</c>
/// must not be named here: the reader cannot tell that payload from the wrapper it rejects, and will
/// reject it.
/// </para>
/// </summary>
internal sealed record WrappedOneOf(Type WrapperType, params string[] BareArms);

/// <summary>
/// Selects <see cref="WrappedOneOfJsonConverter{T}"/> for the generated wrapper types it is
/// constructed with, and for no others.
/// </summary>
internal sealed class WrappedOneOfJsonConverterFactory : JsonConverterFactory
{
    private readonly FrozenDictionary<Type, FrozenSet<string>> _bareArmsByWrapperType;

    public WrappedOneOfJsonConverterFactory(params WrappedOneOf[] wrappers) =>
        _bareArmsByWrapperType = wrappers.ToFrozenDictionary(
            wrapper => wrapper.WrapperType,
            wrapper => wrapper.BareArms.ToFrozenSet(StringComparer.Ordinal));

    /// <inheritdoc />
    public override bool CanConvert(Type typeToConvert) => _bareArmsByWrapperType.ContainsKey(typeToConvert);

    /// <inheritdoc />
    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options) =>
        (JsonConverter)Activator.CreateInstance(
            typeof(WrappedOneOfJsonConverter<>).MakeGenericType(typeToConvert),
            _bareArmsByWrapperType[typeToConvert])!;
}

/// <summary>
/// Adapts a generated <c>oneof</c> wrapper to the Canton JSON Ledger API's encoding, where each arm
/// is a single PascalCase key whose payload sits under a further <c>value</c> object. Writing emits
/// exactly one arm and refuses both none and more than one; reading accepts one arm and carries any
/// key the generated type does not declare — notably the <c>Empty</c> arm gnostic drops — into the
/// extension bag rather than rejecting it. A key the type <em>does</em> declare is rejected when it
/// arrives without its <c>value</c> level, because carrying that one into the extension bag instead
/// would leave the declared arm null and indistinguishable from an arm the server never sent.
/// <para>
/// Arms Canton encodes bare are named per wrapper type on <see cref="WrappedOneOf"/> and skip the
/// <c>value</c> level in both directions, so a type mixing wrapped and bare arms is registrable.
/// <c>ReassignmentEvent</c> is the motivating case: the server hand-models its assigned arm bare and
/// passes the unassigned one through wrapped. Bareness is declared, never sniffed, and the
/// declaration is enforced in both directions: an arm not named bare that arrives unwrapped fails,
/// and an arm named bare that arrives wrapped fails too, rather than being read as a payload whose
/// every field is silently null.
/// </para>
/// </summary>
/// <remarks>
/// Retired by digital-asset/canton#527, which would have the JSON HTTP API generated from the
/// annotated protobuf definitions these wrappers come from. The <c>value</c> level is an artefact
/// of the Scala case classes the current server derives its codecs from.
/// </remarks>
internal sealed class WrappedOneOfJsonConverter<T>(FrozenSet<string> bareArms) : JsonConverter<T>
    where T : new()
{
    private const string WrappedValueProperty = "value";

    private static readonly PropertyInfo[] Arms = typeof(T)
        .GetProperties(BindingFlags.Public | BindingFlags.Instance)
        .Where(property => property.GetCustomAttribute<JsonExtensionDataAttribute>() is null)
        .Where(property => property.GetCustomAttribute<JsonPropertyNameAttribute>() is not null)
        .ToArray();

    private static readonly PropertyInfo? ExtensionBag = typeof(T)
        .GetProperties(BindingFlags.Public | BindingFlags.Instance)
        .SingleOrDefault(property => property.GetCustomAttribute<JsonExtensionDataAttribute>() is not null);

    private static string WireNameOf(PropertyInfo arm) =>
        arm.GetCustomAttribute<JsonPropertyNameAttribute>()!.Name;

    /// <inheritdoc />
    public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        try
        {
            using var document = JsonDocument.ParseValue(ref reader);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                throw Malformed($"a JSON {root.ValueKind} value");

            var wrapper = new T();
            foreach (var arm in root.EnumerateObject())
            {
                var matched = Array.Find(Arms, candidate => WireNameOf(candidate) == arm.Name);
                if (matched is null)
                {
                    Stash(wrapper, arm.Name, arm.Value.Clone());
                    continue;
                }

                matched.SetValue(wrapper, PayloadOf(arm).Deserialize(matched.PropertyType, options));
            }

            return wrapper;
        }
        catch (Exception failure) when (failure is not JsonException)
        {
            throw Malformed("a value that could not be read", failure);
        }
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
    {
        if (value is null)
            throw new JsonException($"A {typeof(T).Name} reached the wire as null and cannot be encoded.");

        var set = Arms.Where(arm => arm.GetValue(value) is not null).ToArray();
        var carried = Carried(value).ToArray();

        var names = set.Select(WireNameOf).Concat(carried.Select(entry => entry.Key)).ToArray();
        if (names.Length > 1)
            throw new JsonException(
                $"A {typeof(T).Name} must carry exactly one arm, but {string.Join(", ", names)} were all set.");

        if (names.Length == 0)
            throw new JsonException(
                $"A {typeof(T).Name} must carry exactly one arm, but none was set. "
                + "The server tolerates the empty object this would produce and silently falls back to its default.");

        writer.WriteStartObject();
        foreach (var arm in set)
        {
            var wireName = WireNameOf(arm);
            writer.WritePropertyName(wireName);
            WritePayload(writer, arm.GetValue(value), arm.PropertyType, bareArms.Contains(wireName), options);
        }

        foreach (var entry in carried)
        {
            writer.WritePropertyName(entry.Key);
            entry.Value.WriteTo(writer);
        }

        writer.WriteEndObject();
    }

    private static void WritePayload(
        Utf8JsonWriter writer, object? payload, Type declaredType, bool bare, JsonSerializerOptions options)
    {
        if (bare)
        {
            JsonSerializer.Serialize(writer, payload, declaredType, options);
            return;
        }

        writer.WriteStartObject();
        writer.WritePropertyName(WrappedValueProperty);
        JsonSerializer.Serialize(writer, payload, declaredType, options);
        writer.WriteEndObject();
    }

    private JsonElement PayloadOf(JsonProperty arm) =>
        bareArms.Contains(arm.Name) ? Bare(arm) : Unwrap(arm);

    private static JsonElement Bare(JsonProperty arm) =>
        LooksWrapped(arm.Value)
            ? throw new JsonException(
                $"A {typeof(T).Name} received the arm '{arm.Name}' inside the '{WrappedValueProperty}' level "
                + $"it is declared bare of. Reading the wrapper as the payload would leave every field of "
                + $"{arm.Name} unset and indistinguishable from a payload the server sent empty.")
            : arm.Value;

    private static bool LooksWrapped(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Object)
            return false;

        var properties = value.EnumerateObject();
        return properties.MoveNext()
            && properties.Current.NameEquals(WrappedValueProperty)
            && !properties.MoveNext();
    }

    private static JsonElement Unwrap(JsonProperty arm) =>
        arm.Value.ValueKind == JsonValueKind.Object
        && arm.Value.TryGetProperty(WrappedValueProperty, out var payload)
            ? payload
            : throw new JsonException(
                $"A {typeof(T).Name} received the arm '{arm.Name}' without the '{WrappedValueProperty}' level "
                + $"it is declared wrapped in. Carrying it would leave {arm.Name} unset and "
                + "indistinguishable from an arm the server never sent.");

    private static void Stash(T wrapper, string name, JsonElement element)
    {
        if (ExtensionBag is null)
            throw new JsonException(
                $"A {typeof(T).Name} received the unrecognised arm '{name}', which it can neither represent nor carry.");

        if (ExtensionBag.GetValue(wrapper) is not IDictionary<string, object> bag)
        {
            bag = new Dictionary<string, object>();
            ExtensionBag.SetValue(wrapper, bag);
        }

        bag[name] = element;
    }

    private static IEnumerable<KeyValuePair<string, JsonElement>> Carried(T wrapper)
    {
        if (ExtensionBag?.GetValue(wrapper) is not IDictionary<string, object> bag)
            return [];

        return bag
            .Where(entry => entry.Value is JsonElement)
            .Select(entry => KeyValuePair.Create(entry.Key, (JsonElement)entry.Value));
    }

    private static JsonException Malformed(string detail, Exception? cause = null) =>
        new($"Expected a {typeof(T).Name} as a single-key object with a wrapped value, but found {detail}.", cause);
}
