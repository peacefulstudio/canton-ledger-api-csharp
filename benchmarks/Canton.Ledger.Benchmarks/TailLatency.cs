// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

namespace Canton.Ledger.Benchmarks;

internal sealed record TailLatency(LatencySummary Delivery, LatencySummary Submit, int StreamReopens);
