// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Runtime;
using Daml.Runtime.Streams;

namespace Canton.Ledger.Kernel.Streams;

/// <remarks>
/// <see cref="Offset"/> is null when the entry carried no offset this client could read. A refused
/// entry is still surfaced to the caller, so the offset must never be invented: fabricating the
/// begin-of-ledger offset would silently rewind a consumer that persists it as a resume point.
/// </remarks>
internal readonly record struct StreamEntryRefusal(LedgerOffset? Offset, UnclassifiedKind Kind);
