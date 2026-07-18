# Spec supply-chain provenance

Per [ADR 0012](../../../docs/adr/0012-vendored-patch-on-top-spec-supply-chain.md): the
committed `openapi.yaml` is derived from pristine published protos plus metadata-only
patches, with every link pinned. The fork is advocacy-only and never enters the build.

```
(CantonVersion protos × patches/ × buf) → openapi.yaml → (openapi.yaml × Refitter) → .g.cs
```

## Patch export source

| | |
|---|---|
| Fork | [`peacefulstudio/canton`](https://github.com/peacefulstudio/canton) (advocacy-only; never fetched by any build) |
| Branch | `experiment/google-api-http-full-coverage` ([PR #2](https://github.com/peacefulstudio/canton/pull/2)) |
| Commit | `aa5ec09db73e3026772a5ace55131d277d963576` (rebased onto fork `main` `f06f209`, [PR #1](https://github.com/peacefulstudio/canton/pull/1) evidence folded in) |

## Pristine inputs

Exactly the artifacts `src/Canton.Ledger.Grpc/DownloadProtos.targets` already fetches,
SHA-256-pinned there:

- `com.daml:ledger-api-proto:3.4.11` (`$(CantonVersion)`)
- `com.daml:ledger-api-value-proto:3.4.11`
- `com.google.api.grpc:proto-google-common-protos:2.58.0`

## Patch set

34 per-file patches under `patches/`, mirroring the extracted proto layout. Content is
metadata only: `option go_package` on all 34 files, `import "google/api/annotations.proto"`
plus `option (google.api.http)` on the 14 service protos — 50 annotated RPCs, zero new
RPCs/messages/fields, zero verb deviations from the JSON Ledger API's routes.

The fork branch annotates 52 RPCs; `UpdateService.GetUpdatesPage` and
`StateService.GetActiveContractsPage` are 3.5-era upstream additions absent from 3.4.11,
so their annotations stay on the fork until the version bump. A patch that fails to apply
after a `$(CantonVersion)` bump is the upstream-drift alarm: re-export from a rebased fork
branch, never hand-edit `openapi.yaml`.

Patches apply to a temporary copy of the extracted protos (`git apply` from the copy
root); `src/Canton.Ledger.Grpc/Proto/` always stays pristine.

## Generation

From the patched temp copy root, with `buf.yaml` and `buf.gen.yaml` from this directory
alongside it:

```
buf generate   # buf CLI 1.69.0 used for this export; plugin pin lives in buf.gen.yaml
```

Plugin: `buf.build/community/google-gnostic-openapi:v0.7.0` (ADR 0012). The output
lands at `gen/openapi.yaml` in the temp copy and is committed here as `openapi.yaml`.
Spec surface: OpenAPI 3.0.3, 43 paths / 50 operations / 14 services, `info.version` = 3.4.11.
Verified 1:1 against the annotated RPC set (every annotated RPC has exactly one
operation, and vice versa).

Regen script and drift-check wiring are #173's deliverable.
