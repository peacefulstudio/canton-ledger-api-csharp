// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Canton.Ledger.Abstractions;

namespace Canton.Ledger.Kernel.Wire;

internal static class MalformedResponse
{
    internal static MalformedResponseException MissingRequiredField(string detail) =>
        new($"{detail}, though the Ledger API marks the field as required.");

    internal static MalformedResponseException WithDetail(string detail) =>
        new(detail);

    internal static MalformedResponseException WithDetail(string detail, Exception innerException) =>
        new(detail, innerException);

    internal static bool IsWireDecodeFailure(Exception exception) =>
        exception is FormatException or MalformedTransactionTreeException or MalformedResponseException;

    internal static TDecoded Decoding<TWire, TDecoded>(TWire wire, Func<TWire, TDecoded> decode)
    {
        try
        {
            return decode(wire);
        }
        catch (Exception undecodable) when (IsUndecodableWireValue(undecodable))
        {
            throw WithDetail(undecodable.Message, undecodable);
        }
    }

    internal static MalformedResponseException CouldNotDecodeTransaction(
        string lookupDescription, Exception decodeFailure) =>
        new($"the transaction at {lookupDescription} could not be decoded: {DetailOf(decodeFailure)}", decodeFailure);

    private static bool IsUndecodableWireValue(Exception exception) =>
        exception is ArgumentException or InvalidOperationException or NotSupportedException
        && !IsWireDecodeFailure(exception);

    private static string DetailOf(Exception decodeFailure) =>
        decodeFailure is MalformedResponseException malformed ? malformed.Detail : decodeFailure.Message;
}
