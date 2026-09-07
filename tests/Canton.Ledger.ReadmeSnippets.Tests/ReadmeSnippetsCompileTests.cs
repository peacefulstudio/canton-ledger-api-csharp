// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Text.RegularExpressions;
using Canton.Ledger.Abstractions;
using AwesomeAssertions;
using Xunit;

namespace Canton.Ledger.ReadmeSnippets.Tests;

public partial class ReadmeSnippetsCompileTests
{
    private const string PackagesHeading = "## Packages";
    private const string PackableProjectDirectory = "src-projects";

    private static string ReadReadme()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "README.md");
        return File.ReadAllText(path);
    }

    [Fact]
    public void Readme_harness_compiles_against_the_shipped_surface()
    {
        ITokenProvider.None.Should().NotBeNull(
            "ReadmeSnippets.cs compiles as part of this project, so a regression in any API the "
            + "README documents already breaks the build; resolving the unauthenticated sentinel "
            + "the registrations fall back to gives this test runtime presence too");
    }

    [Fact]
    public void Readme_quickstart_uses_the_correct_api_surface()
    {
        var readme = ReadReadme();

        readme.Should().Contain("TryCreateAsync");
        readme.Should().Contain("TryExerciseAsync");
        readme.Should().Contain("ExerciseCommand.For");
        readme.Should().Contain("new ChoiceName(");
        readme.Should().Contain("QueryAsync<Asset>");
    }

    [Fact]
    public void Readme_quickstart_enters_every_client_through_dependency_injection()
    {
        var readme = ReadReadme();

        readme.Should().Contain("services.AddLedgerClient(");
        readme.Should().Contain("services.AddAdminClient(");
        readme.Should().Contain("services.AddPqsClient(");
        readme.Should().Contain("GetRequiredService<ICantonLedgerClient>()");
        readme.Should().Contain("GetRequiredService<IAdminClient>()");
        readme.Should().Contain("GetRequiredService<IPqsClient>()");
    }

    [Theory]
    [InlineData("new LedgerClient(")]
    [InlineData("new AdminClient(")]
    [InlineData("new PqsClient(")]
    public void Readme_documents_no_direct_client_construction(string constructorCall)
    {
        ReadReadme().Should().NotContain(
            constructorCall,
            "the clients are entered through the container, and a documented constructor call is one "
            + "a consumer will paste and then find unavailable the moment the concrete client is "
            + "internalized");
    }

    [Fact]
    public void Readme_distribution_metadata_points_at_nuget_org()
    {
        var readme = ReadReadme();

        readme.Should().Contain("nuget.org");
        readme.Should().NotContain("github.com/peacefulstudio/canton-ledger-api-csharp/pkgs/nuget");
        readme.Should().NotContain("GitHub Packages");
    }

    [Fact]
    public void Readme_carries_no_stale_release_badge_or_private_repo_note()
    {
        var readme = ReadReadme();

        readme.Should().NotContain("v0.1.4");
        readme.Should().NotContain("repo is private");
    }

    [Fact]
    public void Readme_packages_table_lists_every_packable_project()
    {
        var listed = PackagesTableIds(ReadReadme());

        PackableProjectIds().Should().OnlyContain(
            id => listed.Contains(id),
            "the Packages table is the only place a consumer learns a package exists, so a project "
            + "that ships without a row is invisible and a row that disappears takes the package "
            + "with it");
    }

    [Fact]
    public void Readme_packages_table_lists_no_package_that_is_not_a_packable_project()
    {
        var packable = PackableProjectIds().ToHashSet(StringComparer.Ordinal);

        PackagesTableIds(ReadReadme()).Should().OnlyContain(
            id => packable.Contains(id),
            "a row for a package src/ no longer builds sends consumers to a nuget.org page that "
            + "will never be updated again");
    }

    private static IReadOnlyCollection<string> PackagesTableIds(string readme)
    {
        var heading = readme.IndexOf(PackagesHeading, StringComparison.Ordinal);
        heading.Should().BeGreaterThanOrEqualTo(0, $"README.md must still carry a '{PackagesHeading}' section");

        var next = readme.IndexOf("\n## ", heading + PackagesHeading.Length, StringComparison.Ordinal);
        var section = next < 0 ? readme[heading..] : readme[heading..next];

        var ids = PackageRow().Matches(section).Select(match => match.Groups[1].Value).ToList();
        ids.Should().NotBeEmpty("the Packages table rows must still parse as '| [`Package.Id`](…) | … |'");
        return ids;
    }

    private static IReadOnlyCollection<string> PackableProjectIds()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, PackableProjectDirectory);
        var projects = Directory.GetFiles(directory, "*.csproj");
        projects.Should().NotBeEmpty(
            $"{PackableProjectDirectory} is populated from src/*/*.csproj by this project's csproj, "
            + "and an empty copy would compare the README against nothing");

        return projects
            .Select(path => (Path: path, Content: File.ReadAllText(path)))
            .Where(project => !PackableFalse().IsMatch(project.Content))
            .Select(project => PackageId().Match(project.Content) is { Success: true } id
                ? id.Groups[1].Value
                : Path.GetFileNameWithoutExtension(project.Path))
            .ToList();
    }

    [GeneratedRegex(@"^\| \[`([^`]+)`\]", RegexOptions.Multiline)]
    private static partial Regex PackageRow();

    [GeneratedRegex(@"<IsPackable>\s*false\s*</IsPackable>")]
    private static partial Regex PackableFalse();

    [GeneratedRegex(@"<PackageId>([^<]+)</PackageId>")]
    private static partial Regex PackageId();
}
