# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

Covers: `Canton.Ledger.Grpc`, `Canton.Ledger.Grpc.Client`, `Canton.Ledger.Pqs.Client`, `Daml.Runtime.Grpc`, `Canton.Ledger.Auth` — versioned in lockstep.

## [Unreleased]

## [0.1.2] - 2026-05-01

### Added

- **Typed subscription streams** in `Canton.Ledger.Grpc.Client`: `ILedgerClient.SubscribeAsync<T>(actAs, fromOffset, ct)` and `ILedgerClient.SubscribeActiveAsync<T>(actAs, ct)` wrap `UpdateService.GetUpdates` and `StateService.GetActiveContracts` with template-id filtering and project results to a typed `ContractStreamEvent<T>` discriminated record (`Created` with full payload + offset, `Archived`, `Exercised` with choice argument/result, `StreamError` for in-band gRPC failure). `IAsyncEnumerable<>` honours consumer backpressure; cancellation tears down the gRPC stream cleanly; mid-stream gRPC errors surface as a `StreamError` event rather than throwing — mirroring the outcome-typing discipline of `ExerciseOutcome<T>`. Caller-driven offset checkpointing only — the library does not persist `fromOffset`. (#55)
- **`ContractStreamEvent<T>.Checkpoint(long Offset)`** surfaces participant-emitted offset checkpoints from `UpdateService.GetUpdates`. Canton emits these on a participant-configured cadence regardless of the active filter; persisting `Checkpoint.Offset` lets a low-traffic subscription advance its resume offset during quiet periods, avoiding the re-process-from-stale-offset failure mode after a crash. (#55)
- **`ContractStreamEvent<T>.Assigned` / `Unassigned`** surface reassignment events on `SubscribeAsync<T>`, filtered by `T.TemplateId` the same way as `Created`/`Archived`. Each carries the on-ledger contract ID, the source/target synchronizer IDs, and the offset. `Assigned` additionally carries the re-emitted `CreateArguments` payload so consumers rebuilding state from a single stream stay correct on the target synchronizer. (#55)
- **`SubscribeActiveAsync<T>` includes in-flight reassignment entries** (`IncompleteAssigned`, `IncompleteUnassigned` from `StateService.GetActiveContracts`). These carry create-arguments for contracts that are mid-reassignment in multi-synchronizer deployments and are required for a complete ACS view. Previously they were silently dropped, yielding an under-reported snapshot. (#55)
- **`ILedgerClient.GetLedgerEndAsync(CancellationToken)`** returns the participant's current ledger-end offset. Compose with `SubscribeActiveAsync<T>` to snapshot-then-subscribe: capture the offset before draining the snapshot, then call `SubscribeAsync<T>` with that offset to stay current without missing or duplicating events. (#55)

  Cross-track follow-ups (TODO):
  - Broaden the `where T : ITemplate` constraint to `IDamlType` once `feat/interface-markers` lands on `dev`, so the streams can also dispatch on interface IDs computed by participant-side interface views.
  - Replace the `string actAs` parameter with the multi-party `SubmitterInfo` type once `feat/multi-party-submitters` (#56) ships.

- **Outcome-typed exercise API** in `Canton.Ledger.Grpc.Client`: `TrySubmitAndWaitForTransactionAsync`, `TryCreateAsync<X>`, and `TryExerciseForCreatedAsync<X>` return a single discriminated `ExerciseOutcome<T>` record instead of throwing. The outcome variants — `One`, `None`, `Many`, `DamlError`, `InfraError` — distinguish success (carrying a `T` payload), structured Canton/Daml errors (`DamlError` with a `DamlErrorCategory` enum, error ID, message, and `ErrorInfo.metadata`), and infrastructure failures (`InfraError`). Errors are decoded from the gRPC `grpc-status-details-bin` trailer's rich error model; missing/unparseable trailers fall back to `DamlErrorCategory.Unknown` without losing information. Lets consumers `switch` on outcomes instead of catching exceptions and parsing trailers manually. (#47)
- `ExerciseOutcome<T>` is unconstrained on `T`: success can carry a typed `ContractId<X>`, a raw `TransactionResult`, a composite Daml choice-result record, or any scalar — whatever shape the caller's API surface needs. Template-typed projection out of a transaction lives on `TransactionResultExtensions` (`Single<X>()`, `TrySingle<X>()`, `All<X>()` extension methods over `TransactionResult`, each constrained `where X : ITemplate`). (#47, #48)

### Changed

- **Lifted to upstream `Daml.Runtime` 0.1.4**: types `ExerciseOutcome<T>` (now in `Daml.Runtime.Outcomes` with variants renamed to `One` / `None` / `Many`), `DamlErrorCategory` (in `Daml.Runtime.Outcomes`), `TransactionResult` / `CreatedContract` / `TransactionResultExtensions` (in `Daml.Runtime.Contracts`), and `ContractStreamEvent<T>` (in `Daml.Runtime.Streams`, formerly canton's `ContractEvent<T>`). Local copies removed; canton consumes the upstream versions via `<PackageVersion Include="Daml.Runtime" Version="0.1.4" />`. The `ExerciseOutcome<T>.InfraError.StatusCode` field is now `int` (was `Grpc.Core.StatusCode`) so the type stays free of any transport-library dep; cast back to the typed enum when needed.
- **`ILedgerClient` interface lifted to `Daml.Ledger.Abstractions` 0.1.4**. Canton's `LedgerClient` now implements `Daml.Ledger.Abstractions.ILedgerClient` from upstream. Add `<PackageVersion Include="Daml.Ledger.Abstractions" Version="0.1.4" />`.
- **BREAKING:** `CreatedContract.TemplateId` is now `Daml.Runtime.Data.Identifier` (was `string`). Surfaces the parsed package/module/entity directly so `TransactionResult.Single<T>()` etc. can match by qualified name. (#47)

### Removed — BREAKING

- Deprecated throwing methods `ILedgerClient.CreateAsync<T>` and `ILedgerClient.SubmitAndWaitForTransactionAsync` (long `[Obsolete]`, see issue #47) are no longer part of the abstraction or canton's `LedgerClient`. Migrate to `TryCreateAsync` / `TrySubmitAndWaitForTransactionAsync` which return `ExerciseOutcome<T>` and let callers `switch` on the outcome instead of catching exceptions.

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

[Unreleased]: https://github.com/peacefulstudio/canton-ledger-api-csharp/compare/v0.1.2...HEAD
[0.1.2]: https://github.com/peacefulstudio/canton-ledger-api-csharp/compare/v0.1.1...v0.1.2
[0.1.1]: https://github.com/peacefulstudio/canton-ledger-api-csharp/compare/v0.1.0-preview.2...v0.1.1
