// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Canton.Ledger.Testing.Localnet;
using Microsoft.Extensions.DependencyInjection;
using Peaceful.Canton.Localnet.Testing;

namespace Canton.Ledger.Client.Parity.Tests;

internal static class LaneTeardown
{
    /// <summary>
    /// Revokes a lane's act-as rights and then disposes its fixture, so a rejected revoke still
    /// leaves the fixture torn down.
    /// </summary>
    internal static async Task ReleaseAsync(ActAsRightsLease actAsRights, LocalnetFixture fixture)
    {
        try
        {
            await actAsRights.DisposeAsync().ConfigureAwait(false);
        }
        finally
        {
            await fixture.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Releases everything a lane took while <paramref name="openFailure"/> is in flight. The
    /// revoke runs even when <paramref name="services"/> faults on the way out, and a failed
    /// release is reported alongside the open failure rather than replacing it — the two share a
    /// cause whenever the LocalNet is the thing that went away.
    /// </summary>
    internal static async Task ReleaseAsync(
        Exception openFailure,
        ServiceProvider? services,
        ActAsRightsLease actAsRights,
        LocalnetFixture fixture)
    {
        try
        {
            try
            {
                if (services is not null)
                {
                    await services.DisposeAsync().ConfigureAwait(false);
                }
            }
            finally
            {
                await ReleaseAsync(actAsRights, fixture).ConfigureAwait(false);
            }
        }
        catch (Exception releaseFailure)
        {
            throw new AggregateException(openFailure, releaseFailure);
        }
    }
}
