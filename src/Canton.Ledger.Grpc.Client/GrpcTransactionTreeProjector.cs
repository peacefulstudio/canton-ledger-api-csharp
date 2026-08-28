// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using Canton.Ledger.Abstractions;
using Com.Daml.Ledger.Api.V2;
using Daml.Runtime;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Grpc;
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
        var roots = new List<TreeEvent>();
        var openExercises = new Stack<OpenExercise>();
        var previousNodeId = -1;

        foreach (var evt in transaction.Events)
        {
            var nodeId = NodeIdOf(evt);
            if (nodeId <= previousNodeId)
            {
                throw NotATree(
                    $"node id {nodeId} follows node id {previousNodeId}, but node ids must strictly ascend");
            }
            previousNodeId = nodeId;

            while (openExercises.Count > 0 && nodeId > openExercises.Peek().LastDescendantNodeId)
            {
                Emit(Close(openExercises.Pop()), openExercises, roots);
            }

            if (evt.EventCase == Event.EventOneofCase.Exercised)
            {
                openExercises.Push(OpenSubtree(evt.Exercised, nodeId, openExercises));
            }
            else
            {
                Emit(ToCreatedNode(evt, nodeId), openExercises, roots);
            }
        }

        while (openExercises.Count > 0)
        {
            Emit(Close(openExercises.Pop()), openExercises, roots);
        }

        return new TransactionTree(
            transaction.UpdateId,
            LedgerOffset.At(transaction.Offset),
            roots);
    }

    private static OpenExercise OpenSubtree(ProtoExercisedEvent exercised, int nodeId, Stack<OpenExercise> openExercises)
    {
        var lastDescendantNodeId = exercised.LastDescendantNodeId;
        if (lastDescendantNodeId < nodeId)
        {
            throw NotATree(
                $"the exercise of '{exercised.Choice}' at node id {nodeId} claims a last descendant node id of "
                + $"{lastDescendantNodeId}, which precedes the exercise itself");
        }

        if (openExercises.Count > 0 && lastDescendantNodeId > openExercises.Peek().LastDescendantNodeId)
        {
            var enclosing = openExercises.Peek();
            throw NotATree(
                $"the subtree of '{exercised.Choice}' at node id {nodeId} ends at node id {lastDescendantNodeId}, "
                + $"past the end ({enclosing.LastDescendantNodeId}) of the enclosing subtree rooted at node id "
                + $"{enclosing.NodeId}, so the two overlap instead of nesting");
        }

        return new OpenExercise(exercised, nodeId, lastDescendantNodeId);
    }

    private static void Emit(TreeEvent node, Stack<OpenExercise> openExercises, List<TreeEvent> roots)
    {
        if (openExercises.Count > 0)
        {
            openExercises.Peek().Children.Add(node);
        }
        else
        {
            roots.Add(node);
        }
    }

    private static TreeEvent Close(OpenExercise open)
    {
        var exercised = open.Wire;
        var templateId = exercised.TemplateId
            ?? throw GrpcTransactionResultProjector.MalformedResponse(
                $"ExercisedEvent for contract '{exercised.ContractId}' has no template_id");

        return new TreeEvent.Exercised(
            EventIdOf(open.NodeId),
            exercised.ContractId,
            LedgerWireConversions.ToRuntimeIdentifier(templateId),
            exercised.InterfaceId is null ? null : LedgerWireConversions.ToRuntimeIdentifier(exercised.InterfaceId),
            exercised.Choice,
            exercised.ChoiceArgument is null ? DamlUnit.Instance : DamlValueConverter.FromProtoValue(exercised.ChoiceArgument),
            exercised.ExerciseResult is null ? DamlUnit.Instance : DamlValueConverter.FromProtoValue(exercised.ExerciseResult),
            exercised.Consuming,
            LedgerWireConversions.ToPartyList(exercised.ActingParties),
            LedgerWireConversions.ToPartyList(exercised.WitnessParties),
            open.Children);
    }

    private static TreeEvent ToCreatedNode(Event evt, int nodeId)
    {
        if (evt.EventCase != Event.EventOneofCase.Created)
        {
            throw NotATree(
                $"the event at node id {nodeId} is a {evt.EventCase} event, which has no place in a transaction tree; "
                + "trees are read from ledger-effects transactions, whose events are creates and exercises only");
        }

        var created = evt.Created;
        var templateId = created.TemplateId
            ?? throw GrpcTransactionResultProjector.MalformedResponse(
                $"CreatedEvent for contract '{created.ContractId}' has no template_id");
        var createArguments = created.CreateArguments
            ?? throw GrpcTransactionResultProjector.MalformedResponse(
                $"CreatedEvent for contract '{created.ContractId}' has no create_arguments");
        var runtimeTemplateId = LedgerWireConversions.ToRuntimeIdentifier(templateId);

        return new TreeEvent.Created(
            EventIdOf(nodeId),
            created.ContractId,
            runtimeTemplateId,
            DamlValueConverter.FromProtoRecord(createArguments),
            LedgerWireConversions.ToPartyList(created.WitnessParties),
            LedgerWireConversions.ToPartyList(created.Signatories),
            LedgerWireConversions.ToPartyList(created.Observers),
            created.ContractKey is null
                ? null
                : new ContractKey(DamlValueConverter.FromProtoValue(created.ContractKey), runtimeTemplateId),
            created.CreatedAt?.ToDateTimeOffset())
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
                ?? throw GrpcTransactionResultProjector.MalformedResponse(
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
        _ => throw NotATree($"a {evt.EventCase} event carries no node id, so its place in the tree is unknowable"),
    };

    private static string EventIdOf(int nodeId) => nodeId.ToString(CultureInfo.InvariantCulture);

    private static MalformedTransactionTreeException NotATree(string detail) =>
        new($"Cannot reconstruct the transaction tree: {detail}.");

    private sealed class OpenExercise(ProtoExercisedEvent wire, int nodeId, int lastDescendantNodeId)
    {
        public ProtoExercisedEvent Wire { get; } = wire;

        public int NodeId { get; } = nodeId;

        public int LastDescendantNodeId { get; } = lastDescendantNodeId;

        public List<TreeEvent> Children { get; } = [];
    }
}
