#!/usr/bin/env bash
# Shared Linux validation for Codespaces, local Docker and CI. No Windows app or live streams.
set -euo pipefail
repository="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
mode=full
include_compat=false
plan=false
for option in "$@"; do
  case "$option" in
    --quick) mode=quick ;;
    --restore-only) mode=restore ;;
    --include-compat) include_compat=true ;;
    --plan) plan=true ;;
    *) echo "Unknown option: $option" >&2; exit 2 ;;
  esac
done
artifacts="${RADIO_ARTIFACTS_DIR:-$repository/.container-artifacts}"
source="${RADIO_NUGET_SOURCE:-https://api.nuget.org/v3/index.json}"
audit="${RADIO_NUGET_AUDIT:-true}"
case "$audit" in true|false) ;; *) echo "RADIO_NUGET_AUDIT must be true or false." >&2; exit 2 ;; esac
run() {
  if "$plan"; then printf '%q ' "$@"; printf '\n'; else "$@"; fi
}
cd "$repository/portable"
run dotnet restore Radio.Portable.slnx --locked-mode --source "$source" "-p:NuGetAudit=$audit" --artifacts-path "$artifacts/build"
if "$include_compat"; then
  (
    cd "$repository"
    for project in smodr.RadioCore smodr.RadioSmoke; do
      run dotnet restore "$project/$project.csproj" --locked-mode --source "$source" "-p:NuGetAudit=$audit" --artifacts-path "$artifacts/compat"
      if [[ "$mode" != restore ]]; then
        run dotnet build "$project/$project.csproj" -c Release --no-restore --artifacts-path "$artifacts/compat" -warnaserror
      fi
    done
  )
fi
if [[ "$mode" == restore ]]; then exit 0; fi
run dotnet test Radio.Portable.slnx -c Release --no-restore --artifacts-path "$artifacts/build" -warnaserror --logger trx --results-directory "$artifacts/results"
if [[ "$mode" == quick ]]; then exit 0; fi
# Unique draft versions reuse the third-party cache without ever consuming stale local SDK packages.
package_version="0.1.0-dev.$(date -u +%Y%m%d%H%M%S).${RANDOM}"
for project in Cascadia.Radio Cascadia.Radio.Metadata Cascadia.Radio.Services Cascadia.RadioBrowser; do
  run dotnet pack "$repository/$project/$project.csproj" -c Release --no-restore --artifacts-path "$artifacts/build" "-p:Version=$package_version" -o "$artifacts/packages" -warnaserror
done
(
  cd "$repository/samples/RadioSdk.Consumer"
  run dotnet restore --source "$artifacts/packages" "-p:RadioPackageVersion=$package_version" "-p:NuGetAudit=$audit" --artifacts-path "$artifacts/build"
  run dotnet build -c Release --no-restore --artifacts-path "$artifacts/build" "-p:RadioPackageVersion=$package_version" -warnaserror
  run dotnet run -c Release --no-restore --no-build --artifacts-path "$artifacts/build" "-p:RadioPackageVersion=$package_version"
)
runtime_identifier="${RADIO_RUNTIME_IDENTIFIER:-}"
if [[ -z "$runtime_identifier" ]]; then
  case "$(uname -m)" in
    aarch64|arm64) runtime_identifier=linux-arm64 ;;
    x86_64|amd64) runtime_identifier=linux-x64 ;;
    *) echo "Set RADIO_RUNTIME_IDENTIFIER for this unsupported architecture." >&2; exit 2 ;;
  esac
fi
case "$runtime_identifier" in linux-arm64|linux-x64) ;; *) echo "Only Linux ARM64/x64 execution is supported." >&2; exit 2 ;; esac
(
  cd "$repository/samples/RadioBrowser.TrimConsumer"
  run dotnet restore -r "$runtime_identifier" --source "$artifacts/packages" --source "$source" "-p:RadioPackageVersion=$package_version" "-p:NuGetAudit=$audit" --artifacts-path "$artifacts/build"
  run dotnet publish -c Release -r "$runtime_identifier" --self-contained --no-restore --artifacts-path "$artifacts/build" "-p:RadioPackageVersion=$package_version" -o "$artifacts/trimmed" -warnaserror
  run "$artifacts/trimmed/RadioBrowser.TrimConsumer"
)
