// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Xunit;

namespace Canton.Ledger.Rest.Client.Integration.Tests;

/// <summary>
/// Runs its members after every parallel test class has finished, so no other lane grants or
/// revokes rights on the shared validator user while they hold a stream open. Every such change
/// aborts that user's open streams with <c>STALE_STREAM_AUTHORIZATION</c>.
/// </summary>
[CollectionDefinition(nameof(ValidatorUserRightsIsolation), DisableParallelization = true)]
public sealed class ValidatorUserRightsIsolation;
