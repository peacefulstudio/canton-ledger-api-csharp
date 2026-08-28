// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Runtime.Contracts;
using Daml.Runtime.Data;

namespace Canton.Ledger.Abstractions;

/// <summary>
/// An active contract observed through a Daml interface: its interface-typed
/// <see cref="ContractId{T}"/> paired with the participant-computed interface view.
/// </summary>
/// <remarks>
/// The interface analogue of <see cref="Contract{T}"/>. A template query yields the template
/// payload in <c>Contract&lt;T&gt;.Data</c>; an interface query yields the interface view in
/// <see cref="View"/>, because PQS projects the participant-computed view (not the implementing
/// template's fields) onto an interface row. <see cref="Id"/> is typed to the interface so it can
/// drive interface choices directly.
/// </remarks>
/// <typeparam name="TInterface">The Daml interface marker the contract was queried through.</typeparam>
/// <typeparam name="TView">The interface's view record type.</typeparam>
/// <param name="Id">The contract id, typed to the interface.</param>
/// <param name="View">The deserialized interface view projection.</param>
public sealed record InterfaceContract<TInterface, TView>(ContractId<TInterface> Id, TView View)
    where TInterface : IDamlInterface, IHasView<TView>
    where TView : IDamlRecord;
