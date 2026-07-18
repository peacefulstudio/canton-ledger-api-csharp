// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Grpc.Core;

namespace Canton.Ledger.Grpc.Client.Tests;

/// <summary>
/// Stream-reader fake matching the real transport contract: Grpc.Net.Client defaults
/// <c>GrpcChannelOptions.ThrowOperationCanceledOnCancellation</c> to <c>false</c>, so a
/// cancelled token surfaces from <c>MoveNext</c> as <see cref="RpcException"/> with
/// <see cref="StatusCode.Cancelled"/>, never as <see cref="OperationCanceledException"/>.
/// </summary>
internal sealed class FakeStreamReader<T> : IAsyncStreamReader<T>
{
    internal const string CancelledByClientDetail = "Call canceled by the client.";

    private readonly IReadOnlyList<T> _items;
    private readonly Exception? _afterItemsException;
    private int _index = -1;
    private T _current = default!;

    public FakeStreamReader(IEnumerable<T> items, Exception? afterItemsException = null)
    {
        _items = items.ToList();
        _afterItemsException = afterItemsException;
    }

    public T Current => _current;

    public Task<bool> MoveNext(CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromException<bool>(
                new RpcException(new Status(StatusCode.Cancelled, CancelledByClientDetail)));
        }

        _index++;
        if (_index < _items.Count)
        {
            _current = _items[_index];
            return Task.FromResult(true);
        }

        if (_afterItemsException is not null)
        {
            return Task.FromException<bool>(_afterItemsException);
        }

        return Task.FromResult(false);
    }
}
