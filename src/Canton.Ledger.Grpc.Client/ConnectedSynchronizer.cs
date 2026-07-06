// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

namespace Canton.Ledger.Grpc.Client;

/// <summary>
/// A synchronizer the participant is connected to, as reported by
/// <see cref="LedgerClient.GetConnectedSynchronizersAsync"/>.
/// </summary>
public record ConnectedSynchronizer(
    string SynchronizerAlias,
    string SynchronizerId,
    string Permission);
