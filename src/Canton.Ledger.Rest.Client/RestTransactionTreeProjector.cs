// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using Canton.Ledger.Abstractions;
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
/// Rebuilds the parent/child hierarchy a participant reports implicitly on a ledger-effects
/// transaction — each exercise names the highest node id in the subtree it caused — into a
/// <see cref="TransactionTree"/>. Mirrors the gRPC transport's <c>GrpcTransactionTreeProjector</c>.
/// </summary>
internal static class RestTransactionTreeProjector
{
    public static TransactionTree Project(WireTransaction transaction)
    {
        ArgumentNullException.ThrowIfNull(transaction);

        var roots = new List<TreeEvent>();
        var openExercises = new Stack<OpenExercise>();
        var previousNodeId = -1;

        foreach (var evt in transaction.Events ?? [])
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

            if (evt!.ExercisedEvent is { } exercised)
            {
                openExercises.Push(OpenSubtree(exercised, nodeId, openExercises));
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
            LedgerOffset.At(RestWireConversions.ParseOffset(transaction.Offset)),
            roots);
    }

    private static OpenExercise OpenSubtree(WireExercisedEvent exercised, int nodeId, Stack<OpenExercise> openExercises)
    {
        var lastDescendantNodeId = exercised.LastDescendantNodeId
            ?? throw NotATree(
                $"the exercise of '{exercised.Choice}' at node id {nodeId} states no last descendant node id, "
                + "so the extent of the subtree it caused is unknowable");

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
            ?? throw RestTransactionResultProjector.MalformedResponse(
                $"ExercisedEvent for contract '{exercised.ContractId}' has no templateId");

        return new TreeEvent.Exercised(
            EventIdOf(open.NodeId),
            exercised.ContractId,
            RestWireConversions.ToRuntimeIdentifier(templateId),
            exercised.InterfaceId is null ? null : RestWireConversions.ToRuntimeIdentifier(exercised.InterfaceId),
            exercised.Choice,
            exercised.ChoiceArgument is null ? DamlUnit.Instance : RestValueDecoder.ToDamlValue(exercised.ChoiceArgument),
            exercised.ExerciseResult is null ? DamlUnit.Instance : RestValueDecoder.ToDamlValue(exercised.ExerciseResult),
            exercised.Consuming ?? false,
            RestWireConversions.ToPartyList(exercised.ActingParties),
            RestWireConversions.ToPartyList(exercised.WitnessParties),
            open.Children);
    }

    private static TreeEvent ToCreatedNode(WireEvent evt, int nodeId)
    {
        if (evt.CreatedEvent is not { } created)
        {
            throw NotATree(
                $"the event at node id {nodeId} is {DescribeVariant(evt)}, which has no place in a transaction tree; "
                + "trees are read from ledger-effects transactions, whose events are creates and exercises only");
        }

        var templateId = created.TemplateId
            ?? throw RestTransactionResultProjector.MalformedResponse(
                $"CreatedEvent for contract '{created.ContractId}' has no templateId");
        var createArgument = created.CreateArgument
            ?? throw RestTransactionResultProjector.MalformedResponse(
                $"CreatedEvent for contract '{created.ContractId}' has no createArgument");
        var runtimeTemplateId = RestWireConversions.ToRuntimeIdentifier(templateId);

        return new TreeEvent.Created(
            EventIdOf(nodeId),
            created.ContractId,
            runtimeTemplateId,
            RestValueDecoder.ToDamlRecord(createArgument),
            RestWireConversions.ToPartyList(created.WitnessParties),
            RestWireConversions.ToPartyList(created.Signatories),
            RestWireConversions.ToPartyList(created.Observers),
            created.ContractKey is null
                ? null
                : new ContractKey(RestValueDecoder.ToDamlValue(created.ContractKey), runtimeTemplateId),
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
                ?? throw RestTransactionResultProjector.MalformedResponse(
                    $"an interface view on CreatedEvent for contract '{created.ContractId}' has no interfaceId");
            interfaceIds.Add(RestWireConversions.ToRuntimeIdentifier(interfaceId));
        }
        return interfaceIds;
    }

    private static int NodeIdOf(WireEvent? evt) =>
        evt?.CreatedEvent?.NodeId
        ?? evt?.ExercisedEvent?.NodeId
        ?? evt?.ArchivedEvent?.NodeId
        ?? throw NotATree($"{DescribeVariant(evt)} carries no node id, so its place in the tree is unknowable");

    private static string DescribeVariant(WireEvent? evt) => evt switch
    {
        { CreatedEvent: not null } => "a CreatedEvent",
        { ExercisedEvent: not null } => "an ExercisedEvent",
        { ArchivedEvent: not null } => "an ArchivedEvent",
        _ => "an event of no recognised kind",
    };

    private static string EventIdOf(int nodeId) => nodeId.ToString(CultureInfo.InvariantCulture);

    private static MalformedTransactionTreeException NotATree(string detail) =>
        new($"Cannot reconstruct the transaction tree: {detail}.");

    private sealed class OpenExercise(WireExercisedEvent wire, int nodeId, int lastDescendantNodeId)
    {
        public WireExercisedEvent Wire { get; } = wire;

        public int NodeId { get; } = nodeId;

        public int LastDescendantNodeId { get; } = lastDescendantNodeId;

        public List<TreeEvent> Children { get; } = [];
    }
}
