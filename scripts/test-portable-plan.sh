#!/usr/bin/env bash
# Regression fixtures exercise orchestration without Docker, SDK execution or streams.
set -euo pipefail
cd "$(dirname "$0")/.."
export RADIO_RUNTIME_IDENTIFIER=linux-arm64
export RADIO_ARTIFACTS_DIR=/tmp/radio-plan-fixture
export RADIO_NUGET_SOURCE=https://www.nuget.org/api/v2/
export RADIO_NUGET_AUDIT=false
quick=$(bash scripts/validate-portable-radio.sh --quick --plan)
[[ "$quick" == *'dotnet test Radio.Portable.slnx'* && "$quick" != *'dotnet pack'* ]]
restore=$(bash scripts/validate-portable-radio.sh --restore-only --include-compat --plan)
[[ "$restore" == *'smodr.RadioSmoke.csproj'* && "$restore" != *'dotnet build'* && "$restore" != *'dotnet test'* ]]
full=$(bash scripts/validate-portable-radio.sh --include-compat --plan)
[[ "$full" == *'dotnet pack'* && "$full" == *'dotnet publish'* && "$full" == *'--no-build'* ]]
[[ "$full" == *'-r linux-arm64'* && "$full" == *'-p:RadioPackageVersion=0.1.0-dev.'* ]]
export RADIO_RUNTIME_IDENTIFIER=linux-x64
x64=$(bash scripts/validate-portable-radio.sh --plan)
[[ "$x64" == *'-r linux-x64'* && "$x64" != *'-r linux-arm64'* ]]
if bash scripts/validate-portable-radio.sh --unknown --plan >/dev/null 2>&1; then
  echo 'Unknown options must fail.' >&2; exit 1
fi
export RADIO_NUGET_AUDIT=invalid
if bash scripts/validate-portable-radio.sh --plan >/dev/null 2>&1; then
  echo 'Invalid audit configuration must fail.' >&2; exit 1
fi
export RADIO_NUGET_AUDIT=false RADIO_RUNTIME_IDENTIFIER=unsupported
if bash scripts/validate-portable-radio.sh --plan >/dev/null 2>&1; then
  echo 'Unsupported execution RID must fail.' >&2; exit 1
fi
echo 'Passed 8 portable validation plan fixtures; no SDK or Docker process executed.'
