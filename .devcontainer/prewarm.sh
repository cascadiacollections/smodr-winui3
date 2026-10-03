#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
bash scripts/validate-portable-radio.sh --restore-only --include-compat
