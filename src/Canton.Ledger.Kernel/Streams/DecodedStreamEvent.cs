// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Runtime.Streams;

namespace Canton.Ledger.Kernel.Streams;

internal readonly record struct DecodedStreamEvent<TSynchronizerScope>(
    long Offset,
    bool MatchesMarker,
    TSynchronizerScope? SynchronizerScope,
    UnclassifiedKind UnmatchedKind)
    where TSynchronizerScope : struct;
