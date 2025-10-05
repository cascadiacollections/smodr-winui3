# smodr - WinUI3 Podcast Player

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

1. **Prerequisites**
   - [Visual Studio Code](https://code.visualstudio.com/)
   - [Docker Desktop](https://www.docker.com/products/docker-desktop)
   - [Remote - Containers extension](https://marketplace.visualstudio.com/items?itemName=ms-vscode-remote.remote-containers)

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

1. Use the dev container for consistent environment
2. Follow the code style defined in `.editorconfig`
3. Maintain MVVM architecture patterns
4. Test changes on Windows before submitting

## License

See [LICENSE.txt](LICENSE.txt) for details.

## Links

- [WinUI3 Documentation](https://learn.microsoft.com/windows/apps/winui/winui3/)
- [Windows App SDK](https://learn.microsoft.com/windows/apps/windows-app-sdk/)
- [.NET 9.0 Documentation](https://learn.microsoft.com/dotnet/core/whats-new/dotnet-9)