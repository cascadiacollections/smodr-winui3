# Agent guidance

## Analyzer and editor consistency

- Read `DEVELOPMENT.md` and `STATIC_ANALYSIS.md` before changing build, editor,
  analyzer, or WinUI configuration. `.editorconfig` and `Directory.Build.props`
  are the shared policy for Roslyn, Visual Studio, ReSharper and Rider.
- Preserve analyzer execution during build and live analysis. IDE0045 and IDE0046
  are warnings caught by the existing warnings-as-errors CI gate. Fix findings
  without changing behavior; do not downgrade rules to make a build pass.
- Use narrow, explained source suppressions only for intentional exceptions.
  Do not blanket-disable inspections or add a competing editor-specific policy.
- Apply cleanup only to relevant source files. Inspect the diff after autofixes
  and rerun analysis: one simplification can expose another. Avoid unrelated
  formatting changes and preserve existing user changes, including lock files.
- When full ReSharper cleanup is requested, use its Full Cleanup profile and
  review source changes before accepting them. Project-loading/reference warnings
  can cause removal of required imports. Preserve implicitly consumed XAML theme
  resources even when cleanup considers them unused. Resolve Roslyn formatting
  differences and verify warning-free builds and existing tests afterward.

## WinUI generated code and stale diagnostics

- Edit source XAML and code-behind, never `obj`, `bin`, `out`, `*.g.cs`,
  `*.g.i.cs`, or `*.generated.cs`. Never paste commands or manual declarations
  into generated files. Generated-code classification does not excuse compiler
  errors or prove that source XAML and generated fields agree.
- For a type mismatch after changing XAML, compare source element type with
  the generated field and connection cast for the affected architecture,
  configuration, target framework and runtime identifier.
- If generated output is stale or corrupted, use
  `scripts/Reset-WinUiGeneratedCode.ps1 -Platform ARM64 -Configuration Debug`
  (substitute the affected platform/configuration). It archives the selected
  app output under `out/generated-code-backup`; rebuild to regenerate it.
  Do not reset every configuration routinely or delete the entire workspace.
- Avoid moving generated output during an active build/debug session. Files
  locked by the app or Visual Studio are a separate issue; do not terminate a
  user's app/IDE without authorization. Report the lock or use an isolated output
  for verification. After rebuilding, reload the solution if editor diagnostics
  remain stale. Do not claim to have cleared ReSharper caches without doing so.

## Verification

- Resolve the pinned Windows SDK with `scripts/dotnet-dev.ps1`; read `global.json`
  instead of assuming the latest installed SDK. Portable projects have their own
  stable SDK setup. Serialize architecture builds and operations sharing outputs.
- Use locked restores. Do not repair or update package locks as a side effect of
  an unrelated code change. Choose matching `Platform` and `RuntimeIdentifier`.
- For Windows source changes, run an appropriate warnings-as-errors build and
  relevant existing tests. Example ARM64 commands from the repository root:

  ```powershell
  ./scripts/dotnet-dev.ps1 ARM64 build smodr/smodr.csproj '-p:Platform=ARM64' '-p:RuntimeIdentifier=win-arm64' '-p:RestoreLockedMode=true' --no-incremental -warnaserror
  ./scripts/dotnet-dev.ps1 ARM64 test smodr.Tests/smodr.Tests.csproj '-p:Platform=ARM64' '-p:RuntimeIdentifier=win-arm64' --no-restore -warnaserror
  ```

- Use x64/win-x64 for x64 verification. For the complete static analysis gate,
  follow `STATIC_ANALYSIS.md`, including its separate locked restore and SARIF
  checks. Check `git diff --check` before handing off changes.
- Report exactly which build/tests/inspection tool ran. A Roslyn build is not a
  ReSharper InspectCode run, an editor Error List check, or native UI/playback QA.
  Explain any unverified platform or editor behavior without claiming success.
