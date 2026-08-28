// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

namespace Canton.Ledger.Rest.Client.Integration.Tests;

/// <summary>
/// A region of a block-structured YAML document: the lines nested under one key, addressed by the
/// indentation they share. It reads the subset the participant's tapir-generated OpenAPI document is
/// written in — space-indented block mappings, sequences written at their parent key's own indent,
/// single-line scalars — and nothing else. Every lookup that finds no key throws rather than
/// answering a default, so a document that stops matching these expectations surfaces as a failure
/// instead of a silently empty result.
/// </summary>
internal sealed class YamlBlock
{
    private const string SequenceItemMarker = "- ";

    private readonly IReadOnlyList<string> _lines;

    private YamlBlock(IReadOnlyList<string> lines)
    {
        _lines = lines;
        Indent = lines.Count == 0 ? 0 : lines.Min(IndentOf);
    }

    private int Indent { get; }

    internal static YamlBlock Root(string yaml) =>
        new(yaml.Split('\n').Select(line => line.TrimEnd('\r')).Where(IsContentful).ToList());

    internal YamlBlock ValueOf(string key)
    {
        var declaration = DeclarationOf(key);
        return new YamlBlock(_lines.Skip(declaration + 1).TakeWhile(NestsUnderDeclaration).ToList());
    }

    internal string ScalarOf(string key)
    {
        var declaration = _lines[DeclarationOf(key)].TrimStart();
        return declaration[(key.Length + 1)..].Trim().Trim('\'');
    }

    internal IReadOnlyList<string> Keys() =>
        _lines.Where(line => IndentOf(line) == Indent).Select(KeyOf).OfType<string>().ToList();

    internal IReadOnlyList<string> ScalarSequence() =>
        _lines.Where(IsSequenceItem)
            .Select(line => line.TrimStart()[SequenceItemMarker.Length..].Trim())
            .ToList();

    internal IReadOnlyList<YamlBlock> BlockSequence()
    {
        var elements = new List<YamlBlock>();
        var element = new List<string>();

        foreach (var line in _lines)
        {
            if (IsSequenceItem(line))
            {
                if (element.Count > 0) elements.Add(new YamlBlock(element));
                element = [Unmarked(line)];
            }
            else if (element.Count > 0)
            {
                element.Add(line);
            }
        }

        if (element.Count > 0) elements.Add(new YamlBlock(element));
        return elements;
    }

    private int DeclarationOf(string key)
    {
        for (var index = 0; index < _lines.Count; index++)
        {
            if (IndentOf(_lines[index]) == Indent && KeyOf(_lines[index]) == key) return index;
        }

        throw new InvalidOperationException(
            $"This YAML block declares no '{key}'. At indent {Indent} it declares: "
            + $"{string.Join(", ", Keys())}.");
    }

    private bool NestsUnderDeclaration(string line) => IndentOf(line) > Indent || IsSequenceItem(line);

    private bool IsSequenceItem(string line) =>
        IndentOf(line) == Indent && line.TrimStart().StartsWith(SequenceItemMarker, StringComparison.Ordinal);

    private string Unmarked(string line) =>
        new string(' ', Indent + SequenceItemMarker.Length)
        + line.TrimStart()[SequenceItemMarker.Length..];

    private static string? KeyOf(string line)
    {
        var trimmed = line.TrimStart();
        if (trimmed.StartsWith(SequenceItemMarker, StringComparison.Ordinal)) return null;

        var separator = trimmed.IndexOf(':', StringComparison.Ordinal);
        return separator < 0 ? null : trimmed[..separator];
    }

    private static int IndentOf(string line) => line.Length - line.TrimStart(' ').Length;

    private static bool IsContentful(string line) =>
        !string.IsNullOrWhiteSpace(line) && !line.TrimStart().StartsWith('#');
}
