// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Runtime.Data;

namespace Canton.Ledger.Abstractions;

/// <summary>
/// Builds by-package-name template identifiers. The Ledger API addresses a template either by
/// package id (<c>&lt;package-id&gt;:&lt;module&gt;:&lt;entity&gt;</c>) or, since smart contract
/// upgrades, by package name (<c>#&lt;package-name&gt;:&lt;module&gt;:&lt;entity&gt;</c>), which
/// the participant resolves dynamically on every request and which therefore keeps following a
/// newly uploaded version with no repinning by the caller. The leading <c>#</c> is the entire
/// convention, and it is otherwise learnable only from Canton's own sources.
/// </summary>
public static class IdentifierExtensions
{
    private const string PackageNameReferencePrefix = "#";

    /// <summary>Extension members for <see cref="Identifier"/>.</summary>
    extension(Identifier)
    {
        /// <summary>
        /// Builds a by-package-name <see cref="Identifier"/> whose package-id component is
        /// <paramref name="packageName"/> behind the <c>#</c> prefix. Use it to address a template
        /// version-agnostically and let the participant choose the version; use
        /// <see cref="PackageIdResolver"/> instead when a concrete package id is required.
        /// </summary>
        /// <param name="packageName">The Daml package name, without the <c>#</c> prefix.</param>
        /// <param name="moduleName">The module name.</param>
        /// <param name="entityName">The template or interface name.</param>
        /// <exception cref="ArgumentException">
        /// An argument is null, empty or whitespace, or <paramref name="packageName"/> already
        /// carries the <c>#</c> prefix — which would address a package literally named <c>#…</c>.
        /// </exception>
        public static Identifier ForPackageName(string packageName, string moduleName, string entityName)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(packageName);
            ArgumentException.ThrowIfNullOrWhiteSpace(moduleName);
            ArgumentException.ThrowIfNullOrWhiteSpace(entityName);

            if (packageName.StartsWith(PackageNameReferencePrefix, StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    $"Package name '{packageName}' already carries the '#' by-package-name prefix; "
                    + "pass the bare package name.",
                    nameof(packageName));
            }

            return new Identifier($"{PackageNameReferencePrefix}{packageName}", moduleName, entityName);
        }
    }
}
