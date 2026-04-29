# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

Covers: `Canton.Ledger.Grpc`, `Canton.Ledger.Grpc.Client`, `Canton.Ledger.Pqs.Client`, `Daml.Runtime.Grpc`, `Canton.Ledger.Auth` — versioned in lockstep.

## [Unreleased]

### Added

- **Outcome-typed exercise API** in `Canton.Ledger.Grpc.Client`: `TrySubmitAndWaitForTransactionAsync`, `TryCreateAsync<X>`, and `TryExerciseForCreatedAsync<X>` return a single discriminated `ExerciseOutcome<T>` record instead of throwing. The outcome variants — `Created`, `NoCreated`, `TooMany`, `DamlError`, `InfraError` — distinguish success (carrying a `T` payload), structured Canton/Daml errors (`DamlError` with a `DamlErrorCategory` enum, error ID, message, and `ErrorInfo.metadata`), and infrastructure failures (`InfraError` with `Grpc.Core.StatusCode`). Errors are decoded from the gRPC `grpc-status-details-bin` trailer's rich error model; missing/unparseable trailers fall back to `DamlErrorCategory.Unknown` without losing information. Lets consumers `switch` on outcomes instead of catching exceptions and parsing trailers manually. (#47)
- `ExerciseOutcome<T>` is unconstrained on `T`: success can carry a typed `ContractId<X>`, a raw `TransactionResult`, a composite Daml choice-result record, or any scalar — whatever shape the caller's API surface needs. Template-typed projection out of a transaction lives on `TransactionResultExtensions` (`Single<X>()`, `TrySingle<X>()`, `All<X>()` extension methods over `TransactionResult`, each constrained `where X : ITemplate`). (#47, #48)
- New types in `Canton.Ledger.Grpc.Client`: `ExerciseOutcome<T>`, `TransactionResultExtensions`, `DamlErrorCategory` (closed enum mirroring Canton 3.5 documented error categories with `Unknown` as the safe fallback). (#47)

### Changed

- **BREAKING:** `CreatedContract.TemplateId` is now `Daml.Runtime.Data.Identifier` (was `string`). Surfaces the parsed package/module/entity directly so `ExerciseTransactionOutcome.Success.Single<T>()` etc. can match by qualified name. (#47)

### Deprecated

- `ILedgerClient.SubmitAndWaitForTransactionAsync` and `ILedgerClient.CreateAsync<T>` are marked `[Obsolete]` in favour of `TrySubmitAndWaitForTransactionAsync` and `TryCreateAsync<T>`. The throwing API still works for one minor cycle and will be removed in the next major release. (#47)

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
