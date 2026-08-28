// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Runtime.Data;
using RuntimeCommands = Daml.Runtime.Commands;

namespace Canton.Ledger.Abstractions;

/// <summary>
/// A single reassignment command — either an <see cref="UnassignCommand"/> departing a
/// contract from its source synchronizer or an <see cref="AssignCommand"/> completing that
/// move on the target. Submitted through <c>ICantonLedgerClient.SubmitReassignmentAsync</c>
/// (fire) or <c>ICantonLedgerClient.TrySubmitAndWaitForReassignmentAsync&lt;T&gt;</c> (await).
/// </summary>
public interface IReassignmentCommand
{
}

/// <summary>
/// Unassigns a contract from its source synchronizer, making it unusable there until a matching
/// <see cref="AssignCommand"/> completes the move on the target. The source and target
/// synchronizer ids are required — a reassignment names both endpoints explicitly,
/// unlike the optional per-submission synchronizer pin of a normal command.
/// </summary>
/// <param name="ContractId">The id of the contract to unassign.</param>
/// <param name="Source">The synchronizer the contract is currently assigned to.</param>
/// <param name="Target">The synchronizer the contract is moving to.</param>
public sealed record UnassignCommand(
    string ContractId,
    SynchronizerId Source,
    SynchronizerId Target) : IReassignmentCommand;

/// <summary>
/// Completes a reassignment on the target synchronizer, referencing the <c>reassignment_id</c>
/// carried by the <c>UnassignedEvent</c> that the matching <see cref="UnassignCommand"/> produced.
/// The client does not orchestrate the unassign→assign dance: the consumer captures the
/// reassignment id from the unassign result and passes it here. The source and target
/// synchronizer ids are required.
/// </summary>
/// <param name="ReassignmentId">The id from the unassigned event to be completed by this assignment.</param>
/// <param name="Source">The synchronizer the contract was unassigned from.</param>
/// <param name="Target">The synchronizer the contract is assigned to.</param>
public sealed record AssignCommand(
    string ReassignmentId,
    SynchronizerId Source,
    SynchronizerId Target) : IReassignmentCommand;

/// <summary>
/// A reassignment submission: the single <see cref="IReassignmentCommand"/> to submit on behalf of
/// <see cref="Submitter"/>, with an optional caller-supplied command id, workflow id, and
/// submission id. Mirrors <see cref="RuntimeCommands.CommandsSubmission"/> for the reassignment
/// write path. Construct with <see cref="Of"/> and refine with the <c>With…</c> members.
/// </summary>
public sealed record ReassignmentSubmission
{
    private ReassignmentSubmission(IReassignmentCommand command, Party submitter)
    {
        Command = command;
        Submitter = submitter;
    }

    /// <summary>The reassignment command to submit.</summary>
    public IReassignmentCommand Command { get; }

    /// <summary>The party on whose behalf the reassignment is submitted.</summary>
    public Party Submitter { get; }

    /// <summary>
    /// The command id correlating the eventual completion. Minted by the client when omitted and
    /// reported back from the submit call.
    /// </summary>
    public RuntimeCommands.CommandId? CommandId { get; private init; }

    /// <summary>The on-ledger workflow this reassignment is part of, if any.</summary>
    public RuntimeCommands.WorkflowId? WorkflowId { get; private init; }

    /// <summary>
    /// Distinguishes completions of submissions sharing a change id. Minted by the client when
    /// omitted.
    /// </summary>
    public string? SubmissionId { get; private init; }

    /// <summary>Creates a submission for <paramref name="command"/> on behalf of <paramref name="submitter"/>.</summary>
    public static ReassignmentSubmission Of(IReassignmentCommand command, Party submitter)
    {
        ArgumentNullException.ThrowIfNull(command);
        return new ReassignmentSubmission(command, submitter);
    }

    /// <summary>Pins the command id used for completion correlation.</summary>
    public ReassignmentSubmission WithCommandId(RuntimeCommands.CommandId commandId) =>
        this with { CommandId = commandId };

    /// <summary>Sets the on-ledger workflow id.</summary>
    public ReassignmentSubmission WithWorkflowId(RuntimeCommands.WorkflowId workflowId) =>
        this with { WorkflowId = workflowId };

    /// <summary>Pins the submission id.</summary>
    public ReassignmentSubmission WithSubmissionId(string submissionId) =>
        this with { SubmissionId = submissionId };
}
