# Canton.Ledger.Grpc.Client.Integration.Tests

End-to-end localnet integration tests proving that the published `Daml.*` C# packages round-trip a richly-typed Daml contract through a real Canton ledger — create, exercise, subscribe.

## Fixture

The Daml fixture lives in `testdata/richtypes/` and covers the full surface of the C# type mapping: records, variants, enums, `Optional`, `List`, `TextMap`, `Numeric`, `Party`, `Date`, `Time`, and nested `ContractId<T>`.

`Generated/` is committed so CI and local development compile without a JDK or dpm build step. The rich Daml source is in `testdata/richtypes/`.

## Rebuilding the DAR and regenerating C#

### Prerequisites

Install **dpm >= 1.0.20**. The floor is 1.0.20 because that release keys the OCI component cache by content digest (`pkg/cacheindex`); older builds key it by name and can silently reuse a stale extraction, poisoning regen output. `regen.sh` enforces this floor and refuses to run on an older binary.

Pull the pinned `1.0.21` tarball from the upstream [`digital-asset/dpm` release](https://github.com/digital-asset/dpm/releases/tag/1.0.21) and verify it against the published `checksums.txt` — do **not** use `get.daml.com` / `install.sh`, which installs an old 1.0.10:

```bash
DPM_VERSION=1.0.21
# platform: darwin-arm64 | darwin-amd64 | linux-amd64 | linux-arm64
PLATFORM=darwin-arm64
BASE="https://github.com/digital-asset/dpm/releases/download/${DPM_VERSION}"

curl -fsSLO "${BASE}/dpm-${DPM_VERSION}-${PLATFORM}.tar.gz"
curl -fsSLO "${BASE}/dpm-${DPM_VERSION}-checksums.txt"
shasum -a 256 --ignore-missing -c "dpm-${DPM_VERSION}-checksums.txt"

mkdir -p "$HOME/.dpm/bin"
tar -xzf "dpm-${DPM_VERSION}-${PLATFORM}.tar.gz" -C "$HOME/.dpm/bin" dpm
export PATH="$HOME/.dpm/bin:$PATH"
```

Alternatively, use the upstream dpm installer with an **explicit version argument** (never its unpinned default) per the [Canton Network dpm docs](https://docs.canton.network/appdev/tooling/development-tools-overview#dpm-daml-package-manager).

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
