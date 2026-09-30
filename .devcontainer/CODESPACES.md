# Codespaces and dev container

The Linux container pins the .NET 11 RC SDK from `global.json` and validates the
cross-platform `smodr.RadioCore` project during setup. Open the repository with
VS Code Dev Containers or create a GitHub Codespace, then run:

```bash
dotnet --version
dotnet build smodr.RadioCore/smodr.RadioCore.csproj -c Release -warnaserror -p:NuGetAudit=false
```

The WinUI app and `smodr.Tests` require Windows. Use the Windows ARM64/x64
tasks, a Windows terminal, or Windows CI for full builds, tests, packaging, and
native playback. Container setup does not try to restore `smodr.slnx` and does
not treat a failed restore as success. See [DEVELOPMENT.md](../DEVELOPMENT.md)
for editor and SDK selection details.
