// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Canton.Ledger.Abstractions;

namespace Canton.Ledger.Benchmarks;

internal sealed record Transport(string Name, ICantonLedgerClient Client);
