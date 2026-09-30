# Shoutkit Windows development

This is the active Windows ARM64/x64 WinUI 3 preview. The solution is `smodr.slnx`,
and `global.json` pins .NET SDK `11.0.100-rc.1.26425.128`. See [README.md](README.md)
for the current app behavior and self-contained publish command.

## Windows host: VS Code or Zed

Use a Windows host with the pinned .NET 11 RC SDK and the Windows build tools
required by WinUI. Restore with the checked-in lock files before building. The
editor tasks use `scripts/dotnet-dev.ps1`, which looks for:

1. `SHOUTKIT_DOTNET_ARM64` or `SHOUTKIT_DOTNET_X64` (an absolute `dotnet.exe` path),
2. `SHOUTKIT_DOTNET`,
3. this workspace's optional sibling SDK folders (`../dotnet11-rc-sdk/runtime/`
   and `../dotnet11-rc-x64/runtime/`), or
4. a matching SDK on PATH for the host's architecture.

The resolver checks the version against `global.json` and fails with a useful
message rather than silently building with .NET 8 or 10. A globally installed
SDK may still be needed for the editor's C# language server; the resolver only
controls tasks. On Windows ARM64, use an x64 SDK/runtime for x64 test execution.

VS Code has ARM64 and x64 restore, build, and test tasks in `.vscode/tasks.json`.
The ARM64 build is the default build task. The launch configurations point at
the actual `net11.0-windows10.0.22621.0` Debug outputs. Because Shoutkit is
single-instance, choose **Attach to running Shoutkit** if it is already open;
launching a second process only activates the first window. These are Windows
debug configurations, not container launch configurations.

Zed has corresponding commands in `.zed/tasks.json`. Install Zed's C# extension
for Roslyn language services; `.zed/settings.json` keeps generated output out of
file scanning and formats changed C# lines on save. Zed's tasks build/test the
Windows app on the host, but do not provide a WinUI visual designer.

For a terminal build on this Windows ARM64 host:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/dotnet-dev.ps1 ARM64 restore smodr.slnx -p:Platform=ARM64 --locked-mode -p:NuGetAudit=false --ignore-failed-sources
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/dotnet-dev.ps1 ARM64 test smodr.slnx -c Release --no-restore -p:Platform=ARM64 -warnaserror
```

The local editor restore disables NuGet auditing because corporate networks may
block the advisory endpoint. Missing packages still fail the restore. CI uses
an ordinary audited restore and is the release gate.

## Linux dev container and Codespaces

Open the repository in VS Code Dev Containers or GitHub Codespaces. The
container installs the exact SDK pinned in `global.json`; post-create restores
and builds `smodr.RadioCore` with warnings as errors. It does not attempt to
restore the whole solution and does not report a failed WinUI build as success.

The container supports editing, C# navigation, documentation, and cross-platform
RadioCore builds. WinUI compilation, the app test project, native playback,
packaging, and interactive debugging still require a Windows host or Windows CI.
The container validation workflow performs the same RadioCore restore/build and
fails if either step fails.

```bash
dotnet --version
dotnet restore smodr.RadioCore/smodr.RadioCore.csproj --locked-mode -p:NuGetAudit=false --ignore-failed-sources
dotnet build smodr.RadioCore/smodr.RadioCore.csproj -c Release --no-restore -warnaserror
```

If the container cannot download the pinned SDK or NuGet packages behind a
corporate proxy, fix network/proxy access or use the Windows host's cached SDK;
the setup intentionally does not hide that failure.
