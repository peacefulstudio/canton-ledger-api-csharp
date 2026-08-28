// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using System.Text.Json.Serialization;
using Canton.Ledger.Rest.Client.Raw;

namespace Canton.Ledger.Rest.Client;

/// <summary>
/// Binds <see cref="FeaturesDescriptor.UserManagement"/> from either the proto snake_case field name
/// the generated POCO declares (<c>user_management</c>) or the camelCase key the JSON Ledger API
/// actually sends on the wire (<c>userManagement</c>).
/// </summary>
/// <remarks>
/// Adaptation delta: the <see cref="JsonPropertyNameAttribute"/> metadata on the generated
/// <see cref="FeaturesDescriptor"/> POCO only declares the snake_case proto3 JSON name
/// <c>user_management</c>, so without this converter the camelCase <c>userManagement</c> key that
/// <c>GET /v2/version</c> answers with lands in <see cref="FeaturesDescriptor.AdditionalProperties"/>
/// and the typed property deserializes to <c>null</c>. Scoped to the multi-word <c>userManagement</c>
/// delta deliberately — the descriptor's other snake_case fields keep their generated wire names.
/// </remarks>
internal sealed class FeaturesDescriptorJsonConverter : JsonConverter<FeaturesDescriptor>
{
    private const string ExperimentalName = "experimental";
    private const string UserManagementSnakeCase = "user_management";
    private const string UserManagementCamelCase = "userManagement";
    private const string PartyManagementName = "party_management";
    private const string OffsetCheckpointName = "offset_checkpoint";
    private const string PackageFeatureName = "package_feature";

    /// <inheritdoc />
    public override FeaturesDescriptor Read(
        ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        try
        {
            using var document = JsonDocument.ParseValue(ref reader);
            var root = document.RootElement;
            var features = new FeaturesDescriptor();

            if (root.TryGetProperty(ExperimentalName, out var experimental))
                features.Experimental = experimental.ValueKind == JsonValueKind.Null
                    ? null!
                    : experimental.Deserialize<ExperimentalFeatures>(options)!;

            if (WireCaseJson.TryGetEitherCase(root, UserManagementSnakeCase, UserManagementCamelCase, out var userManagement))
                features.UserManagement = userManagement.ValueKind == JsonValueKind.Null
                    ? null!
                    : userManagement.Deserialize<UserManagementFeature>(options)!;

            if (root.TryGetProperty(PartyManagementName, out var partyManagement))
                features.PartyManagement = partyManagement.ValueKind == JsonValueKind.Null
                    ? null!
                    : partyManagement.Deserialize<PartyManagementFeature>(options)!;

            if (root.TryGetProperty(OffsetCheckpointName, out var offsetCheckpoint))
                features.OffsetCheckpoint = offsetCheckpoint.ValueKind == JsonValueKind.Null
                    ? null!
                    : offsetCheckpoint.Deserialize<OffsetCheckpointFeature>(options)!;

            if (root.TryGetProperty(PackageFeatureName, out var packageFeature))
                features.PackageFeature = packageFeature.ValueKind == JsonValueKind.Null
                    ? null!
                    : packageFeature.Deserialize<PackageFeature>(options)!;

            foreach (var property in root.EnumerateObject())
            {
                if (IsKnownPropertyName(property.Name)) continue;
                features.AdditionalProperties[property.Name] = property.Value.Clone();
            }

            return features;
        }
        catch (Exception ex) when (ex is not JsonException)
        {
            throw new JsonException(
                "Failed to deserialize FeaturesDescriptor from the JSON Ledger API response.", ex);
        }
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, FeaturesDescriptor value, JsonSerializerOptions options)
        => throw new NotSupportedException(
            $"{nameof(FeaturesDescriptorJsonConverter)} is read-only; " +
            $"{nameof(FeaturesDescriptor)} is never serialized as a request body.");

    private static bool IsKnownPropertyName(string name) =>
        name is ExperimentalName or UserManagementSnakeCase or UserManagementCamelCase
            or PartyManagementName or OffsetCheckpointName or PackageFeatureName;
}
