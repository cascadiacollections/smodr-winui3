# smodr - WinUI3 Podcast Player

[![Build Status](https://github.com/cascadiacollections/smodr-winui3/actions/workflows/build.yml/badge.svg)](https://github.com/cascadiacollections/smodr-winui3/actions/workflows/build.yml)
[![CodeQL](https://github.com/cascadiacollections/smodr-winui3/actions/workflows/codeql.yml/badge.svg)](https://github.com/cascadiacollections/smodr-winui3/actions/workflows/codeql.yml)
[![Dev Container](https://github.com/cascadiacollections/smodr-winui3/actions/workflows/devcontainer.yml/badge.svg)](https://github.com/cascadiacollections/smodr-winui3/actions/workflows/devcontainer.yml)
[![License](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE.txt)

A modern Windows desktop podcast player built with WinUI3 and .NET 9.0.

## Features

- 🎧 **RSS Feed Parsing**: Automatically fetch and parse podcast RSS feeds
- ▶️ **Media Playback**: Play podcast episodes with pause/resume support
- 💾 **Smart Caching**: Efficient episode caching with configurable expiry
- 📥 **Downloads**: Download episodes for offline listening
- 🎨 **Modern UI**: Clean WinUI3 interface following Windows design guidelines
- 🔄 **Background Updates**: Automatic feed refresh with manual override

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

**Note**: The dev container is perfect for code editing and analysis. To run the WinUI3 app, you'll need Windows.

### 🪟 Running on Windows

See [DEVELOPMENT.md](DEVELOPMENT.md) for detailed Windows setup instructions.

## Project Structure

```
smodr/
├── App.xaml(.cs)           # Application entry point
├── MainWindow.xaml(.cs)    # Main application window
├── Models/                 # Data models
├── ViewModels/             # MVVM ViewModels
├── Services/               # Business logic
│   ├── AudioService.cs     # Media playback
│   ├── DataService.cs      # RSS feed handling
│   ├── CacheService.cs     # Episode caching
│   └── DownloadService.cs  # Download management
├── Converters/             # XAML value converters
└── Assets/                 # Application resources
```

## Technology Stack

- **Framework**: .NET 9.0
- **UI**: WinUI3 (Windows App SDK)
- **Architecture**: MVVM
- **Dependencies**:
  - CommunityToolkit.Mvvm
  - System.ServiceModel.Syndication

## Development

- **VS Code**: Pre-configured settings, tasks, and extensions in `.vscode/`
- **Code Style**: Enforced via `.editorconfig`
- **GitHub Copilot**: Custom instructions in `.github/copilot-instructions.md`
- **Dev Container**: Complete containerized dev environment
- **GitHub Actions**: Automated builds, testing, and security scanning
- **Dependabot**: Automated dependency updates

For complete development setup instructions, see [DEVELOPMENT.md](DEVELOPMENT.md).

## Building

```bash
# Restore dependencies
dotnet restore

# Build
dotnet build

# Clean
dotnet clean
```

## Documentation

- [Development Setup](DEVELOPMENT.md) - Complete setup guide
- [Copilot Instructions](.github/copilot-instructions.md) - AI assistance guidelines

## Requirements

### For Running the Application
- Windows 10 version 1809 (build 17763) or later
- .NET 9.0 Runtime
- Windows App SDK Runtime

### For Development
- .NET 9.0 SDK
- Visual Studio 2022 (with WinUI3 workload) **OR**
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

- [WinUI3 Documentation](https://learn.microsoft.com/windows/apps/winui/winui3/)
- [Windows App SDK](https://learn.microsoft.com/windows/apps/windows-app-sdk/)
- [.NET 9.0 Documentation](https://learn.microsoft.com/dotnet/core/whats-new/dotnet-9)