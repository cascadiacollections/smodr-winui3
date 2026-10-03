#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
bash scripts/validate-portable-radio.sh --quick --include-compat
echo "Portable libraries and RadioCore are ready. Build and run WinUI on Windows."
