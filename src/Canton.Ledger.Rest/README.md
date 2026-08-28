# Canton.Ledger.Rest

The raw, Refit-generated client surface over the Canton JSON Ledger API (`/v2/...`) — one interface per Ledger API service, plus a few hand-authored off-spec endpoints.

> **Experimental.** Every interface in this package is annotated `[Experimental("CANTONREST001")]`. Consuming the raw surface directly requires opting into that diagnostic. Most applications should use the supported, transport-neutral adapter in **`Canton.Ledger.Rest.Client`** (`RestLedgerClient` / `AddRestLedgerClient`) instead — it implements `Daml.Ledger.Abstractions.ILedgerClient` and `Canton.Ledger.Abstractions.ICantonLedgerClient` on top of these interfaces.

Reach for this package only when you need an endpoint the adapter does not surface. Register the raw surface with `AddRestLedgerRawApis(...)` from `Canton.Ledger.Rest.Client`.

## Related Packages

- `Canton.Ledger.Rest.Client` — the supported adapter over this surface (start here)
- `Canton.Ledger.Abstractions` — the transport-neutral Canton participant contracts

## Spec supply-chain provenance

The committed `spec/openapi.yaml` these interfaces are generated from is derived from pristine
published protos plus the vendored patch set described below, with every link pinned. The
fork is advocacy-only and never enters the build.

```
(CantonVersion protos × spec/patches/ × buf) → spec/openapi.yaml → (spec/openapi.yaml × Refitter) → .g.cs
```

### Patch export source

