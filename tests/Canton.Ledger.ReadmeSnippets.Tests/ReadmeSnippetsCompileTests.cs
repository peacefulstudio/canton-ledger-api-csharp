// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Canton.Ledger.Abstractions;
using AwesomeAssertions;
using Xunit;

namespace Canton.Ledger.ReadmeSnippets.Tests;

public class ReadmeSnippetsCompileTests
{
    private static string ReadReadme()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "README.md");
        return File.ReadAllText(path);
    }

    [Fact]
    public void Readme_harness_compiles_against_the_shipped_surface()
    {
        // The snippet harness (ReadmeSnippets.cs) compiles as part of this project, so a
        // regression in any API the README documents breaks the build. ITokenProvider.None
        // is the unauthenticated sentinel the README quick start leans on; assert it resolves
        // so this test has runtime presence rather than being compile-only.
        ITokenProvider.None.Should().NotBeNull();
    }

    [Fact]
    public void Readme_quickstart_uses_the_correct_api_surface()
    {
        var readme = ReadReadme();

        // Corrected surface.
        readme.Should().Contain("ITokenProvider.None");
        readme.Should().Contain("TryCreateAsync");
        readme.Should().Contain("TryExerciseAsync");
        readme.Should().Contain("ExerciseCommand.For");
        readme.Should().Contain("new ChoiceName(");
        readme.Should().Contain("new PqsClient(pqsOptions)");

        // Distribution metadata points at nuget.org, not GitHub Packages.
        readme.Should().Contain("nuget.org");
        readme.Should().NotContain("github.com/peacefulstudio/canton-ledger-api-csharp/pkgs/nuget");
        readme.Should().NotContain("GitHub Packages");

        // The stale v0.1.4 release badge and the private-repo comment are gone.
        readme.Should().NotContain("v0.1.4");
        readme.Should().NotContain("repo is private");

        // All six packages are listed.
        readme.Should().Contain("Canton.Ledger.OpenTelemetry");
        readme.Should().Contain("Daml.Runtime.Grpc");
    }
}
