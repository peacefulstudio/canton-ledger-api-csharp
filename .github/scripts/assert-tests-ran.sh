#!/usr/bin/env bash
# Copyright 2026 Peaceful Studio OÜ
# SPDX-License-Identifier: Apache-2.0

set -euo pipefail

EXIT_FLOOR=1
EXIT_BROKEN=2

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
script_path="${script_dir}/$(basename "${BASH_SOURCE[0]}")"

usage() {
  cat <<EOF
Usage: $(basename "${BASH_SOURCE[0]}") <test-log> <suite-label> <min-total> <max-skipped>
       $(basename "${BASH_SOURCE[0]}") --self-test

Fail when a test step did not actually exercise its suite. <min-total> is the
per-suite floor: the run must discover at least that many tests, so a suite that
silently collapses to a handful cannot read as green. <max-skipped> is the
per-suite skip budget: the run may skip at most that many tests, so one new skip
fails the lane whatever the suite's size.

  --self-test      prove the floor, the budget, the missing-summary and the empty-run guards still fire

Exit codes: 0 the suite ran at or above its floor and within its budget, ${EXIT_FLOOR} it did not, ${EXIT_BROKEN} the check could not run.
EOF
}

broken() {
  echo "::error::assert-tests-ran could not run: $*" >&2
  exit "${EXIT_BROKEN}"
}

assert_ran() {
  local log_file="$1" suite_label="$2" min_total="$3" max_skipped="$4"

  case "${min_total}" in
    '' | *[!0-9]*) broken "min-total must be a non-negative integer, got '${min_total}'." ;;
  esac
  [ "${min_total}" -ge 1 ] || broken "min-total must be at least 1, got '${min_total}' — a floor of 0 asserts nothing."

  case "${max_skipped}" in
    '' | *[!0-9]*) broken "max-skipped must be a non-negative integer, got '${max_skipped}'." ;;
  esac

  if [ ! -f "${log_file}" ]; then
    echo "::error::${suite_label}: no test log was written to ${log_file}" >&2
    return "${EXIT_FLOOR}"
  fi

  local summaries total succeeded skip_summaries skipped
  read -r summaries total succeeded skip_summaries skipped <<EOF
$(awk '
  # Microsoft.Testing.Platform keeps ANSI colour on when stdout is a pipe under
  # GitHub Actions, emitting "ESC[m  total: 12" — the reset lands before the
  # indent, so an anchored match on the raw bytes never fires.
  BEGIN { ansi = sprintf("%c", 27) "\\[[0-9;]*[A-Za-z]" }
  { gsub(ansi, ""); gsub(/\r/, "") }
  /^[[:space:]]*total:[[:space:]]*[0-9]+[[:space:]]*$/     { summaries++; total += $2 }
  /^[[:space:]]*succeeded:[[:space:]]*[0-9]+[[:space:]]*$/ { succeeded += $2 }
  /^[[:space:]]*skipped:[[:space:]]*[0-9]+[[:space:]]*$/   { skip_summaries++; skipped += $2 }
  END { print summaries + 0, total + 0, succeeded + 0, skip_summaries + 0, skipped + 0 }
' "${log_file}")
EOF

  if [ "${summaries}" -eq 0 ]; then
    echo "::error::${suite_label}: the test output carries no 'total:' summary line, so nothing shows the suite ran at all. An unrecognised runner option or a changed output format both look like this." >&2
    return "${EXIT_FLOOR}"
  fi

  if [ "${skip_summaries}" -ne "${summaries}" ]; then
    echo "::error::${suite_label}: the test output carries ${summaries} 'total:' summary line(s) but ${skip_summaries} 'skipped:' line(s), so the skip count cannot be held to the budget. A changed runner output format looks like this." >&2
    return "${EXIT_FLOOR}"
  fi

  if [ "${total}" -eq 0 ]; then
    echo "::error::${suite_label}: the runner reported 'total: 0' — no test matched, so this step proved nothing." >&2
    return "${EXIT_FLOOR}"
  fi

  if [ "${total}" -lt "${min_total}" ]; then
    echo "::error::${suite_label}: only ${total} test(s) ran but the floor is ${min_total} — the suite collapsed. Either a filter, trait or class name stopped matching, or tests were removed on purpose: if so, lower the floor at this suite's call site in .github/workflows/integration.yaml in the same change." >&2
    return "${EXIT_FLOOR}"
  fi

  if [ "${succeeded}" -eq 0 ]; then
    echo "::error::${suite_label}: ${total} tests were discovered but none succeeded — the entire suite skipped." >&2
    return "${EXIT_FLOOR}"
  fi

  if [ "${skipped}" -gt "${max_skipped}" ]; then
    echo "::error::${suite_label}: ${skipped} test(s) skipped but the budget is ${max_skipped} — a skip appeared that nobody declared. Either restore the test, or, when the skip is a tracked exemption, raise the budget at this suite's call site in .github/workflows/integration.yaml in the same change." >&2
    return "${EXIT_FLOOR}"
  fi

  echo "${suite_label}: ${succeeded}/${total} tests succeeded across ${summaries} assembly summary(ies), floor ${min_total}, ${skipped} skipped against a budget of ${max_skipped}."
}