| | |
|---|---|
| Fork | [`peacefulstudio/canton`](https://github.com/peacefulstudio/canton) (advocacy-only; never fetched by any build) |
| Branch | `experiment/google-api-http-full-coverage` ([PR peacefulstudio/canton#4](https://github.com/peacefulstudio/canton/pull/4)) |
| Commit | `472b36a787866095617da776b6980f580f9e5ab1` |
| Rebase base | upstream `digital-asset/canton` `release-line-3.5` @ `1f9a483455307cfb7875ff120ff803500663eeac` |

#### Re-vendor from the `release-line-3.5` branch, not from the `vX.Y.Z` tag

Upstream's public git tag `v3.5.9` does **not** correspond to the Maven `3.5.9` artifacts — its
proto tree differs in 10 files and is *ahead* of the release (it contains, for example,
`GetUpdateByHash`, documented as available only after Canton 3.6). Rebasing the fork onto the tag
produces patches that fail to apply. The commit whose proto tree is byte-identical to Maven
`3.5.9` is `release-line-3.5` @ `1f9a4834`. Always verify a candidate base by hashing the
extracted Maven protos against the upstream tree before exporting patches.

### Pristine inputs

Exactly the artifacts `src/Canton.Ledger.Grpc/DownloadProtos.targets` already fetches,
SHA-256-pinned there:

- `com.daml:ledger-api-proto:3.5.9` (`$(CantonVersion)`)
- `com.daml:ledger-api-value-proto:3.5.9`
- `com.google.api.grpc:proto-google-common-protos:2.58.0`

### Patch set

34 per-file patches under `spec/patches/`, mirroring the extracted proto layout. Zero new RPCs;
the content is of two kinds.

**Transport metadata.** `option go_package` on all 34 files, `import "google/api/annotations.proto"`
plus `option (google.api.http)` on the 14 service protos — 52 annotated RPCs.

**Wire-shape corrections.** This spec is derived from the protos, while the JSON Ledger API is
served from a separate tapir stack, and the two disagree on how a `oneof` is encoded: protobuf
flattens it into sibling fields, the served document nests it one level down as a single-key
object. Fifteen sites are re-nested by lifting each `oneof` into a message of its own and holding
that message in a single field. That adds ten wrapper messages — `CompletionResponse`,
`ContractEntry`, `DeduplicationPeriod`, `IdentifierFilter`, `PriorTopologySerialSerial`,
`ReassignmentCommandCommand`, `RightKind`, `TopologyEventEvent`, `Update` and
`VettedPackagesChangeOperation` — and re-declares two more, `CumulativeFilter` and
`ReassignmentCommand`, around their new wrapper field. Explicit `json_name` options pin every
affected key to the served spelling. No arm is added, removed or retyped — the wrapper messages
exist only to carry the nesting level the served document has and the protos do not.

Routes match the JSON Ledger API's own with **exactly one known verb deviation**:
`StateService.GetActiveContractsPage` is annotated `POST /v2/state/active-contracts-page`,
whereas Canton's JSON Ledger API exposes that route as `GET`.

`UpdateService.GetUpdatesPage` and `StateService.GetActiveContractsPage` were 3.5-era upstream
additions the earlier 3.4 pin could not serve, so their annotations were held back on the fork.
Both are in the pin as of `3.5.9` and are now patched and generated like every other RPC.

Seven of the 59 RPCs in the pinned protos are deliberately left unannotated, which is why
coverage is 52 rather than 59: `GetCommandStatus`, `GetCompletions`, `GetTime`,
`ListKnownPackages`, `Prune`, `SetTime`, and `UpdatePartyIdentityProviderId`.

A patch that fails to apply after a `$(CantonVersion)` bump is the upstream-drift alarm:
re-export from a rebased fork branch, never hand-edit `spec/openapi.yaml`.

Patches apply to a temporary copy of the extracted protos (`git apply` from the copy
root); `src/Canton.Ledger.Grpc/Proto/` always stays pristine.

### Deprecation prose in these descriptions is unreliable

Descriptions in `spec/openapi.yaml` are inherited verbatim from the pinned protos, and the
patch set never edits prose. Where a description names the version something will be removed
in, treat it as upstream's stale intent rather than a schedule — measured against a live
`3.5.9` participant, it is wrong in both spec sources.

`GetPreferredPackageVersion` carries *"Provided for backwards compatibility, it will be
removed in the Canton version 3.4.0"*, so the generated `<remarks>` on that endpoint repeats
it. A `3.5.9` participant still routes `GET /v2/interactive-submission/preferred-package-version`
— it answers `400` to a malformed query, where a route it does not have answers `404` — and
its own served document redates the removal to Canton `3.6`.

Canton's tapir-generated served document, a separate artifact this package is not built from,
has the same defect independently: its `3.5.9` copy repeats *"...removed in the Canton version
3.5.0."* at 16 places, including the `filter` and `verbose` fields of
`GetActiveContractsRequest`. Two `GET` routes carrying that sentence,
`/v2/updates/transaction-tree-by-offset/{offset}` and `/v2/updates/transaction-tree-by-id/{update-id}`,
still answer `401` rather than `404` on `3.5.9`. Neither `filter` nor `verbose` exists in the
vendored spec's `GetActiveContractsRequest`, so nothing generated here has ever sent them.

Correcting either text means correcting it upstream; both are recorded as evidence for
[digital-asset/canton#527](https://github.com/digital-asset/canton/issues/527).

### Generation

From the patched temp copy root, with `buf.yaml` and `buf.gen.yaml` from `spec/`
alongside it:

```
buf generate   # buf CLI 1.69.0 used for this export; plugin pin lives in spec/buf.gen.yaml
```

`spec/buf.gen.yaml` declares `version=@CantonVersion@` rather than a literal. The placeholder
is substituted into the temp copy from `$(CantonVersion)` — the single authored Canton pin, in
`Directory.Build.props` — so the generated `info.version` tracks the pin and cannot drift from
it; `scripts/regen-rest-client.sh` refuses to continue if the two disagree. Running `buf
generate` by hand against an unsubstituted `buf.gen.yaml` emits the placeholder verbatim.

Plugin: `buf.build/community/google-gnostic-openapi:v0.7.0`. The output
lands at `gen/openapi.yaml` in the temp copy and is committed as `spec/openapi.yaml`.
Spec surface: OpenAPI 3.0.3, 45 paths / 52 operations / 14 services, with `info.version`
equal to `$(CantonVersion)`.
Verified 1:1 against the annotated RPC set (every annotated RPC has exactly one
operation, and vice versa).

That correspondence is pinned **by name, not by count**, in
`tests/Canton.Ledger.Rest.Client.Tests/RestEndpointCoverageTests.cs`, whose
`PinnedSpecOperations` maps all 52 `operationId`s to their `VERB route` and asserts that
`spec/openapi.yaml` declares exactly those and that the raw Refit surface routes every one of
them plus the seven hand-authored off-spec endpoints in `PinnedOffSpecEndpoints`. The raw
surface is compared per declaring interface **and method** —
`IPackageManagementServiceApi.UploadDarFile: POST /v2/dars`, not `POST /v2/dars` — because
`IDarApi`, `IPackageApi` and `IInteractiveSubmissionApi` re-declare off-spec routes the generated
surface also carries and an unqualified route set would let them stand in for a regeneration that
dropped them, and because two methods sharing one route on one
interface would otherwise collapse into a single entry and stand in for each other. The method
half is the `operationId` suffix, which Refitter emits verbatim as the C# method name, so an
upstream rename that changes no route fails the pin too and is answered by renaming the
`PinnedSpecOperations` key.
`rest-drift` cannot see a coverage loss —
a regeneration that silently drops an annotation is still byte-reproducible against itself —
so an annotation that stops applying fails there instead, naming the operation that vanished.
A deliberate coverage change updates `PinnedSpecOperations` and the counts above together.

Both links are scripted end to end by `scripts/regen-rest-client.sh`, which re-derives
`spec/openapi.yaml` from the pinned protos and the patch set and then regenerates
`Generated/CantonLedgerApi.g.cs` from it; its `--check` mode fails when either committed
artifact drifts from what the inputs produce, and CI runs that check on every pull request.

### Refitter namespace and experimental marker

`.refitter` sets `namespace` to `Canton.Ledger.Rest.Client.Raw`, so the regenerated
`Generated/CantonLedgerApi.g.cs` lands there and stays in lockstep with the hand-authored
off-spec interfaces. The raw Refit interfaces carry `[Experimental("CANTONREST001")]`
from the companion `Generated/RawApisExperimental.cs` — a set of `partial interface`
declarations that Refitter never overwrites — so regeneration re-applies neither the
namespace nor the attribute by hand.
