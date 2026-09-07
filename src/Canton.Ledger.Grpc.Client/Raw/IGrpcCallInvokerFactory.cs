// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Grpc.Core;

namespace Canton.Ledger.Grpc.Client.Raw;

/// <summary>
/// The gRPC escape hatch: hands out a <see cref="CallInvoker"/> for driving raw generated gRPC
/// stubs — services or overloads the typed
/// <see cref="Canton.Ledger.Abstractions.ICantonLedgerClient"/> and
/// <see cref="Canton.Ledger.Abstractions.IAdminClient"/> surfaces do not cover — through the same
/// authentication, deadline and retry plumbing the typed clients use. Opt in with
/// <c>AddLedgerRawGrpc</c> and resolve it like any other service; prefer the typed surfaces for
/// anything they already cover.
/// </summary>
public interface IGrpcCallInvokerFactory
{
    /// <summary>
    /// Creates a <see cref="CallInvoker"/> that authenticated raw stubs can be constructed over:
    /// construct any generated stub with it, e.g.
    /// <c>new StateService.StateServiceClient(factory.CreateCallInvoker())</c>, and call it without
    /// building any <see cref="CallOptions"/> by hand.
    /// </summary>
    /// <remarks>
    /// A bearer token is resolved from the registered
    /// <see cref="Canton.Ledger.Abstractions.ITokenProvider"/> on every call;
    /// <see cref="Canton.Ledger.Abstractions.ITokenProvider.None"/> sends no
    /// <c>authorization</c> header, and a caller-supplied <c>authorization</c> metadata entry wins
    /// over the resolved token. Unary calls carry the configured
    /// <see cref="LedgerClientOptions.Timeout"/> as a per-attempt deadline when the caller sets none
    /// and run through the configured <see cref="LedgerClientOptions.Retry"/> pipeline, with auth
    /// headers and deadline recomputed on each attempt; a caller-supplied deadline is kept verbatim.
    /// Streaming calls attach auth headers but carry no default deadline — a server stream may
    /// legitimately outlive any per-call budget — and are never retried.
    /// The factory owns the channel the invoker runs on and the container owns the factory, so the
    /// invoker stays valid for as long as the resolving provider does; dispose the provider, not the
    /// invoker.
    /// </remarks>
    /// <returns>A <see cref="CallInvoker"/> that authenticated raw stubs can be constructed over.</returns>
    CallInvoker CreateCallInvoker();
}
