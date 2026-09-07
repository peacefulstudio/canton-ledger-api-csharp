// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Richtypes;
using Xunit;

namespace Canton.Ledger.Client.Parity.Tests;

/// <summary>
/// Pins where a generated exerciser's argument guard fires. The 0.5.0 emitter forwards a choice to
/// <c>TrySubmitSingleAsync</c>, and emits the forwarding members without <c>async</c> — their
/// guards therefore throw before a task exists, so a caller asserting on them needs
/// <see cref="Assert.Throws{T}(Action)"/> rather than <c>Assert.ThrowsAsync</c>. A choice whose
/// result the exerciser has to project is still emitted <c>async</c>, and its guard still faults
/// the returned task; the two shapes are pinned together so a later emitter change that unifies
/// them is visible here rather than only at a consumer.
/// </summary>
public class GeneratedExerciserArgumentGuardTests
{
    private static readonly Party Alice = new("party::alice");

    [Fact]
    public void A_forwarding_exerciser_rejects_a_null_client_before_a_task_exists()
    {
        Assert.Throws<ArgumentNullException>(() =>
        {
            _ = MarkerSubmissionExtensions.CreateAsync(
                null!, new Marker(Alice), TestContext.Current.CancellationToken);
        });
    }

    [Fact]
    public async Task A_projecting_exerciser_rejects_a_null_client_on_the_returned_task()
    {
        var contractId = new ContractId<Marker>("00marker");

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => contractId.ArchiveAsync(
                null!, Alice, cancellationToken: TestContext.Current.CancellationToken));
    }
}
