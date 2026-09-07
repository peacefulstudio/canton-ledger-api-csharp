// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Canton.Ledger.Kernel.Trees;
using Canton.Ledger.Kernel.Wire;
using Com.Daml.Ledger.Api.V2;
using Daml.Runtime;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using ProtoCreatedEvent = Com.Daml.Ledger.Api.V2.CreatedEvent;
using RuntimeIdentifier = Daml.Runtime.Data.Identifier;
using ProtoExercisedEvent = Com.Daml.Ledger.Api.V2.ExercisedEvent;

namespace Canton.Ledger.Grpc.Client;

internal static class GrpcTransactionTreeProjector
{
    public static TransactionTree Project(SubmitAndWaitForTransactionResponse response)
    {
        var transaction = response.Transaction
            ?? throw new InvalidOperationException(
                "Server returned a successful response but no Transaction was present.");
        return Project(transaction);
    }

    public static TransactionTree Project(Transaction transaction)
    {
        var roots = TreeShape.Assemble(transaction.Events.Select(ToTreeNode));
        return new TransactionTree(
            transaction.UpdateId,
            LedgerWireConversions.ToLedgerOffset(transaction.Offset),
            roots);
    }

    private static TreeNode ToTreeNode(Event evt)
    {
        var nodeId = NodeIdOf(evt);
        return new TreeNode(nodeId, () => Decode(evt, nodeId));
    }

    private static TreeNodeContent Decode(Event evt, int nodeId) =>
        evt.EventCase == Event.EventOneofCase.Exercised
            ? new TreeNodeContent.Subtree(
                evt.Exercised.Choice,
                evt.Exercised.LastDescendantNodeId,
                children => Close(evt.Exercised, nodeId, children))
            : new TreeNodeContent.Leaf(ToCreatedNode(evt, nodeId));

    private static TreeEvent Close(ProtoExercisedEvent exercised, int nodeId, IReadOnlyList<TreeEvent> children)
    {
        var templateId = exercised.TemplateId
            ?? throw MalformedResponse.MissingRequiredField(
                $"ExercisedEvent for contract '{exercised.ContractId}' has no template_id");

        return new TreeEvent.Exercised(
            TreeShape.EventIdOf(nodeId),
            exercised.ContractId,
            LedgerWireConversions.ToRuntimeIdentifier(templateId),
            exercised.InterfaceId is null ? null : LedgerWireConversions.ToRuntimeIdentifier(exercised.InterfaceId),
            exercised.Choice,
            exercised.ChoiceArgument is null ? DamlUnit.Instance : GrpcValueDecoder.ToDamlValue(exercised.ChoiceArgument),
            exercised.ExerciseResult is null ? DamlUnit.Instance : GrpcValueDecoder.ToDamlValue(exercised.ExerciseResult),
            exercised.Consuming,
            LedgerWireConversions.ToPartyList(exercised.ActingParties),
            LedgerWireConversions.ToPartyList(exercised.WitnessParties),
            children);
    }

    private static TreeEvent ToCreatedNode(Event evt, int nodeId)
    {
        if (evt.EventCase != Event.EventOneofCase.Created)
        {
            throw TreeShape.NotATree(
                $"the event at node id {nodeId} is a {evt.EventCase} event, which has no place in a transaction tree; "
                + "trees are read from ledger-effects transactions, whose events are creates and exercises only");
        }

        var created = evt.Created;
        var templateId = created.TemplateId
            ?? throw MalformedResponse.MissingRequiredField(
                $"CreatedEvent for contract '{created.ContractId}' has no template_id");
        var createArguments = created.CreateArguments
            ?? throw MalformedResponse.MissingRequiredField(
                $"CreatedEvent for contract '{created.ContractId}' has no create_arguments");
        var runtimeTemplateId = LedgerWireConversions.ToRuntimeIdentifier(templateId);

        return new TreeEvent.Created(
            TreeShape.EventIdOf(nodeId),
            created.ContractId,
            runtimeTemplateId,
            GrpcValueDecoder.ToDamlRecord(createArguments),
            LedgerWireConversions.ToPartyList(created.WitnessParties),
            LedgerWireConversions.ToPartyList(created.Signatories),
            LedgerWireConversions.ToPartyList(created.Observers),
            LedgerWireConversions.ContractKeyOf(created, runtimeTemplateId),
            GrpcValueDecoder.ToCreatedAt(created))
        {
            InterfaceIds = ToInterfaceIds(created),
        };
    }

    private static IReadOnlyList<RuntimeIdentifier> ToInterfaceIds(ProtoCreatedEvent created)
    {
        if (created.InterfaceViews.Count == 0)
        {
            return [];
        }

        var interfaceIds = new List<RuntimeIdentifier>(created.InterfaceViews.Count);
        foreach (var view in created.InterfaceViews)
        {
            var interfaceId = view.InterfaceId
                ?? throw MalformedResponse.MissingRequiredField(
                    $"an interface view on CreatedEvent for contract '{created.ContractId}' has no interface_id");
            interfaceIds.Add(LedgerWireConversions.ToRuntimeIdentifier(interfaceId));
        }
        return interfaceIds;
    }

    private static int NodeIdOf(Event evt) => evt.EventCase switch
    {
        Event.EventOneofCase.Created => evt.Created.NodeId,
        Event.EventOneofCase.Archived => evt.Archived.NodeId,
        Event.EventOneofCase.Exercised => evt.Exercised.NodeId,
        _ => throw TreeShape.NotATree($"a {evt.EventCase} event carries no node id, so its place in the tree is unknowable"),
    };
}
