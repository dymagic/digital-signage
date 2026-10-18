#!/usr/bin/env bash
# Retakes the README screenshots (docs/images/*.jpg) from the running app, on a fresh database.
set -euo pipefail
cd "$(dirname "$0")/.."

rm -rf artifacts/e2e/readme
SIGNAGE_E2E_README=1 scripts/test-e2e.sh -class "Signage.E2E.Docs.ReadmeScreenshots"
mkdir -p docs/images
cp artifacts/e2e/readme/*.jpg docs/images/
ls -l docs/images
