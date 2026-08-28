// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Concurrent;
using System.Globalization;

namespace Canton.Ledger.Abstractions;

/// <summary>
/// Resolves a Daml package name to a concrete package id over any <see cref="IAdminClient"/>,
/// caching each answer so a name costs at most one <see cref="IAdminClient.ListKnownPackagesAsync"/>
/// round-trip in single-threaded use. Under concurrent first-callers for the same name, each
/// concurrent caller may issue its own round-trip; only one result is written to the cache.
/// </summary>
/// <remarks>
/// <para>
/// A package name usually maps to several package ids, one per version. This resolver returns the
/// id of the <em>highest</em> version, comparing versions component-wise as integers so that
/// <c>2.10.0</c> outranks <c>2.9.0</c>. It throws when the participant knows no package under the
/// name, rather than reporting an absent package as an empty id.
/// </para>
/// <para>
/// The cache is a snapshot: a version uploaded after a name first resolves is not observed, and
/// there is no invalidation. That is the deliberate trade for the round-trip it saves. Callers who
/// need the participant to keep choosing the version should not resolve an id at all — they should
/// address the template by package name with
/// <see cref="IdentifierExtensions.ForPackageName(string, string, string)"/>, which Canton resolves
/// afresh on every request.
/// </para>
/// </remarks>
public sealed class PackageIdResolver
{
    private readonly IAdminClient _adminClient;
    private readonly ConcurrentDictionary<string, string> _resolvedPackageIds = new(StringComparer.Ordinal);

    /// <summary>Creates a resolver over the participant reached by <paramref name="adminClient"/>.</summary>
    /// <param name="adminClient">The admin client whose known packages are searched.</param>
    public PackageIdResolver(IAdminClient adminClient)
    {
        ArgumentNullException.ThrowIfNull(adminClient);

        _adminClient = adminClient;
    }

    /// <summary>
    /// Resolves <paramref name="packageName"/> to the package id of its highest known version,
    /// answering from the cache once a name has resolved.
    /// </summary>
    /// <param name="packageName">The Daml package name, without the <c>#</c> prefix.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="ArgumentException"><paramref name="packageName"/> is null, empty or whitespace.</exception>
    /// <exception cref="InvalidOperationException">
    /// The participant knows no package under that name, or a package under that name reports a
    /// version that is not dot-separated non-negative integers.
    /// </exception>
    public async Task<string> ResolvePackageIdAsync(
        string packageName,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageName);

        if (_resolvedPackageIds.TryGetValue(packageName, out var cached))
        {
            return cached;
        }

        var knownPackages = await _adminClient.ListKnownPackagesAsync(cancellationToken).ConfigureAwait(false);
        var packageId = HighestVersionPackageId(knownPackages, packageName);
        _resolvedPackageIds.TryAdd(packageName, packageId);
        return _resolvedPackageIds[packageName];
    }

    private static string HighestVersionPackageId(
        IReadOnlyList<PackageDetails> knownPackages,
        string packageName)
    {
        string? highestPackageId = null;
        int[]? highestVersion = null;

        foreach (var package in knownPackages)
        {
            if (!string.Equals(package.Name, packageName, StringComparison.Ordinal))
            {
                continue;
            }

            var version = ParseVersion(package);
            if (highestVersion is null || Compare(version, highestVersion) > 0)
            {
                highestPackageId = package.PackageId;
                highestVersion = version;
            }
        }

        return highestPackageId ?? throw new InvalidOperationException(
            $"The participant knows no package named '{packageName}'.");
    }

    private static int[] ParseVersion(PackageDetails package)
    {
        var components = package.Version.Split('.');
        var parsed = new int[components.Length];

        for (var index = 0; index < components.Length; index++)
        {
            parsed[index] = int.TryParse(
                components[index], NumberStyles.None, CultureInfo.InvariantCulture, out var component)
                ? component
                : throw new InvalidOperationException(
                    $"Package '{package.Name}' ({package.PackageId}) reports version "
                    + $"'{package.Version}', which is not the dot-separated non-negative integers "
                    + "a Daml package version is required to be.");
        }

        return parsed;
    }

    private static int Compare(int[] left, int[] right)
    {
        for (var index = 0; index < Math.Max(left.Length, right.Length); index++)
        {
            var comparison = ComponentAt(left, index).CompareTo(ComponentAt(right, index));
            if (comparison != 0)
            {
                return comparison;
            }
        }

        return 0;
    }

    private static int ComponentAt(int[] version, int index) => index < version.Length ? version[index] : 0;
}
