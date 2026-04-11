# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

Covers: `Canton.Ledger.Grpc`, `Canton.Ledger.Grpc.Client`, `Canton.Ledger.Pqs.Client`, `Daml.Runtime.Grpc`, `Canton.Ledger.Auth` — versioned in lockstep.

## [Unreleased]

### Changed

- Bump `Daml.Runtime` peer dependency to `0.1.2-dev.11.c61a066`. (#24)

### Fixed

- CI prerelease version strings now use dot-separated SemVer 2.0 identifiers (`${BASE}-${BRANCH}.${RUN}.${SHA}`) so `run_number` compares numerically; prevents `NU1605` downgrade warnings when consuming prereleases. (#24)

[Unreleased]: https://github.com/peacefulstudio/canton-ledger-api-csharp/compare/dev...HEAD
