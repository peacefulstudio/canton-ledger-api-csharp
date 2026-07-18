// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Grpc.Core;

namespace Canton.Ledger.Grpc.Client;

internal readonly record struct StreamMoveResult(bool Moved, RpcException? Faulted)
{
    public static async Task<StreamMoveResult> NextAsync<TResponse>(
        IAsyncStreamReader<TResponse> stream,
        CancellationToken cancellationToken)
    {
        try
        {
            var moved = await stream.MoveNext(cancellationToken).ConfigureAwait(false);
            return new StreamMoveResult(moved, null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (RpcException ex) when (CallerCancellation.Signals(ex, cancellationToken))
        {
            throw CallerCancellation.AsOperationCanceled(ex, cancellationToken);
        }
        catch (RpcException ex)
        {
            return new StreamMoveResult(false, ex);
        }
    }
}
