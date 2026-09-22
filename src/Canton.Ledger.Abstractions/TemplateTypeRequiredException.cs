// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

namespace Canton.Ledger.Abstractions;

/// <summary>
/// Thrown when a Daml payload read over the JSON Ledger API names a template, choice or interface for
/// which no single generated type is loaded, so the payload cannot be decoded.
/// </summary>
/// <remarks>
/// Daml-LF JSON does not say which Daml type a value has: <c>"alice::1220ab"</c> is a Party or a Text
/// depending on the field it fills. The JSON Ledger API transport therefore decodes every payload against
/// the type generated for its template, choice or interface, and refuses rather than guessing when that
/// type is not loaded, or when more than one loaded type claims it. Load exactly one assembly generated for
/// the payload's package before reading it.
/// </remarks>
public sealed class TemplateTypeRequiredException : InvalidOperationException
{
    private const string LoadAdvice =
        "load exactly one assembly generated for its Daml package before reading this payload over the JSON Ledger API.";

    /// <summary>Creates an exception naming the Daml type identifier that has no loaded generated type.</summary>
    /// <param name="typeId">The template or interface identifier, as <c>package:module:entity</c>.</param>
    /// <param name="choiceName">
    /// The choice whose argument or result could not be decoded, or <see langword="null"/> when the
    /// payload is a create argument, contract key or interface view.
    /// </param>
    public TemplateTypeRequiredException(string typeId, string? choiceName = null)
        : base(
            (choiceName is null
                ? $"No generated type is loaded for '{typeId}'; "
                : $"No generated type is loaded for choice '{choiceName}' of '{typeId}'; ")
            + LoadAdvice)
    {
        TypeId = typeId;
        ChoiceName = choiceName;
        ConflictingTypes = [];
    }

    /// <summary>
    /// Creates an exception naming the Daml type identifier that more than one loaded generated type claims.
    /// </summary>
    /// <param name="typeId">The template or interface identifier, as <c>package:module:entity</c>.</param>
    /// <param name="choiceName">
    /// The choice whose argument or result could not be decoded, or <see langword="null"/> when the
    /// payload is a create argument, contract key or interface view.
    /// </param>
    /// <param name="conflictingTypes">The full names of the loaded generated types that claim it.</param>
    public TemplateTypeRequiredException(string typeId, string? choiceName, IReadOnlyList<string> conflictingTypes)
        : base(
            (choiceName is null
                ? $"'{typeId}' is claimed by more than one loaded generated type ({string.Join(", ", conflictingTypes)}); "
                : $"choice '{choiceName}' of '{typeId}' is claimed by more than one loaded generated type ({string.Join(", ", conflictingTypes)}); ")
            + LoadAdvice)
    {
        TypeId = typeId;
        ChoiceName = choiceName;
        ConflictingTypes = conflictingTypes;
    }

    private TemplateTypeRequiredException(string typeId, string? choiceName, string message)
        : base(message)
    {
        TypeId = typeId;
        ChoiceName = choiceName;
        ConflictingTypes = [];
    }

    /// <summary>
    /// Creates an exception for a contract key on a template whose loaded generated type declares no key —
    /// most likely a type generated for a different version of the payload's package, matched by module and
    /// entity name.
    /// </summary>
    /// <param name="typeId">The template identifier, as <c>package:module:entity</c>.</param>
    /// <param name="loadedTemplateType">The full name of the loaded generated template type that declares no key.</param>
    /// <returns>The exception to throw.</returns>
    public static TemplateTypeRequiredException ForKeylessTemplate(string typeId, string loadedTemplateType) =>
        new(
            typeId,
            null,
            $"A contract key arrived for '{typeId}', but the loaded generated template {loadedTemplateType} declares no contract key, "
            + "so it was likely generated for a different version of the package; " + LoadAdvice);

    /// <summary>The template or interface identifier, as <c>package:module:entity</c>.</summary>
    public string TypeId { get; }

    /// <summary>The choice whose payload could not be decoded, when the payload belongs to a choice.</summary>
    public string? ChoiceName { get; }

    /// <summary>
    /// The full names of the loaded generated types that claim <see cref="TypeId"/>; empty when none does.
    /// </summary>
    public IReadOnlyList<string> ConflictingTypes { get; }
}
