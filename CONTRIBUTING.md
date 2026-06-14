# Contributing to canton-ledger-api-csharp

Thanks for your interest. This document covers everything you need to send a
patch — from cloning the repo to getting your PR merged.

## Code of Conduct

By participating in this project you agree to abide by the
[Code of Conduct](CODE_OF_CONDUCT.md).

## Getting set up

If you have direct write access to `peacefulstudio/canton-ledger-api-csharp` (i.e.
you're a maintainer), clone the upstream repo:

```bash
git clone https://github.com/peacefulstudio/canton-ledger-api-csharp.git
cd canton-ledger-api-csharp
git checkout main
dotnet restore && dotnet build
dotnet test
```

If you're an external contributor, **start by forking the repo** (see
"Opening a pull request" below for the full fork/upstream flow). The
build and test commands inside the cloned tree are the same:

```bash
dotnet restore && dotnet build
dotnet test
```

You'll need [.NET SDK](https://dotnet.microsoft.com/download) — install the version pinned in the target repo's `global.json`.

### Pre-commit hook

If the repo ships a pre-commit hook (lint + build + test) under `.githooks/`,
activate it once after cloning:

```bash
git config core.hooksPath .githooks
```

## Branching model

| Branch  | Purpose                          |
|---------|----------------------------------|
| `main`  | Default branch — open PRs here   |
| `stage` | Staging / pre-production         |
| `prod`  | Production — release tags only   |

All PRs target `main`. Promotion to `stage` and `prod` is handled by the
maintainers.

## Test-driven development

Bug fixes and new features must follow red-green TDD:

1. **Red** — write a failing test that describes the desired behaviour.
2. **Green** — write the minimum production code to make it pass.
3. **Refactor** — clean up while keeping tests green.

```bash
dotnet test --configuration Release --collect:"XPlat Code Coverage" --results-directory ./coverage
```

## Code style

- **.NET / C#** — substitute the SDK and `<LangVersion>` from `global.json` and `Directory.Build.props` in the target repo.
- Every `.cs` source file starts with the two-line SPDX copyright header:<br>`// Copyright (c) 2026 Peaceful Studio OÜ`<br>`// SPDX-License-Identifier: Apache-2.0`<br>The `<Copyright>` tag in `Directory.Build.props` stays in place too — it drives the assembly attribute. The two coexist; they don't replace each other.
- Follow standard .NET conventions (`dotnet format` for layout; `Microsoft.Extensions.Logging.ILogger` resolved from DI at point of use, with `ILogger<T>` reserved for classes where logging is central — see `peaceful-rules` skill for the project's published pattern; structured-template log strings, no interpolation; `TimeSpan` for durations rather than raw numeric milliseconds; xUnit `Theory` + `MemberData`/`InlineData` for parameterised tests). Public APIs documented with XML doc comments.
- Code should be expressive enough to not need comments. Add a comment only
  when the *why* is non-obvious (a workaround for an external bug, a hidden
  invariant). Don't comment on *what* the code does.

## Documentation

Public APIs are documented with XML doc comments
(`/// <summary>...</summary>`) directly on the type or member.
IDE tooling (Visual Studio, Rider, OmniSharp) surfaces them on hover
and at completion sites; there is no separate doc-generation step to
run. If your change touches a public API, update or add the relevant
doc comment in the same PR.

If your change touches a public API, schema, or example, update the
relevant documentation in the same PR.

## Opening a pull request

External contributors usually don't have write access to
`peacefulstudio/canton-ledger-api-csharp`, so the PR flow goes through a fork:

1. Fork `peacefulstudio/canton-ledger-api-csharp` to your own GitHub account using
   the **Fork** button on the repo page (or `gh repo fork`).
2. Clone your fork (`origin` is set automatically) and add the
   upstream as a second remote so you can pull future changes:
   ```bash
   git clone https://github.com/<your-username>/canton-ledger-api-csharp.git
   cd canton-ledger-api-csharp
   git remote add upstream https://github.com/peacefulstudio/canton-ledger-api-csharp.git
   ```
3. Create a feature branch from `main`:
   ```bash
   git fetch upstream
   git checkout -b feat/<short-description> upstream/main
   ```
4. Commit using the [Conventional Commits](https://www.conventionalcommits.org/)
   format (`feat:`, `fix:`, `chore:`, `docs:`, `refactor:`, `test:`).
5. Push the branch to your fork and open a PR targeting `peacefulstudio/canton-ledger-api-csharp`'s `main`:
   ```bash
   git push -u origin feat/<short-description>
   gh pr create --repo peacefulstudio/canton-ledger-api-csharp --base main
   ```
6. Fill out the PR template — explicitly call out anything that affects
   public behaviour, schema, or state migration.
7. Make sure CI passes (build, tests, lint, coverage report).
8. Request review. A maintainer will respond — the maintainers may also
   request an automated reviewer (GitHub Copilot, or a Claude-based bot)
   on top of human review; treat its comments as suggestions rather than
   blockers.

(Maintainers with direct write access can skip the fork step and push
branches directly to `peacefulstudio/canton-ledger-api-csharp`; the rest of the
flow is identical.)

For user-visible changes, add an entry to the `[Unreleased]` section of
[`CHANGELOG.md`](CHANGELOG.md). Skip this for purely internal refactors,
test-only changes, CI tweaks, and dependency bumps that don't alter behaviour.

## Reporting bugs

Open an issue using the "Bug report" template. The more reproducible the
report, the faster the fix.

For security-sensitive bugs, **do not open a public issue** — see
[SECURITY.md](SECURITY.md).

## License

By contributing, you agree that your contributions will be licensed under the
[Apache License 2.0](LICENSE), the same license as the project. No CLA
required.
