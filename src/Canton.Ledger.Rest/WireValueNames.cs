// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

namespace Canton.Ledger.Rest.Client;

/// <summary>
/// Wire property names of the <see cref="Raw.Value"/> sum that our own specification does not
/// declare as arms, so they travel through <c>AdditionalProperties</c> instead. Encoder, decoder
/// and Daml-LF writer share these names, so a rename fails the build rather than silently leaving
/// one of them looking for a key nothing writes.
/// </summary>
internal static class WireValueNames
{
    internal const string Unit = "unit";
}
