// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Canton.Ledger.Kernel.Telemetry;
using Canton.Ledger.Kernel.Wire;
using Com.Daml.Ledger.Api.V2;
using Daml.Runtime.Contracts;
using RuntimeCommands = Daml.Runtime.Commands;

namespace Canton.Ledger.Grpc.Client;

internal sealed partial class LedgerClient
{
    /// <inheritdoc />
    public Task<TransactionResult> GetUpdateByOffsetAsync(
        long offset,
        RuntimeCommands.SubmitterInfo submitter,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(offset);

        var request = new GetUpdateByOffsetRequest
        {
            Offset = offset,
            UpdateFormat = SubscribeRequestBuilder.BuildTransactionUpdateFormat(submitter),
        };

        return _invoker.InvokeTracedAsync<LedgerClient, GetUpdateResponse, TransactionResult>(
            LedgerCallInvoker.Source,
            UpdateService.Descriptor,
            "GetUpdateByOffset",
            (headers, deadline, token) => _updateService.GetUpdateByOffsetAsync(request, headers, deadline, token),
            response => ProjectPointReadTransaction(response, $"offset {offset}"),
            cancellationToken,
            timeout: timeout,
            configureActivity: activity =>
            {
                activity?.SetTag(LedgerActivityTagNames.CantonOffset, offset);
                activity.SetSubmitterTags(submitter);
            });
    }

    /// <inheritdoc />
    public Task<TransactionResult> GetUpdateByIdAsync(
        string updateId,
        RuntimeCommands.SubmitterInfo submitter,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(updateId);

        var request = new GetUpdateByIdRequest
        {
            UpdateId = updateId,
            UpdateFormat = SubscribeRequestBuilder.BuildTransactionUpdateFormat(submitter),
        };

        return _invoker.InvokeTracedAsync<LedgerClient, GetUpdateResponse, TransactionResult>(
            LedgerCallInvoker.Source,
            UpdateService.Descriptor,
            "GetUpdateById",
            (headers, deadline, token) => _updateService.GetUpdateByIdAsync(request, headers, deadline, token),
            response => ProjectPointReadTransaction(response, $"id {updateId}"),
            cancellationToken,
            timeout: timeout,
            configureActivity: activity =>
            {
                activity?.SetTag(LedgerActivityTagNames.CantonUpdateId, updateId);
                activity.SetSubmitterTags(submitter);
            });
    }

    /// <inheritdoc />
    public Task<TransactionTree> GetUpdateTreeByOffsetAsync(
        long offset,
        RuntimeCommands.SubmitterInfo submitter,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(offset);

        var request = new GetUpdateByOffsetRequest
        {
            Offset = offset,
            UpdateFormat = SubscribeRequestBuilder.BuildTransactionUpdateFormat(submitter),
        };

        return _invoker.InvokeTracedAsync<LedgerClient, GetUpdateResponse, TransactionTree>(
            LedgerCallInvoker.Source,
            UpdateService.Descriptor,
            "GetUpdateByOffset",
            (headers, deadline, token) => _updateService.GetUpdateByOffsetAsync(request, headers, deadline, token),
            response => ProjectPointRead(response, $"offset {offset}", GrpcTransactionTreeProjector.Project),
            cancellationToken,
            timeout: timeout,
            configureActivity: activity =>
            {
                activity?.SetTag(LedgerActivityTagNames.CantonOffset, offset);
                activity.SetSubmitterTags(submitter);
            });
    }

    private static TransactionResult ProjectPointReadTransaction(GetUpdateResponse response, string lookupDescription) =>
        ProjectPointRead(response, lookupDescription, GrpcTransactionResultProjector.Project);

    internal static TProjection ProjectPointRead<TProjection>(
        GetUpdateResponse response,
        string lookupDescription,
        Func<Transaction, TProjection> project)
    {
        if (response.UpdateCase != GetUpdateResponse.UpdateOneofCase.Transaction)
        {
            throw new InvalidOperationException(
                $"Update at {lookupDescription} is a {response.UpdateCase}, not a Transaction; "
                + "point reads only project transaction-shaped updates.");
        }

        try
        {
            return project(response.Transaction);
        }
        catch (Exception decodeFailure) when (MalformedResponse.IsWireDecodeFailure(decodeFailure))
        {
            throw MalformedResponse.CouldNotDecodeTransaction(lookupDescription, decodeFailure);
        }
    }
}
