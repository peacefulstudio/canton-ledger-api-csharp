// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Canton.Ledger.Abstractions;

namespace Canton.Ledger.Rest.Client;

internal abstract record RestCallFailure
{
    private RestCallFailure()
    {
    }

    internal sealed record Rejected(ParsedLedgerError Parsed) : RestCallFailure;

    internal sealed record Transport(int StatusCode, string Message, Exception Cause) : RestCallFailure;

    internal sealed record Undecodable(string Message, Exception? Cause) : RestCallFailure;
}
