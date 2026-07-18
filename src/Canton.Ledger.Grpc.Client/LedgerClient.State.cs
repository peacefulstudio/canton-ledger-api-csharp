// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Canton.Ledger.Kernel.Telemetry;
using Com.Daml.Ledger.Api.V2;
using Daml.Runtime;
using Daml.Runtime.Data;

namespace Canton.Ledger.Grpc.Client;

public sealed partial class LedgerClient
{
    /// <inheritdoc />
    public Task<LedgerOffset> GetLedgerEndAsync(
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default) =>
        _invoker.ExecuteTracedAsync<LedgerClient, LedgerOffset>(
            LedgerCallInvoker.Source,
            StateService.Descriptor,
            "GetLedgerEnd",
            async (activity, token) =>
            {
                var response = await _invoker.InvokeAsync(
                    (headers, deadline, callToken) => _stateService.GetLedgerEndAsync(new GetLedgerEndRequest(), headers, deadline, callToken),
                    token,
                    timeout).ConfigureAwait(false);
                activity?.SetTag(LedgerClientActivityTags.CantonOffset, response.Offset);
                return LedgerOffset.At(response.Offset);
            },
            cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyList<ConnectedSynchronizer>> GetConnectedSynchronizersAsync(
        Party? party = null,
        string? participantId = null,
        CancellationToken cancellationToken = default)
    {
        var request = new GetConnectedSynchronizersRequest();
        if (party is { } requestedParty)
            request.Party = requestedParty.Id;
        if (participantId is not null)
            request.ParticipantId = participantId;

        return _invoker.InvokeTracedAsync<LedgerClient, GetConnectedSynchronizersResponse, IReadOnlyList<ConnectedSynchronizer>>(
            LedgerCallInvoker.Source,
            StateService.Descriptor,
            "GetConnectedSynchronizers",
            (headers, deadline, token) => _stateService.GetConnectedSynchronizersAsync(request, headers, deadline, token),
            response => response.ConnectedSynchronizers
                .Select(s => new ConnectedSynchronizer(s.SynchronizerAlias, s.SynchronizerId, MapPermission(s.Permission)))
                .ToList(),
            cancellationToken,
            configureActivity: activity =>
            {
                if (party is { } taggedParty)
                    activity?.SetTag(LedgerClientActivityTags.CantonPartyId, taggedParty.Id);
                if (participantId is not null)
                    activity?.SetTag(LedgerClientActivityTags.CantonParticipantId, participantId);
            });
    }

    private static SynchronizerPermissionLevel MapPermission(ParticipantPermission permission) => permission switch
    {
        ParticipantPermission.Unspecified => SynchronizerPermissionLevel.Unspecified,
        ParticipantPermission.Submission => SynchronizerPermissionLevel.Submission,
        ParticipantPermission.Confirmation => SynchronizerPermissionLevel.Confirmation,
        ParticipantPermission.Observation => SynchronizerPermissionLevel.Observation,
        _ => SynchronizerPermissionLevel.Unrecognized,
    };

    /// <inheritdoc />
    public Task<string> GetLedgerApiVersionAsync(CancellationToken cancellationToken = default) =>
        _invoker.InvokeTracedAsync<LedgerClient, GetLedgerApiVersionResponse, string>(
            LedgerCallInvoker.Source,
            VersionService.Descriptor,
            "GetLedgerApiVersion",
            (headers, deadline, token) => _versionService.GetLedgerApiVersionAsync(new GetLedgerApiVersionRequest(), headers, deadline, token),
            response => response.Version,
            cancellationToken);
}
