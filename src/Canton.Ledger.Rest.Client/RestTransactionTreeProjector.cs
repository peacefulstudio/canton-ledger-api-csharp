// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Canton.Ledger.Kernel.Trees;
using Canton.Ledger.Kernel.Wire;
using Daml.Runtime;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using RuntimeIdentifier = Daml.Runtime.Data.Identifier;
using WireCreatedEvent = Canton.Ledger.Rest.Client.Raw.CreatedEvent;
using WireEvent = Canton.Ledger.Rest.Client.Raw.Event;
using WireExercisedEvent = Canton.Ledger.Rest.Client.Raw.ExercisedEvent;
using WireTransaction = Canton.Ledger.Rest.Client.Raw.Transaction;

namespace Canton.Ledger.Rest.Client;

/// <summary>
/// Reads a JSON Ledger API transaction into the nodes <see cref="TreeShape"/> assembles, and returns
/// the assembled <see cref="TransactionTree"/>. The hierarchy a participant reports implicitly —
/// each exercise names the highest node id in the subtree it caused — is rebuilt in
/// <see cref="TreeShape"/>, which the gRPC transport's <c>GrpcTransactionTreeProjector</c> feeds the
/// same way from proto events, so both transports share one assembler and differ only in wire vocabulary.
/// </summary>
internal static class RestTransactionTreeProjector
{
    public static TransactionTree Project(WireTransaction transaction)
    {
        ArgumentNullException.ThrowIfNull(transaction);

        var roots = TreeShape.Assemble((transaction.Events ?? []).Select(ToTreeNode));
        return new TransactionTree(
            transaction.UpdateId,
            LedgerOffset.At(RestWireConversions.ParseOffset(transaction.Offset)),
            roots);
    }

    private static TreeNode ToTreeNode(WireEvent? evt)
    {
        var nodeId = NodeIdOf(evt);
        return new TreeNode(nodeId, () => Decode(evt!, nodeId));
    }

    private static TreeNodeContent Decode(WireEvent evt, int nodeId) =>
        evt.ExercisedEvent is { } exercised
            ? new TreeNodeContent.Subtree(
                exercised.Choice,
                LastDescendantNodeIdOf(exercised, nodeId),
                children => Close(exercised, nodeId, children))
            : new TreeNodeContent.Leaf(ToCreatedNode(evt, nodeId));

    private static int LastDescendantNodeIdOf(WireExercisedEvent exercised, int nodeId) =>
        exercised.LastDescendantNodeId
        ?? throw TreeShape.NotATree(
            $"the exercise of '{exercised.Choice}' at node id {nodeId} states no last descendant node id, "
            + "so the extent of the subtree it caused is unknowable");

    private static TreeEvent Close(WireExercisedEvent exercised, int nodeId, IReadOnlyList<TreeEvent> children)
    {
        var templateId = exercised.TemplateId
            ?? throw MalformedResponse.MissingRequiredField(
                $"ExercisedEvent for contract '{exercised.ContractId}' has no templateId");

        return new TreeEvent.Exercised(
            TreeShape.EventIdOf(nodeId),
            exercised.ContractId,
            RestWireConversions.ToRuntimeIdentifier(templateId),
            exercised.InterfaceId is null ? null : RestWireConversions.ToRuntimeIdentifier(exercised.InterfaceId),
            exercised.Choice,
            exercised.ChoiceArgument is null ? DamlUnit.Instance : RestValueDecoder.ToDamlValue(exercised.ChoiceArgument),
            exercised.ExerciseResult is null ? DamlUnit.Instance : RestValueDecoder.ToDamlValue(exercised.ExerciseResult),
            exercised.Consuming ?? false,
            RestWireConversions.ToPartyList(exercised.ActingParties),
            RestWireConversions.ToPartyList(exercised.WitnessParties),
            children);
    }

    private static TreeEvent ToCreatedNode(WireEvent evt, int nodeId)
    {
        if (evt.CreatedEvent is not { } created)
        {
            throw TreeShape.NotATree(
                $"the event at node id {nodeId} is {DescribeVariant(evt)}, which has no place in a transaction tree; "
                + "trees are read from ledger-effects transactions, whose events are creates and exercises only");
        }

        var templateId = created.TemplateId
            ?? throw MalformedResponse.MissingRequiredField(
                $"CreatedEvent for contract '{created.ContractId}' has no templateId");
        var createArgument = created.CreateArgument
            ?? throw MalformedResponse.MissingRequiredField(
                $"CreatedEvent for contract '{created.ContractId}' has no createArgument");
        var runtimeTemplateId = RestWireConversions.ToRuntimeIdentifier(templateId);

        return new TreeEvent.Created(
            TreeShape.EventIdOf(nodeId),
            created.ContractId,
            runtimeTemplateId,
            RestValueDecoder.ToDamlRecord(createArgument),
            RestWireConversions.ToPartyList(created.WitnessParties),
            RestWireConversions.ToPartyList(created.Signatories),
            RestWireConversions.ToPartyList(created.Observers),
            RestWireConversions.ContractKeyOf(created, runtimeTemplateId),
            created.CreatedAt)
        {
            InterfaceIds = ToInterfaceIds(created),
        };
    }

    private static IReadOnlyList<RuntimeIdentifier> ToInterfaceIds(WireCreatedEvent created)
    {
        if (created.InterfaceViews is not { Count: > 0 } views)
        {
            return [];
        }

        var interfaceIds = new List<RuntimeIdentifier>(views.Count);
        foreach (var view in views)
        {
            var interfaceId = view?.InterfaceId
                ?? throw MalformedResponse.MissingRequiredField(
                    $"an interface view on CreatedEvent for contract '{created.ContractId}' has no interfaceId");
            interfaceIds.Add(RestWireConversions.ToRuntimeIdentifier(interfaceId));
        }
        return interfaceIds;
    }

    private static int NodeIdOf(WireEvent? evt) =>
        evt?.CreatedEvent?.NodeId
        ?? evt?.ExercisedEvent?.NodeId
        ?? evt?.ArchivedEvent?.NodeId
        ?? throw TreeShape.NotATree($"{DescribeVariant(evt)} carries no node id, so its place in the tree is unknowable");

    private static string DescribeVariant(WireEvent? evt) => evt switch
    {
        { CreatedEvent: not null } => "a CreatedEvent",
        { ExercisedEvent: not null } => "an ExercisedEvent",
        { ArchivedEvent: not null } => "an ArchivedEvent",
        _ => "an event of no recognised kind",
    };
}
