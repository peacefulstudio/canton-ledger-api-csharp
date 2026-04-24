# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

Covers: `Canton.Ledger.Grpc`, `Canton.Ledger.Grpc.Client`, `Canton.Ledger.Pqs.Client`, `Daml.Runtime.Grpc`, `Canton.Ledger.Auth` — versioned in lockstep.

## [Unreleased]

## [0.1.1] - 2026-04-24

### Added

- `ServiceCollectionExtensions.AddCantonLedger(IConfiguration)` convention-based registration in `Canton.Ledger.Grpc.Client`. Reads `Canton:Ledger` for client options and `Canton:Auth` for client-credentials auth; auth is registered whenever the `Canton:Auth` section has any populated value, so half-configured auth fails loudly at startup instead of silently falling back to unauthenticated. Consumers can replace bespoke `AddLedgerClient`/`AddAdminClient`/`AddCantonAuth` wiring with a single call that matches the canonical config layout used by Helm charts. (#42)
- `ClientCredentialsOptions.Domain` now accepts a bare hostname (e.g. `dev-peaceful.eu.auth0.com`) in addition to an absolute http/https URL. For bare hostnames, the token endpoint is `https://{Domain}/oauth/token`; for absolute URLs, the provided scheme and any path are preserved and `/oauth/token` is appended (`https://auth.example.com/tenant-a` → `https://auth.example.com/tenant-a/oauth/token`). (#42)

### Changed

- Pin `Daml.Runtime` peer dependency to stable `0.1.2` (was previously a `0.1.2-dev.*` prerelease). First consumption of a stable `Daml.Runtime` release from this repo. (#24)
- Bump `Peaceful.Extensions.Logging` to `0.1.1` (from `0.1.0-dev.1.a1959c8`) — first consumption of a stable `Peaceful.Extensions` release. (#44)
- Bump `Npgsql` to `10.0.2` (was `9.0.3`). Used internally by `Canton.Ledger.Pqs.Client`; no public API change, but consumers that pin `Npgsql` directly should align their floor to `10.x`.
- Bump `Microsoft.Extensions.*` to `10.0.7`, `Grpc.Tools` to `2.80.0`, `Microsoft.Extensions.TimeProvider.Testing` to `10.5.0`, `Microsoft.NET.Test.Sdk` to `18.4.0`, `coverlet.collector` to `10.0.0`.
- `ClientCredentialsOptions.Domain` now rejects values with userinfo, query strings, fragments, or a path ending in `/oauth/token` (use `TokenEndpoint` for such cases). Validation error wording updated to mention bare hostnames. (#42)

### Fixed

- CI prerelease version strings now use dot-separated SemVer 2.0 identifiers (`${BASE}-${BRANCH}.${RUN}.${SHA}`) so `run_number` compares numerically; prevents `NU1605` downgrade warnings when consuming prereleases. (#24)

[Unreleased]: https://github.com/peacefulstudio/canton-ledger-api-csharp/compare/v0.1.1...HEAD
[0.1.1]: https://github.com/peacefulstudio/canton-ledger-api-csharp/compare/v0.1.0-preview.2...v0.1.1
