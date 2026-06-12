# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

Covers: `Canton.Ledger.Grpc`, `Canton.Ledger.Grpc.Client`, `Canton.Ledger.Pqs.Client`, `Daml.Runtime.Grpc`, `Canton.Ledger.Auth` — versioned in lockstep.

## [Unreleased]

### Changed — BREAKING

- **Bumped `Daml.Ledger.Abstractions` and `Daml.Runtime` to `0.1.8-preview.2`** (public nuget.org releases of the runtime line) and adopted its typed submission surface (#103). `LedgerClient` follows the new `ILedgerClient` contract: the `string actAs` convenience overloads of `TryCreateAsync`, `TryExerciseAsync`, `TryExerciseForCreatedAsync`, `SubscribeAsync`, and `SubscribeActiveAsync` are gone — pass a `Party` (implicitly convertible to `SubmitterInfo`) or a full `SubmitterInfo` instead. `CommandsSubmission.CommandId`/`WorkflowId` are now the `CommandId`/`WorkflowId` value types and `ExerciseCommand` carries `ContractId`/`ChoiceName` value types, so a bare string can no longer be transposed into the wrong slot at a call site.
- **`DamlValueConverter.ToProtoValue` emits `Numeric` in canonical unpadded decimal form** (`Daml.Runtime.Grpc`), the commitment-grade wire shape per codegen ADR-0011: trailing zeros stripped, at least one fractional digit, never scientific notation — `1.50m` → `"1.5"`, `0m` → `"0.0"`, `42m` → `"42.0"`. The gRPC wire encoder now agrees with the runtime's `DamlJsonSerializer`, so the wire shape no longer depends on the `decimal`'s construction scale (previously `0m` → `"0"`, `1.50m` → `"1.50"`).
- **Bumped `Peaceful.Extensions.Logging` `0.2.1-preview.1` → `0.2.1-preview.2`** (nuget.org).

### Added

- Add CI-verified platform support across the full OS × architecture matrix: every shipped package builds and passes the unit test suite on Linux, Windows, and macOS, on both amd64 and arm64 (matching `daml-codegen-csharp` [#314](https://github.com/peacefulstudio/daml-codegen-csharp-internal/pull/314)).
  Integration tests against Canton localnet continue to run on linux-amd64 only.

- **Package management methods on `IAdminClient`/`AdminClient`** (`Canton.Ledger.Grpc.Client`, #7). `ListKnownPackagesAsync()` returns every Daml-LF package known to the participant as a new `PackageDetails` record (id, name, version, size as `long`, known-since); `GetPackageAsync(packageId)` downloads a package as a new `PackageArchive` record carrying the `daml_lf` archive payload together with its hash and hash function, and propagates `RpcException` (`NotFound`) for unknown package IDs; `ListVettedPackagesAsync(packageNamePrefixes)` returns the vetted-package topology state flattened into a new `VettedPackage` record (package id/name/version plus the participant and synchronizer it is vetted on), optionally filtered by package-name prefixes, transparently following server pagination to return the complete result set, and throwing `InvalidOperationException` if the server echoes a page token without progressing; `UploadDarAsync(darFile, submissionId)` uploads a DAR to the participant — note the ledger's default `vetting_change` is `VETTING_CHANGE_VET_ALL_PACKAGES`, so the upload also vets all packages in the DAR; `ValidateDarAsync(darFile)` runs the same checks without persisting or vetting anything and throws `RpcException` on validation failure. DAR bytes and package IDs are argument-validated up front (`ArgumentNullException`/`ArgumentException`). Backed by the existing generated `PackageManagementService` and `PackageService` gRPC stubs, with the established auth-header, deadline, and OpenTelemetry activity conventions. Non-breaking for `IAdminClient` consumers; implementors of the interface must add the five methods.

- **Live round-trip coverage for variants and non-default-scale numerics** (`Canton.Ledger.Grpc.Client.Integration.Tests`). The `richtypes` fixture DAR now carries an `Outcome` variant — including the `Win` record-payload case (`prize : Numeric 2; tier : Text`) — and a `fee : Numeric 2` field; `RichTypesRoundTripTests` creates and reads both back through Canton localnet, gating the ADR-0011 numeric wire form and the ADR-0012 variant round-trip end to end. Generated fixtures regenerated with `dpm-codegen-cs:0.1.8-preview.1`.

- **`TransactionResult.ExerciseResult<TReturn>(choiceName)` and `AllExerciseResults<TReturn>(choiceName)` extension methods** (`Canton.Ledger.Grpc.Client`). They locate exercised events by ordinal choice name in `TransactionResult.ExercisedEvents` and deserialize each `ExerciseResult` through the existing `Daml.Runtime` `FromDamlValue<T>` path, giving codegen consumers a typed surface for non-CID choice results (e.g. `choice GetTrailingTwap : Decimal`). `ExerciseResult<TReturn>` mirrors the `Single<T>()` cardinality contract — it throws `InvalidOperationException` on zero or more than one match; `AllExerciseResults<TReturn>` returns every match in transaction order (empty list when none). Both accept the choice as `string` or as the `Daml.Runtime` `ChoiceName` value type, so `ExerciseCommand.Choice` flows through without unwrapping. Non-breaking addition.

### Changed

- **`LedgerClient.TryExerciseAsync<TResult>` and `TrySubmitAndWaitForTransactionAsync` now share a single internal submit path** (`Canton.Ledger.Grpc.Client`). Both route through one private `TrySubmitCoreAsync` that owns command building, header/deadline resolution, the gRPC call, response projection, and the `RpcException` → `DamlError`/`InfraError` mapping; only the `TransactionFormat` differs (`TryExerciseAsync` requests `LedgerEffects` + verbose, the plain submit path keeps the server-default `AcsDelta`). `TryExerciseAsync<TResult>` now unpacks its typed value via `TransactionResult.ExerciseResult<TResult>(choiceName)` instead of walking the raw protobuf. Consequences for `TryExerciseAsync` callers: the exercised event is now located by choice name only (no longer also keyed on the command's contract id), so a transaction containing more than one exercised event with that choice name — e.g. a choice whose body re-exercises the same choice on a child contract — now throws `InvalidOperationException` (the cardinality contract of `ExerciseResult<TResult>`) where the contract-id-keyed lookup previously disambiguated; the "no exercised event" not-found error message changed wording; and a choice that returns no `ExerciseResult` is normalized to `Unit` and yields `One` (previously threw). Caller-requested cancellation still propagates from `TryExerciseAsync` as before. `RpcException` failures are now logged on the shared path for both methods.
- **Bumped runtime transitive deps.** `Google.Protobuf` 3.35.0 → 3.35.1 (matches `daml-codegen-csharp` 0.1.8-preview.2) and all `Microsoft.Extensions.*` runtime packages (incl. `Http`) 10.0.8 → 10.0.9. No API or behaviour change; the package floors consumers inherit move up accordingly.
- **`Daml.Ledger.Abstractions` and `Daml.Runtime` now resolve from public nuget.org instead of the `peacefulstudio` GitHub Packages feed.** No remaining dependency maps to the GitHub feed, so `dotnet restore` works without `GITHUB_USERNAME`/`GITHUB_TOKEN` (verified with a cold package cache); the `peaceful` source stays configured for future internal packages.
- **`Peaceful.Extensions.Logging` now resolves from public nuget.org instead of the `peacefulstudio` GitHub Packages feed, bumped `0.2.0` → `0.2.1-preview.1`.** Consumers no longer need GitHub Packages authentication to restore this transitive dependency — only the still-private `Daml.*` packages keep the `peaceful` feed. The `NuGet.config` source mapping routes `Peaceful.Extensions.*` to nuget.org (the broader `Peaceful.*` pattern stays on the GitHub feed for any internal Peaceful packages).

## [0.1.4] - 2026-06-04

### Added

- **`LedgerClient` and `AdminClient` now log a `Warning` at construction time when running in unauthenticated mode** (`ITokenProvider.None`). Each message names the concrete step to take — register an `ITokenProvider` or use the matching `AddLedgerClient`/`AddAdminClient` overload that accepts `authConfiguration` — so misconfigured apps surface the oversight immediately instead of failing with an opaque gRPC `UNAUTHENTICATED` error at the first API call.

- **Integration test project `Canton.Ledger.Grpc.Client.Integration.Tests`** — end-to-end round-trip of a rich Daml template (Int/Numeric/Text/Bool/Date/Party/Optional/List/nested Record + one choice) through a real Canton participant on `canton-localnet-internal`, proving the published `Daml.Runtime` + generated C# + gRPC `LedgerClient` create/subscribe/exercise path. Self-skips without a live localnet.

- **`ILedgerClient.TryExerciseAsync<TResult>`** — structured-outcome exercise overload required by the new `Daml.Ledger.Abstractions` 0.1.7 interface contract. `LedgerClient` implements this method; any other `ILedgerClient` implementation must add it.

### Fixed

- **`DamlValueConverter.ToProtoTemplateNameIdentifier` now throws `ArgumentException` on an empty package name** instead of silently emitting the package-id hash (#92). The hash form is rejected by Canton's ACS/update filter endpoints (`Invalid field packageId: ... expected a package name`), so the previous soft fallback re-introduced the exact failure it was meant to avoid — surfacing as a cryptic runtime gRPC error mid-stream; the method never falls back to the hash on the stream-filter path. `SubscribeAsync<T>` / `SubscribeActiveAsync<T>` compute the filter identifier eagerly, so the throw now fails fast at the subscribe call rather than on the first `await foreach`. The message names the offending template's module, entity, and package-id hash so an operator can trace it back to the DAR. Stream-filter path only; the command (create/exercise) path is unaffected and continues to use the hash via `ToProtoIdentifier`.

- **`SubscribeAsync<T>` and `SubscribeActiveAsync<T>` now reference templates by package name in their read-path filters.** Both stream filters previously sent the package hash in the `TemplateId.package_id` field; Canton's ACS/update filter endpoints reject that with `InvalidArgument: Invalid field packageId: ... expected a package name`, so every subscription failed against a real ledger. The filter now sends `#<T.PackageName>` (the smart-contract-upgrade package-name reference). The command path (create/exercise) is unchanged and continues to use the hash.

- `LedgerClient` and `AdminClient` no longer dispose the shared static `ActivitySource` when an instance is disposed. Previously, the first instance's `Dispose()` silently disabled tracing — `StartActivity` returned `null` on every subsequent instance, so OpenTelemetry (or any other `ActivityListener`) saw no further spans, with no warning or exception. Affects any process that disposes a `LedgerClient`/`AdminClient` and then constructs another.

### Removed — BREAKING for `ILedgerClient` implementors

- **`ExerciseAsync` overloads removed from `ILedgerClient`** (upstream `Daml.Ledger.Abstractions` 0.1.7). Any class that directly implements `ILedgerClient` must remove these methods from the interface implementation. *Callers* of `ILedgerClient.ExerciseAsync` are unaffected — the methods now live as extension methods on `LedgerClientExtensions` and resolve without source changes.

### Changed

- **Bumped `Daml.Ledger.Abstractions` and `Daml.Runtime` to `0.1.7`.** Picks up the `ILedgerClient` surface change described above (removed `ExerciseAsync`, added `TryExerciseAsync<TResult>`).

- **Bumped runtime transitive deps.** `Grpc.Net.Client` 2.76.0 → 2.80.0 (aligns with the `Grpc.Tools` 2.80.0 already pinned), `Google.Protobuf` 3.34.1 → 3.35.0 (matches `daml-codegen-csharp`), and all `Microsoft.Extensions.*` runtime packages (incl. `Http`) 10.0.7 → 10.0.8. No API or behaviour change; the package floors consumers inherit move up accordingly.
- **Test stack migrated to xUnit v3 + Microsoft.Testing.Platform coverage.** `xunit 2.9.3` + `xunit.runner.visualstudio 3.1.5` + `coverlet.collector 10.0.1` replaced by `xunit.v3 3.2.2` + `Microsoft.Testing.Extensions.CodeCoverage 18.0.6`. Test projects opt into the MTP runner via `tests/Directory.Build.props`, producing per-project `$(MSBuildProjectName).cobertura.xml` outputs — sidesteps the `XPlat Code Coverage` data-collector race that previously produced single-assembly coverage reports under solution-level parallelism. `coverlet.runsettings` (Coverlet/VSTest schema) replaced by `coverage.settings.xml` (Microsoft `<CodeCoverage>` schema); the scope changes from an explicit Include allowlist of four source assemblies to an exclude-only design (excludes `*.Tests.dll` and the generated-stub `Canton.Ledger.Grpc.dll`). The four source libraries are still the only ones that get instrumented today because they are the only non-test, non-generated assemblies in the build — a future `src/` addition will land in coverage by default unless explicitly excluded. CI workflow inlined (no longer calls the shared `csharp-ci.yaml@v1`) pending fleet-wide rollout — tracked in peacefulstudio/github-helper#95; the PR-comment renderer is `irongut/CodeCoverageSummary` (matching the `go-ci` reusable and the other C# repos) over the merged cobertura produced by `dotnet-coverage merge`.

## [0.1.3] - 2026-05-04

### Added

- **Multi-party submitter overloads** on `Canton.Ledger.Grpc.Client.LedgerClient` for `ExerciseAsync`, `TryCreateAsync`, `TryExerciseForCreatedAsync`, `SubscribeAsync`, and `SubscribeActiveAsync`. Each method now also accepts a `Daml.Runtime.Commands.SubmitterInfo`, populating `Commands.act_as` / `Commands.read_as` (and, on subscriptions, `EventFormat.FiltersByParty` for every `actAs` and `readAs` party). The existing `string actAs` overloads delegate to the new path via the `string -> SubmitterInfo` implicit conversion, so single-party callers keep working unchanged.
- **`TransactionResult.ExercisedEvents`** is now populated from `Canton.Ledger.Grpc.Client`. Each `Exercised` event in the gRPC `Transaction` is projected to `Daml.Runtime.Contracts.ExercisedEvent` carrying `ContractId`, `TemplateId`, optional `InterfaceId`, `ChoiceName`, `ChoiceArgument`, `ExerciseResult`, `Consuming`, `ActingParties`, and `WitnessParties`. Required by the typed non-CID exerciser wrappers emitted by `Daml.Codegen.Csharp` 0.1.5+.

### Changed

- **Bumped `Daml.Runtime` and `Daml.Ledger.Abstractions` to `0.1.5`.** Picks up the typed submission surface (`SubmitterInfo`, `IDamlType`, interface-marker exercisers), typed choice-result projection, and the post-create `ExercisedEvents` slot on `TransactionResult`. See the `daml-codegen-csharp` 0.1.5 release notes for the upstream surface.
- **BREAKING — `ContractStreamEvent<T>.WitnessParties` is now `IReadOnlyList<Party>`** (was `IReadOnlyList<string>`). The gRPC bridge in `Canton.Ledger.Grpc.Client` now wraps the wire-format party strings into `Daml.Runtime.Data.Party` before constructing each event variant. Consumers reading `WitnessParties[i]` as a string need `.Id` (or rely on the implicit `Party -> string` conversion). Sourced from upstream `Daml.Runtime` 0.1.5 (#88).
- **BREAKING — `ContractStreamEvent<T>.Assigned.Source` / `Target` and `Unassigned.Source` / `Target` are now `Daml.Runtime.Data.SynchronizerId`** (were `string`). Tolerant of the 3.4 (`name::fingerprint`) → 3.5 (`name::fingerprint::protocol-version`) wire-format change. Wrap incoming wire strings with `new SynchronizerId(s)`; round-trip back through `ToString()`. Sourced from upstream `Daml.Runtime` 0.1.5 (#87, #89).

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

[Unreleased]: https://github.com/peacefulstudio/canton-ledger-api-csharp/compare/v0.1.4...HEAD
[0.1.4]: https://github.com/peacefulstudio/canton-ledger-api-csharp/compare/v0.1.3...v0.1.4
[0.1.3]: https://github.com/peacefulstudio/canton-ledger-api-csharp/compare/v0.1.2...v0.1.3
[0.1.2]: https://github.com/peacefulstudio/canton-ledger-api-csharp/compare/v0.1.1...v0.1.2
[0.1.1]: https://github.com/peacefulstudio/canton-ledger-api-csharp/compare/v0.1.0-preview.2...v0.1.1
