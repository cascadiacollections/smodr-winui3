#!/usr/bin/env bash
# Exercise real hooks as the remote user with fresh, disposable named volumes.
set -euo pipefail
cd "$(dirname "$0")/.."
[[ "$(id -un)" == vscode && "$(id -u)" != 0 ]]
[[ -w portable && -w "$HOME/.nuget/packages" && -w "$RADIO_ARTIFACTS_DIR" ]]
[[ -z "$(ls -A "$HOME/.nuget/packages")" && -z "$(ls -A "$RADIO_ARTIFACTS_DIR")" ]]
echo 'Cold non-root lifecycle: cache/artifact volumes are empty and writable.'
bash .devcontainer/prewarm.sh
bash .devcontainer/post-create.sh
[[ -s "$RADIO_ARTIFACTS_DIR/build/obj/Cascadia.Radio.Tests/project.assets.json" ]]
[[ -n "$(find "$RADIO_ARTIFACTS_DIR/results" -name '*.trx' -print -quit)" ]]
echo 'Warm non-root lifecycle: repeating update-content and post-create hooks.'
bash .devcontainer/prewarm.sh
bash .devcontainer/post-create.sh
for project in portable Cascadia.Radio Cascadia.RadioBrowser Cascadia.Radio.Metadata Cascadia.Radio.Services Cascadia.Radio.Tests smodr.RadioCore smodr.RadioSmoke; do
  [[ ! -d "$project/obj" && ! -d "$project/bin" ]]
done
echo 'Non-root cold/warm lifecycle passed; no build outputs leaked into the checkout.'
