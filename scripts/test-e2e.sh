#!/usr/bin/env bash
# Runs the Playwright E2E suite inside Docker against a fresh app + database.
# Extra arguments are passed to the test runner, e.g.:
#   scripts/test-e2e.sh -trait "Category=Smoke"     (xUnit v3 runner options; see "-?")
set -euo pipefail
cd "$(dirname "$0")/.."

PLAYWRIGHT_VERSION=$(sed -n 's/.*Include="Microsoft.Playwright" Version="\([^"]*\)".*/\1/p' Directory.Packages.props)
if [[ -z "$PLAYWRIGHT_VERSION" ]]; then
  echo "Could not read the Microsoft.Playwright version from Directory.Packages.props" >&2
  exit 1
fi
export PLAYWRIGHT_VERSION E2E_UID="$(id -u)" E2E_GID="$(id -g)"

compose=(docker compose -p signage-e2e -f docker-compose.yml -f docker-compose.e2e.yml --profile e2e)

cleanup() {
  "${compose[@]}" logs --no-color app > artifacts/e2e/app.log 2>&1 || true
  "${compose[@]}" down -v --remove-orphans >/dev/null 2>&1 || true
}
trap cleanup EXIT

mkdir -p artifacts/e2e
"${compose[@]}" build
"${compose[@]}" up -d db app
set +e
"${compose[@]}" run --rm e2e "$@"
status=$?
set -e
if [[ $status -ne 0 ]]; then
  echo "E2E failed (exit $status). Full app log: artifacts/e2e/app.log. Tail:" >&2
  "${compose[@]}" logs app --tail=40 >&2 || true
fi
exit $status
