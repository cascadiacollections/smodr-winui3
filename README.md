# Shoutkit for Windows — local preview

This branch is a Windows ARM64 adaptation of the [Shoutkit iOS app](https://github.com/cascadiacollections/shoutkit), built in the `smodr-winui3` host. It is an unpackaged, self-contained desktop preview, not a direct SwiftUI port or a complete feature-equivalent release. The current window is radio-first; the original podcast services remain in the repository but are not exposed in this preview's navigation.

Listen Now shows popular stations and recently played stations. Search supports station names and genre browsing. Favorites and the 20 most recent stations are saved in the user's local app data. Playback uses Windows MediaPlayer and the mini-player is shared across views. Station artwork is downloaded with timeout, size, format, concurrency, and cache limits before WinUI receives it. A second launch activates the existing window instead of opening a second player.

Build and run on Windows ARM64 with the [.NET 11 RC1 ARM64 SDK](https://dotnet.microsoft.com/en-us/download/dotnet/11.0). The repository's `global.json` pins `11.0.100-rc.1.26425.128`; .NET 10 alone will not build this version:

```powershell
dotnet restore smodr.slnx -p:Platform=ARM64 -r win-arm64
dotnet build smodr.slnx -c Release --no-restore -p:Platform=ARM64 -warnaserror
dotnet test smodr.slnx -c Release --no-build --no-restore -p:Platform=ARM64
dotnet publish smodr/smodr.csproj -c Release --no-restore -p:Platform=ARM64 -r win-arm64 --self-contained true -o out/shoutkit-arm64
./out/shoutkit-arm64/smodr.exe
```

Keep the published folder together: the `.exe` alone is not sufficient. No separate .NET Desktop Runtime or Windows App SDK runtime installation is required. This preview is not signed or packaged for Store distribution. If `api.nuget.org` is blocked on the host, add `--source https://www.nuget.org/api/v2/` to the restore command.

Favorites and recents are stored at `%LOCALAPPDATA%\\CascadiaCollections\\ShoutkitWindows\\library.json`. Library writes use a temporary file and replacement to reduce corruption from interrupted saves. Station metadata comes from [Radio Browser](https://www.radio-browser.info/); audio and artwork are fetched from station-provided URLs. An internet connection is required for discovery and playback. A directory or stream failure is shown in the app.

The verified target is `net11.0-windows10.0.22621.0`, with Windows App SDK 2.4 and a minimum Windows build of 17763. The .NET 11 RC1 SDK was installed side-by-side for this local build; the app was restored, built, tested, published, and launched on ARM64. RC releases can still change before general availability, so revalidate when updating the SDK or Windows App SDK.

This is a local preview. Shoutkit's iOS-specific features such as Siri, CarPlay, widgets, sleep timer, equalizer, and track history have not yet been ported.

## Original smodr project (archival documentation)

The following section describes the upstream podcast app, not the active Shoutkit Windows preview. Its runtime requirements and feature list do not apply to the self-contained preview above.

[![Build Status](https://github.com/cascadiacollections/smodr-winui3/actions/workflows/build.yml/badge.svg)](https://github.com/cascadiacollections/smodr-winui3/actions/workflows/build.yml)
[![Dev Container](https://github.com/cascadiacollections/smodr-winui3/actions/workflows/devcontainer.yml/badge.svg)](https://github.com/cascadiacollections/smodr-winui3/actions/workflows/devcontainer.yml)
[![GitHub Pages](https://github.com/cascadiacollections/smodr-winui3/actions/workflows/pages.yml/badge.svg)](https://cascadiacollections.github.io/smodr-winui3/)
[![License](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE.txt)

A modern Windows desktop podcast player built with WinUI 3 and .NET 10.

🌐 **[Project Website](https://cascadiacollections.github.io/smodr-winui3/)**

## Features

- 🎧 **RSS Feed Parsing**: Automatically fetch and parse podcast RSS feeds
- 📻 **Live Radio**: Browse and search Radio Browser stations, then play live streams
- ▶️ **Media Playback**: Play podcast episodes with pause/resume support
- 💾 **Smart Caching**: Efficient episode caching with configurable expiry
- 📥 **Downloads**: Download episodes for offline listening
- 🎨 **Modern UI**: Clean WinUI 3 interface with Mica backdrop
- 🏗️ **ARM64 Native**: Builds for x64 and ARM64 (Windows on ARM)

## Quick Start

### 🚀 One-Click Development (Recommended)

This project includes a complete dev container setup for instant development:

[![Open in Dev Containers](https://img.shields.io/static/v1?label=Dev%20Containers&message=Open&color=blue&logo=visualstudiocode)](https://vscode.dev/redirect?url=vscode://ms-vscode-remote.remote-containers/cloneInVolume?url=https://github.com/cascadiacollections/smodr-winui3)
[![Open in GitHub Codespaces](https://github.com/codespaces/badge.svg)](https://codespaces.new/cascadiacollections/smodr-winui3)

1. **Prerequisites**
   - [Visual Studio Code](https://code.visualstudio.com/)
   - [Docker Desktop](https://www.docker.com/products/docker-desktop) (for local dev containers)
   - OR use GitHub Codespaces (no local setup needed!)

2. **Get Started**
   ```bash
   git clone https://github.com/cascadiacollections/smodr-winui3.git
   code smodr-winui3
   ```

3. **Open in Container**
   - VS Code will prompt to "Reopen in Container"
   - Click the button and wait for setup to complete
   - Start coding with all tools and extensions ready!

**Note**: The dev container is perfect for code editing and analysis. To run the WinUI 3 app, you'll need Windows.

### 🪟 Running on Windows

See [DEVELOPMENT.md](DEVELOPMENT.md) for detailed Windows setup instructions.

## Project Structure

```
smodr/
├── App.xaml(.cs)           # Application entry point
├── MainWindow.xaml(.cs)    # Main application window
├── Models/                 # Data models
├── ViewModels/             # MVVM ViewModels (CommunityToolkit.Mvvm)
├── Services/               # Business logic
│   ├── AudioService.cs     # Media playback
│   ├── DataService.cs      # RSS feed handling
│   ├── CacheService.cs     # Episode caching
│   └── DownloadService.cs  # Download management
├── Converters/             # XAML value converters
└── Assets/                 # Application resources
```

## Technology Stack

| Component | Version |
|-----------|---------|
| .NET | 10.0 |
| C# | 14 (preview) |
| WinUI 3 | Windows App SDK 1.8 |
| MVVM | CommunityToolkit.Mvvm 8.4 |
| RSS | System.ServiceModel.Syndication 10.0 |
| Platforms | x64, ARM64 |

## Development

- **VS Code**: Pre-configured settings, tasks, and extensions in `.vscode/`
- **Code Style**: Enforced via `.editorconfig` (file-scoped namespaces, collection expressions)
- **GitHub Copilot**: Custom instructions in `.github/copilot-instructions.md`
- **Dev Container**: Complete containerized dev environment
- **GitHub Actions**: Automated builds (x64 + ARM64), formatting, security scanning
- **GitHub Pages**: Project website deployed from `www/`
- **Dependabot**: Automated dependency updates

For complete development setup instructions, see [DEVELOPMENT.md](DEVELOPMENT.md).

## Building

```bash
# Restore dependencies
dotnet restore

# Build (x64)
dotnet build -p:Platform=x64

# Build (ARM64)
dotnet build -p:Platform=ARM64

# Clean
dotnet clean
```

## Documentation

- [Development Setup](DEVELOPMENT.md) - Complete setup guide
- [Contributing Guide](CONTRIBUTING.md) - How to contribute
- [Copilot Instructions](.github/copilot-instructions.md) - AI assistance guidelines

## Requirements

### For Running the Application
- Windows 10 version 1809 (build 17763) or later
- .NET 10.0 Runtime
- Windows App SDK 1.8 Runtime

### For Development
- .NET 10.0 SDK
- Visual Studio 2022 17.14+ or Visual Studio 2026 (with WinUI 3 workload) **OR**
- VS Code with Dev Container (any OS for editing)

## Contributing

Contributions are welcome! Please:

1. Use the dev container or GitHub Codespaces for consistent environment
2. Follow the code style defined in `.editorconfig`
3. Maintain MVVM architecture patterns
4. Test changes on Windows before submitting
5. Review our [Contributing Guide](CONTRIBUTING.md)
6. Check our [Security Policy](SECURITY.md) for security-related contributions

## Community

- 💬 [Discussions](https://github.com/cascadiacollections/smodr-winui3/discussions) - Ask questions, share ideas
- 🐛 [Issues](https://github.com/cascadiacollections/smodr-winui3/issues) - Report bugs, request features
- 🔒 [Security](SECURITY.md) - Report security vulnerabilities

## License

See [LICENSE.txt](LICENSE.txt) for details.

## Links

- [Project Website](https://cascadiacollections.github.io/smodr-winui3/)
- [WinUI 3 Documentation](https://learn.microsoft.com/windows/apps/winui/winui3/)
- [Windows App SDK](https://learn.microsoft.com/windows/apps/windows-app-sdk/)
- [.NET 10 Documentation](https://learn.microsoft.com/dotnet/core/whats-new/dotnet-10)
