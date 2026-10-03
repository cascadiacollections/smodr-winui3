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
