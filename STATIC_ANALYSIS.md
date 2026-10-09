# Static analysis and production readiness

SDK Roslyn analyzers, nullable analysis and `.editorconfig` are the required
baseline. `latest-recommended` tracks the selected SDK: stable .NET 10 for portable
libraries and the pinned .NET 11 RC for Windows. Build-time analyzers are explicit.
Intentional exceptions need narrow suppressions with a reason. Static analysis
does not replace race tests, live streams, accessibility, dependency auditing or signing.

After a locked restore for the chosen scope:

```powershell
./scripts/Test-StaticAnalysisHarness.ps1
./scripts/Test-StaticAnalysis.ps1
./scripts/Test-StaticAnalysis.ps1 -Scope Windows -Platform ARM64
```

These source checks force a non-incremental Release compilation with warnings as
errors and analyzers enabled. They do not format or rewrite source. Every run gets
a unique `out/static-analysis/<id>/` with per-project SARIF 2.1 reports, so old
reports cannot supply a new verdict. CI runs the same gate on portable Linux/Windows
and Windows x64/ARM64 and retains reports on failure. Compiler failure is failure
even without SARIF. Restore stays separate and locked; this gate never repairs locks.
Accepted in-source suppressions (for example reviewed `#pragma` directives) stay
visible in SARIF without failing the verdict. External baselines are not accepted
as waivers; rejected source suppressions fail normally.

## Optional ReSharper / Rider inspection

### Shared editor policy and stale WinUI diagnostics

Use the repository `.editorconfig` as the shared policy. In ReSharper, enable
**Code Inspection > Settings > Read settings from editorconfig and project settings**.
In Rider, enable EditorConfig support. Keep personal overrides in untracked user
settings. IDE0045 and IDE0046 are build warnings; the existing warnings-as-errors
CI gate now rejects these regressions. Live Roslyn analysis is explicitly enabled.
This does not guarantee identical results from JetBrains' independent inspections.

Generated `obj`, `bin`, `out`, `*.g.cs`, `*.g.i.cs` and `*.generated.cs` are marked
as generated code for Roslyn and JetBrains. Never edit these files or include them
in code cleanup. Source XAML and code-behind remain inspected. VS Code also hides
and excludes `out` from searches and file watching.

If a XAML element changes type but the Error List still reports the old type,
stop debugging and close the solution before regenerating the affected output:

```powershell
./scripts/Reset-WinUiGeneratedCode.ps1 -Platform ARM64 -Configuration Debug
./scripts/dotnet-dev.ps1 ARM64 build smodr/smodr.csproj '-p:Platform=ARM64' '-p:RuntimeIdentifier=win-arm64' '-p:RestoreLockedMode=true'
```

Use `x64` / `win-x64` for x64. The reset archives only the selected app's generated
architecture/configuration directory under ignored `out/generated-code-backup`.
It preserves source and restore lock files and does not stop running processes.
Reload the solution after rebuilding. If files are locked, stop the app/debugger
and retry. ReSharper's cache invalidation is a last step after a successful build
if its own inspection results remain stale; do not suppress compiler errors.

References: [JetBrains EditorConfig support](https://www.jetbrains.com/help/resharper/Using_EditorConfig.html),
[generated code settings](https://www.jetbrains.com/help/resharper/Reference__Options__Code_Inspection__Generated_Code.html),
and [Microsoft build-time analysis](https://learn.microsoft.com/en-us/dotnet/fundamentals/code-analysis/overview).

JetBrains supplies a free standalone InspectCode CLI; an editor extension or IDE
installation is unnecessary. Obtain a reviewed version appropriate for the SDK
from the [official CLI documentation](https://www.jetbrains.com/help/resharper/ReSharper_Command_Line_Tools.html).
Record its version alongside review evidence and pass its executable explicitly:

```powershell
./scripts/Test-StaticAnalysis.ps1 -InspectCodePath C:/tools/resharper/inspectcode.exe
```

The wrapper enables the tool's build step (needed for source generators), locked
restore and SARIF output. It rejects warning/error findings even when InspectCode
exits zero, plus failed invocations and tool warning/error notifications. Share
`.editorconfig` between Roslyn and JetBrains; review new inspection severities
before adding them instead of maintaining conflicting editor-specific policies.

See [InspectCode options](https://www.jetbrains.com/help/resharper/InspectCode.html).
This is not a floating download or mandatory CI dependency. No ReSharper execution
or .NET 11 RC/WinUI support is claimed until a matching CLI version is tested.
Start with the stable portable solution. Unsupported SDKs, missing reports,
blocked feeds and nonzero exits are failures, never clean analysis. The wrapper
does not install globally, alter editors or run cleanup/autofix. Independent
ReSharper warnings may need genuine fixes before its optional gate passes.
