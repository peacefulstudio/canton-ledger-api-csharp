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
    SynchronizerPermissionLevel Permission);

/// <summary>
/// The permission a participant holds on a connected synchronizer.
/// </summary>
public enum SynchronizerPermissionLevel
{
    /// <summary>The permission was not specified by the participant.</summary>
    Unspecified,

    /// <summary>The participant can submit transactions.</summary>
    Submission,

    /// <summary>The participant can only confirm transactions.</summary>
    Confirmation,

    /// <summary>The participant can only observe transactions.</summary>
    Observation,

    /// <summary>
    /// A permission reported by the participant that this SDK version does not recognise.
    /// </summary>
    Unrecognized,
}
