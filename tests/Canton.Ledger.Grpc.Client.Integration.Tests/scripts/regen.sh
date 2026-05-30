#!/usr/bin/env bash
# Copyright (c) 2026 Peaceful Studio OÜ
# SPDX-License-Identifier: Apache-2.0
set -euo pipefail
HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
DAR_DIR="${HERE}/testdata/richtypes"
OUT="${HERE}/Generated"
OCI_TAG="${1:?usage: regen.sh <oci-version-tag>}"

# Build the DAR with dpm (the daml assistant is deprecated; assumes dpm >= 1.0.17 on PATH —
# see the README / Task 1.2 for the SHA-pinned install). Pin the SDK component, delete any
# stale committed copy FIRST so any post-build *.dar is fresh (no name collision), then build
# and locate the produced DAR robustly across dpm output-path variants. NOTE: dpm 1.0.17
# rejects --package-root for this layout, so cd into the project dir before building.
export PATH="$HOME/.dpm/bin:$PATH"
dpm install 3.4.11
rm -f "${DAR_DIR}/richtypes.dar"
( cd "${DAR_DIR}" && DPM_AUTO_INSTALL=true dpm build )
shopt -s nullglob
produced=""
for f in "${DAR_DIR}"/.daml/dist/*.dar "${DAR_DIR}"/*.dar; do
  [ -f "$f" ] || continue
  produced="$f"; break
done
[ -n "$produced" ] || produced="$(find "${DAR_DIR}" -name '*.dar' -print -quit)"
[ -n "$produced" ] || { echo "dpm build produced no DAR under ${DAR_DIR}" >&2; exit 1; }
cp "$produced" "${DAR_DIR}/richtypes.dar"

WORK="$(mktemp -d)"; trap 'rm -rf "${WORK}"' EXIT
cp "${DAR_DIR}/richtypes.dar" "${WORK}/fixture.dar"
cat > "${WORK}/daml.yaml" <<EOF
components:
  - "oci://ghcr.io/peacefulstudio/dpm-codegen-cs:${OCI_TAG}"
EOF
rm -rf "${OUT}"; mkdir -p "${OUT}"
( cd "${WORK}" && DPM_AUTO_INSTALL=true dpm codegen-cs --dar ./fixture.dar --out "${OUT}" )
find "${OUT}" -name '*.csproj' -delete
echo "Regenerated ${OUT} from richtypes.dar via oci://…:${OCI_TAG}"
