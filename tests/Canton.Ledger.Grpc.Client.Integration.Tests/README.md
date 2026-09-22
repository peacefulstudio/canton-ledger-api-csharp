# Canton.Ledger.Grpc.Client.Integration.Tests

End-to-end localnet integration tests proving that the published `Daml.*` C# packages round-trip a richly-typed Daml contract through a real Canton ledger — create, exercise, subscribe.

## Fixtures

No Daml fixture lives in this repo. Both fixtures the live lane uploads, `richtypes` and
`contractkeys`, ship prebuilt — DAR and generated C# both — inside the
`Daml.Codegen.Testing.Conformance` package: the generated types under
`Daml.Codegen.Testing.Conformance.RichTypes` / `.ContractKeys`, the DARs through
`ConformanceCorpus.OpenDar(ConformancePackage.RichTypes)` and `(ConformancePackage.ContractKeys)`.
`RichTypesDar` materializes the `richtypes` DAR beside the test assembly for the path-based upload
APIs, and the gRPC, REST and parity projects share it.

The only control point here is the package pin in `Directory.Packages.props`; bump that to take a new
corpus. This repo names no Daml SDK version and no Daml-LF target for either fixture — which target a
fixture is built at is decided upstream, in `daml-codegen-csharp`, and a request for a different one is
filed there.

## Running the tests

### 1. Build the `canton-localnet` CLI

Follow the instructions in the `canton-localnet` repo to build the Go CLI binary.

### 2. Bring up the localnet

```bash
canton-localnet up
canton-localnet wait-ready --timeout 10m --interval 5s
```

### 3. Set environment variables

| Variable | Purpose |
|----------|---------|
| `CANTON_LOCALNET_A_VALIDATOR_1_GRPC_URL` | gRPC endpoint (default: `http://localhost:11901`) |
| `CANTON_LOCALNET_A_VALIDATOR_1_JSON_API_URL` | JSON API base URL (availability check) |
| `CANTON_LOCALNET_A_VALIDATOR_1_CLIENT_ID` | OAuth2 client ID for token acquisition |
| `CANTON_LOCALNET_A_VALIDATOR_1_CLIENT_SECRET` | OAuth2 client secret |

Legacy un-namespaced `CANTON_LOCALNET_*` globals are also accepted as fallbacks.

### 4. Run

```bash
dotnet test tests/Canton.Ledger.Grpc.Client.Integration.Tests
```

## Party rights on a long-lived LocalNet

Every integration and parity lane allocates a fresh party and grants the validator user `CanActAs`
on it. A participant caps a user at 1000 rights by default and a party is never deletable, so those
grants have to be given back: each lane holds them in an `ActAsRightsLease`
(`tests/Canton.Ledger.Testing.Localnet`) and revokes them when the lane disposes, checking the
participant's own `newlyRevokedRights` so a confirmed grant that remained standing fails the run
rather than passing quietly. An ambiguous grant is still sent for cleanup and may already be absent.
Grant through the lease — `lane.GrantActAsAsync(...)` on the REST lane,
`actAsRights.GrantAsync(...)` elsewhere — rather than calling the fixture's `GrantUserRightsAsync`
directly, which the fixture gives no revoke for: the right would outlive the run, and a long-lived
LocalNet would fill up until every new grant failed with `TOO_MANY_USER_RIGHTS`.

A run killed before its teardown still leaves its rights behind, so a long-lived LocalNet can still
reach the cap. Recovering one is a manual revoke against that participant's validator user.
