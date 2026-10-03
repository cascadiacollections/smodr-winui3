#!/usr/bin/env bash
set -euo pipefail

cd "$(dirname "$0")/.."

echo "Pinned SDK: $(dotnet --version)"
echo "Restoring the cross-platform RadioCore project..."
dotnet restore smodr.RadioCore/smodr.RadioCore.csproj --locked-mode \
  -p:NuGetAudit=false --ignore-failed-sources
dotnet build smodr.RadioCore/smodr.RadioCore.csproj -c Release \
  --no-restore -warnaserror

(
  cd Cascadia.Radio.Tests
  echo "Stable portable SDK: $(dotnet --version)"
  dotnet restore --locked-mode -p:NuGetAudit=false --ignore-failed-sources
  dotnet test -c Release --no-restore -warnaserror
)
echo "RadioCore and portable libraries are ready. Build and run WinUI on Windows."
