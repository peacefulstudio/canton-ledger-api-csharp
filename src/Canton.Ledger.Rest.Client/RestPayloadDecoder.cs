// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Canton.Ledger.Abstractions;
using Canton.Ledger.Kernel.Wire;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using RuntimeIdentifier = Daml.Runtime.Data.Identifier;
using WireCreatedEvent = Canton.Ledger.Rest.Client.Raw.CreatedEvent;
using WireExercisedEvent = Canton.Ledger.Rest.Client.Raw.ExercisedEvent;
using WireRecord = Canton.Ledger.Rest.Client.Raw.Record;

namespace Canton.Ledger.Rest.Client;

internal sealed record ExercisePayloads(DamlValue Argument, DamlValue Result);

/// <summary>
/// Decodes the Daml payloads of a JSON Ledger API event — a create argument, a contract key, a choice
/// argument and exercise result — against the generated types <see cref="DamlTypeResolver.Loaded"/>
/// resolves for the event's template or interface. A payload with no single loaded generated type throws
/// <see cref="TemplateTypeRequiredException"/>; nothing is inferred from the JSON alone.
/// </summary>
internal static class RestPayloadDecoder
{
    public static DamlRecord CreateArgumentOf(WireCreatedEvent created, RuntimeIdentifier templateId)
    {
        var template = DamlTypeResolver.Loaded.RequireTemplate(templateId);
        return RestValueDecoder.ToDamlRecord(
            RequireCreateArgument(created), template.CreateArgument, template.Template.Name);
    }

    public static WireRecord RequireCreateArgument(WireCreatedEvent created) =>
        created.CreateArgument
        ?? throw MalformedResponse.MissingRequiredField(
            $"CreatedEvent for contract '{created.ContractId}' has no createArgument");

    public static ContractKey? ContractKeyOf(WireCreatedEvent created, RuntimeIdentifier templateId)
    {
        if (created.ContractKey is null || RestValueDecoder.IsJsonNull(created.ContractKey))
        {
            return null;
        }

        var template = DamlTypeResolver.Loaded.RequireTemplate(templateId);
        var keyDescriptor = template.Key
            ?? throw TemplateTypeRequiredException.ForKeylessTemplate(
                $"{templateId.PackageId}:{templateId.ModuleName}:{templateId.EntityName}",
                template.Template.FullName ?? template.Template.Name);

        var keyValue = RestValueDecoder.ToDamlValue(
            created.ContractKey, keyDescriptor.ReadKeyJson, $"{template.Template.Name}.key");
        return new ContractKey(keyValue, templateId)
        {
            KeyHash = RestWireConversions.ToKeyHash(created.ContractKeyHash),
        };
    }

    public static ExercisePayloads ExercisePayloadsOf(WireExercisedEvent exercised, RuntimeIdentifier templateId)
    {
        var choiceName = exercised.Choice
            ?? throw MalformedResponse.MissingRequiredField(
                $"ExercisedEvent for contract '{exercised.ContractId}' has no choice");
        var choiceArgument = exercised.ChoiceArgument
            ?? throw MalformedResponse.MissingRequiredField(
                $"ExercisedEvent for contract '{exercised.ContractId}' has no choiceArgument");
        var exerciseResult = exercised.ExerciseResult
            ?? throw MalformedResponse.MissingRequiredField(
                $"ExercisedEvent for contract '{exercised.ContractId}' has no exerciseResult");

        var choice = exercised.InterfaceId is { } interfaceId
            ? DamlTypeResolver.Loaded.RequireInterfaceChoice(RestWireConversions.ToRuntimeIdentifier(interfaceId), choiceName)
            : DamlTypeResolver.Loaded.RequireChoice(templateId, choiceName);

        return new ExercisePayloads(
            RestValueDecoder.ToDamlValue(choiceArgument, choice.ReadArgumentJson, $"{choiceName}.argument"),
            RestValueDecoder.ToDamlValue(exerciseResult, choice.ReadResultJson, $"{choiceName}.result"));
    }
}
