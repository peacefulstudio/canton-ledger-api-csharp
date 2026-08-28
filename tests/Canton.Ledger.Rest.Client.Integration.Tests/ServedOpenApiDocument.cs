// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

namespace Canton.Ledger.Rest.Client.Integration.Tests;

/// <summary>
/// The OpenAPI document a Canton participant serves for its own JSON Ledger API. It is the
/// authoritative description of what that participant puts on the wire, and it is a different
/// artifact from the vendored <c>src/Canton.Ledger.Rest/spec/openapi.yaml</c> this client is
/// generated from: ours is derived from the Ledger API protobuf definitions, the served one is
/// emitted by the participant's own tapir endpoint definitions. Where the two disagree, the served
/// document is what a client actually has to decode.
/// </summary>
internal sealed partial class ServedOpenApiDocument
{
    private readonly YamlBlock _schemas;

    private ServedOpenApiDocument(string cantonVersion, YamlBlock schemas)
    {
        CantonVersion = cantonVersion;
        _schemas = schemas;
    }

    internal string CantonVersion { get; }

    internal static ServedOpenApiDocument Parse(string yaml)
    {
        var document = YamlBlock.Root(yaml);
        return new ServedOpenApiDocument(
            document.ValueOf("info").ScalarOf("version"),
            document.ValueOf("components").ValueOf("schemas"));
    }

    internal IReadOnlyList<string> OneOfArmKeysOf(string schemaName) =>
        _schemas.ValueOf(schemaName).ValueOf("oneOf").BlockSequence()
            .SelectMany(arm => arm.ValueOf("required").ScalarSequence())
            .ToList();

    internal IReadOnlyList<string> RequiredPropertiesOf(string schemaName) =>
        _schemas.ValueOf(schemaName).ValueOf("required").ScalarSequence();

    internal string ReferenceTargetOf(string schemaName, string propertyName) =>
        _schemas.ValueOf(schemaName).ValueOf("properties").ValueOf(propertyName).ScalarOf("$ref");
}