expect_status() {
  local expected="$1" description="$2"
  shift 2
  local status=0
  "$@" >/dev/null 2>&1 || status=$?
  [ "${status}" -eq "${expected}" ] ||
    broken "self-test '${description}' expected exit ${expected}, got ${status}."
  echo "  ok: ${description} (exit ${status})"
}

expect_message() {
  local pattern="$1" description="$2"
  shift 2
  local output
  output="$("$@" 2>&1)" || true
  case "${output}" in
    *"${pattern}"*) echo "  ok: ${description}" ;;
    *) broken "self-test '${description}' expected a message containing '${pattern}', got: ${output}" ;;
  esac
}

write_summary_log() {
  local path="$1" total="$2" succeeded="$3" skipped="$4" esc
  esc="$(printf '\033')"
  cat >"${path}" <<LOG
${esc}[32mTest run summary: Passed!
${esc}[m  total: ${total}
  failed: 0
${esc}[32m  succeeded: ${succeeded}
${esc}[m  skipped: ${skipped}
  duration: 6s 744ms
LOG
}

self_test() {
  local tmp_root
  tmp_root="$(mktemp -d)"
  # shellcheck disable=SC2064
  trap "rm -rf '${tmp_root}'" EXIT

  local log="${tmp_root}/suite.log"

  echo "=== self-test: the per-suite test-count floor and skip budget are enforced ==="

  write_summary_log "${log}" 24 21 3
  expect_status 0 "a run at the floor passes" "${script_path}" "${log}" "fixture suite" 24 3
  expect_status 0 "a run above the floor passes" "${script_path}" "${log}" "fixture suite" 20 3

  write_summary_log "${log}" 1 1 0
  expect_status "${EXIT_FLOOR}" "a suite collapsed to a single test is rejected" \
    "${script_path}" "${log}" "fixture suite" 24 0
  expect_message "only 1 test(s) ran but the floor is 24" "the failure names the observed total and the floor" \
    "${script_path}" "${log}" "fixture suite" 24 0

  write_summary_log "${log}" 23 23 0
  expect_status "${EXIT_FLOOR}" "one test short of the floor is rejected" \
    "${script_path}" "${log}" "fixture suite" 24 0

  write_summary_log "${log}" 24 21 3
  expect_status 0 "skips exactly at the budget pass" \
    "${script_path}" "${log}" "fixture suite" 24 3
  expect_status "${EXIT_FLOOR}" "one skip over the budget is rejected" \
    "${script_path}" "${log}" "fixture suite" 24 2
  expect_message "3 test(s) skipped but the budget is 2" "the failure names the observed skips and the budget" \
    "${script_path}" "${log}" "fixture suite" 24 2

  write_summary_log "${log}" 24 24 0
  expect_status 0 "a suite that skips nothing passes a zero budget" \
    "${script_path}" "${log}" "fixture suite" 24 0

  printf 'Determining projects to restore...\nBuild succeeded.\n' >"${log}"
  expect_status "${EXIT_FLOOR}" "a log with no 'total:' summary line is rejected" \
    "${script_path}" "${log}" "fixture suite" 1 0

  printf '  total: 24\n  succeeded: 24\n' >"${log}"
  expect_status "${EXIT_FLOOR}" "a summary with no 'skipped:' line is rejected" \
    "${script_path}" "${log}" "fixture suite" 24 0
  expect_message "1 'total:' summary line(s) but 0 'skipped:' line(s)" "the failure names the mismatched summary lines" \
    "${script_path}" "${log}" "fixture suite" 24 0

  write_summary_log "${log}" 0 0 0
  expect_status "${EXIT_FLOOR}" "a run where no test matched is rejected" \
    "${script_path}" "${log}" "fixture suite" 1 0

  write_summary_log "${log}" 12 0 12
  expect_status "${EXIT_FLOOR}" "a suite that skipped in its entirety is rejected" \
    "${script_path}" "${log}" "fixture suite" 12 12

  expect_status "${EXIT_FLOOR}" "a missing log file is rejected" \
    "${script_path}" "${tmp_root}/absent.log" "fixture suite" 1 0

  printf '\033[32m  total: 24\033[m\r\n\033[32m  succeeded: 24\033[m\r\n\033[m  skipped: 0\033[m\r\n' >"${log}"
  expect_status 0 "an ANSI-coloured, CRLF summary is still parsed" \
    "${script_path}" "${log}" "fixture suite" 24 0

  write_summary_log "${tmp_root}/first-assembly.log" 8 8 0
  write_summary_log "${tmp_root}/second-assembly.log" 9 9 0
  cat "${tmp_root}/first-assembly.log" "${tmp_root}/second-assembly.log" >"${log}"
  expect_status 0 "totals across several assembly summaries add up" \
    "${script_path}" "${log}" "fixture suite" 17 0
  expect_status "${EXIT_FLOOR}" "several assembly summaries are still held to the floor" \
    "${script_path}" "${log}" "fixture suite" 18 0

  write_summary_log "${tmp_root}/first-assembly.log" 8 6 2
  write_summary_log "${tmp_root}/second-assembly.log" 9 8 1
  cat "${tmp_root}/first-assembly.log" "${tmp_root}/second-assembly.log" >"${log}"
  expect_status 0 "skips across several assembly summaries add up to the budget" \
    "${script_path}" "${log}" "fixture suite" 17 3
  expect_status "${EXIT_FLOOR}" "skips across several assembly summaries are held to the budget" \
    "${script_path}" "${log}" "fixture suite" 17 2

  write_summary_log "${log}" 24 21 3
  expect_status "${EXIT_BROKEN}" "a non-numeric floor fails loudly" \
    "${script_path}" "${log}" "fixture suite" "lots" 3
  expect_status "${EXIT_BROKEN}" "a zero floor fails loudly" \
    "${script_path}" "${log}" "fixture suite" 0 3
  expect_status "${EXIT_BROKEN}" "a non-numeric budget fails loudly" \
    "${script_path}" "${log}" "fixture suite" 24 "some"
  expect_status "${EXIT_BROKEN}" "a negative budget fails loudly" \
    "${script_path}" "${log}" "fixture suite" 24 -1
  expect_status "${EXIT_BROKEN}" "a missing floor argument fails loudly" \
    "${script_path}" "${log}" "fixture suite"
  expect_status "${EXIT_BROKEN}" "a missing budget argument fails loudly" \
    "${script_path}" "${log}" "fixture suite" 24
  expect_status "${EXIT_BROKEN}" "a fifth argument fails loudly" \
    "${script_path}" "${log}" "fixture suite" 24 3 extra

  echo "self-test passed"
}

case "${1:-}" in
  -h | --help)
    usage
    exit 0
    ;;
  --self-test)
    [ "$#" -eq 1 ] || broken "--self-test takes no further arguments."
    self_test
    exit 0
    ;;
esac

if [ "$#" -ne 4 ]; then
  usage >&2
  broken "expected exactly <test-log> <suite-label> <min-total> <max-skipped>, got $# argument(s)."
fi

assert_ran "$1" "$2" "$3" "$4"
