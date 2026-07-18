#!/usr/bin/env bash
# Copyright 2026 Peaceful Studio OÜ
# SPDX-License-Identifier: Apache-2.0
set -euo pipefail
HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
DAR_DIR="${HERE}/testdata/richtypes"
OUT="${HERE}/Generated"
OCI_TAG="${1:?usage: regen.sh <oci-version-tag>}"
OCI_REPO="ghcr.io/peacefulstudio/dpm-codegen-cs"

# Resolve the mutable tag to an immutable digest so the codegen bundle pull is
# pinned — a GHCR tag overwrite cannot poison the generator output [audit F-55].
# Falls back to the bare tag with a warning if no OCI inspection tool is found.
OCI_REF=""
if command -v oras >/dev/null 2>&1; then
  OCI_DIGEST="$(oras manifest fetch --descriptor "${OCI_REPO}:${OCI_TAG}" \
    | sed -n 's/.*"digest":"\(sha256:[^"]*\)".*/\1/p')"
  [ -n "${OCI_DIGEST}" ] || { echo "ERROR: could not resolve digest for ${OCI_REPO}:${OCI_TAG} — check oras output format" >&2; exit 1; }
  OCI_REF="${OCI_REPO}@${OCI_DIGEST}"
  echo "Pinned ${OCI_REPO}:${OCI_TAG} → ${OCI_DIGEST}"
else
  OCI_REF="${OCI_REPO}:${OCI_TAG}"
  echo "WARNING: oras not found — pulling by mutable tag ${OCI_TAG}; install oras to pin by digest" >&2
fi

# Build the DAR with dpm (the daml assistant is deprecated; requires dpm >= 1.0.20 on PATH —
# the guard below refuses older dpm whose name-keyed component cache can serve a stale
# extraction; see the README for the pinned install). Pin the SDK component, delete any
# stale committed copy FIRST so any post-build *.dar is fresh (no name collision), then build
# and locate the produced DAR robustly across dpm output-path variants. NOTE: dpm 1.0.17
# rejects --package-root for this layout, so cd into the project dir before building.
export PATH="$HOME/.dpm/bin:$PATH"

# dpm < 1.0.20 keys its OCI component cache by name (not content digest), so a
# digest-pinned codegen pull can silently reuse a stale same-name extraction and
# emit pre-0.4.0 code — the digest-keyed pkg/cacheindex landed in dpm 1.0.20 (#338).
# Refuse to regen on a pre-cacheindex binary (complements the ILedgerWriter output
# marker below). The CLI version comes from `dpm --version`; the `dpm version`
# subcommand lists installed SDKs, not the dpm build.
DPM_MIN_VERSION="1.0.20"
dpm_version_raw="$(dpm --version 2>/dev/null || true)"
dpm_version="$(printf '%s\n' "${dpm_version_raw}" | sed -n 's/^version:[[:space:]]*\([0-9][0-9]*\.[0-9][0-9]*\.[0-9][0-9]*\).*/\1/p' | head -n1 || true)"
if [ -z "${dpm_version}" ]; then
  dpm_version="$(printf '%s\n' "${dpm_version_raw}" | grep -Eo '[0-9]+\.[0-9]+\.[0-9]+' | head -n1 || true)"
fi
if [ -z "${dpm_version}" ]; then
  echo "ERROR: could not determine the dpm version from 'dpm --version'; regen requires dpm >= ${DPM_MIN_VERSION} (digest-keyed component cache). See the README to install a pinned dpm." >&2
  exit 1
fi
if [ "$(printf '%s\n%s\n' "${DPM_MIN_VERSION}" "${dpm_version}" | sort -V | head -n1)" != "${DPM_MIN_VERSION}" ]; then
  echo "ERROR: dpm ${dpm_version} is below the required >= ${DPM_MIN_VERSION} — its name-keyed component cache can silently reuse a stale extraction and poison the fixtures. Upgrade dpm (see the README) before regenerating." >&2
  exit 1
fi
echo "Using dpm ${dpm_version} (>= ${DPM_MIN_VERSION})"

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

WORK="$(mktemp -d)"
GEN="$(mktemp -d)"
trap 'rm -rf "${WORK}" "${GEN}"' EXIT
cp "${DAR_DIR}/richtypes.dar" "${WORK}/fixture.dar"
cat > "${WORK}/daml.yaml" <<EOF
components:
  - "oci://${OCI_REF}"
EOF
( cd "${WORK}" && DPM_AUTO_INSTALL=true dpm codegen-cs --dar ./fixture.dar --out "${GEN}" )
find "${GEN}" -name '*.csproj' -delete

# Guardrail: dpm materialises OCI components
# under a name-keyed (not digest-keyed) cache path, so a stale extraction can be
# reused despite the digest pinned above and silently emit pre-0.4.0 code. Emit
# into a temp dir and refuse to publish output that predates the
# ILedgerReader/ILedgerWriter capability split, so a stale bundle fails loudly
# instead of poisoning the committed fixtures.
if ! grep -rql "ILedgerWriter" "${GEN}"; then
  echo "ERROR: regenerated output has no ILedgerWriter reference — the codegen bundle that ran predates the 0.4.0 capability split (likely a stale dpm component reused from cache). Refusing to overwrite ${OUT}." >&2
  exit 1
fi

rm -rf "${OUT}"
mv "${GEN}" "${OUT}"
echo "Regenerated ${OUT} from richtypes.dar via oci://${OCI_REF}"
