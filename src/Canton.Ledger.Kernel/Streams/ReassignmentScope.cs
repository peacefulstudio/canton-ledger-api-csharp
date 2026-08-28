// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Runtime.Data;

namespace Canton.Ledger.Kernel.Streams;

internal readonly record struct ReassignmentScope(SynchronizerId Source, SynchronizerId Target);
