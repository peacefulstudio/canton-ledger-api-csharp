# Canton.Ledger.Grpc.Client.Integration.Tests

End-to-end localnet integration tests proving that the published `Daml.*` C# packages round-trip a richly-typed Daml contract through a real Canton ledger — create, exercise, subscribe.

## Fixture

The Daml fixture lives in `testdata/richtypes/` and covers the full surface of the C# type mapping: records, variants, enums, `Optional`, `List`, `TextMap`, `Numeric`, `Party`, `Date`, `Time`, and nested `ContractId<T>`.

`Generated/` is committed so CI and local development compile without a JDK or dpm build step. The rich Daml source is in `testdata/richtypes/`.

## Rebuilding the DAR and regenerating C#

### Prerequisites

Install dpm >= 1.0.17 via the SHA-pinned GitHub-release tarball (not `get.daml.com` / `install.sh`, which gives 1.0.10). See the repo-level CI workflow for the pinned-tarball pattern.

Then install the required SDK:

```bash
dpm install 3.4.11
```

Do **not** use `dpm build --package-root`; dpm 1.0.17 rejects that flag for this layout. `cd` into the project directory instead:

```bash
cd testdata/richtypes && DPM_AUTO_INSTALL=true dpm build
```

### Local dev — proto-path stand-in

Use the codegen repo's pipeline script (requires the `daml-codegen-csharp` repo checked out locally):

```bash
scripts/codegen-pipeline.sh --dar testdata/richtypes/richtypes.dar --out Generated
```

### Real OCI path (Phase 3 / CI)

```bash
scripts/regen.sh <oci-version-tag>
```

This builds the DAR with `dpm build`, then runs `dpm codegen-cs` via the `oci://ghcr.io/peacefulstudio/dpm-codegen-cs:<tag>` OCI bundle, and writes the result to `Generated/`.

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
