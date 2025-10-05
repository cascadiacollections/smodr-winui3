# Development Environment Setup

This guide helps you set up your development environment for the smodr WinUI3 project.

## Quick Start with VS Code Dev Container

The fastest way to get started is using VS Code with Dev Containers:

### Prerequisites

- [Visual Studio Code](https://code.visualstudio.com/)
- [Docker Desktop](https://www.docker.com/products/docker-desktop)
- [Remote - Containers extension](https://marketplace.visualstudio.com/items?itemName=ms-vscode-remote.remote-containers)

### Steps

1. **Clone the repository**
   ```bash
   git clone https://github.com/cascadiacollections/smodr-winui3.git
   cd smodr-winui3
   ```

2. **Open in VS Code**
   ```bash
   code .
   ```

3. **Reopen in Container**
   - VS Code will detect the `.devcontainer` configuration
   - Click "Reopen in Container" when prompted
   - Or use Command Palette (F1) → "Remote-Containers: Reopen in Container"

4. **Wait for Setup**
   - The container will build automatically
   - Dependencies will be restored via `dotnet restore`
   - Recommended extensions will be installed

5. **Start Developing!**
   - All VS Code settings are pre-configured
   - IntelliSense and code navigation work out of the box
   - Use the integrated terminal for builds and commands

**Note**: While the dev container is great for code editing, analysis, and builds, running WinUI3 applications requires Windows. You'll need to build and run on a Windows machine or VM.

---

## Alternative: Manual Setup on Windows

If you prefer not to use containers or need to run the application:

### Prerequisites

- **Windows 10** version 1809 (build 17763) or later
- **Visual Studio 2022** or later with the following workloads:
  - .NET Desktop Development
  - Universal Windows Platform development
- **.NET 9.0 SDK** ([Download](https://dotnet.microsoft.com/download/dotnet/9.0))
- **Windows SDK** version 10.0.19041.0 or later

### Visual Studio Setup

1. **Install Visual Studio 2022**
   - Select ".NET Desktop Development" workload
   - Select "Universal Windows Platform development" workload
   - Ensure Windows 10 SDK (10.0.19041.0) is included

2. **Clone the repository**
   ```bash
   git clone https://github.com/cascadiacollections/smodr-winui3.git
   cd smodr-winui3
   ```

3. **Open the solution**
   - Double-click `smodr.sln` or
   - Open Visual Studio → File → Open → Project/Solution → Select `smodr.sln`

4. **Restore NuGet packages**
   - Visual Studio should restore automatically
   - Or right-click solution → Restore NuGet Packages

5. **Build the solution**
   - Press `Ctrl+Shift+B` or
   - Build → Build Solution

6. **Run the application**
   - Press `F5` to run with debugging
   - Or `Ctrl+F5` to run without debugging

### VS Code Setup (Windows)

If you prefer VS Code on Windows:

1. **Install VS Code**
   - Download from [code.visualstudio.com](https://code.visualstudio.com/)

2. **Install .NET 9.0 SDK**
   - Download from [dotnet.microsoft.com](https://dotnet.microsoft.com/download/dotnet/9.0)

3. **Open the project**
   ```bash
   cd smodr-winui3
   code .
   ```

4. **Install recommended extensions**
   - VS Code will prompt to install recommended extensions
   - Or install manually from `.vscode/extensions.json`

5. **Build and restore**
   - Press `Ctrl+Shift+B` to build
   - Or use the terminal:
     ```bash
     dotnet restore
     dotnet build
     ```

---

## Project Structure

```
smodr-winui3/
├── .devcontainer/          # Dev container configuration
│   ├── devcontainer.json   # Container settings
│   └── Dockerfile          # Container image
├── .github/                # GitHub-specific files
│   └── copilot-instructions.md  # Copilot guidelines
├── .vscode/                # VS Code settings
│   ├── extensions.json     # Recommended extensions
│   ├── settings.json       # Editor settings
│   ├── tasks.json          # Build tasks
│   └── launch.json         # Debug configurations
├── smodr/                  # Main application project
│   ├── App.xaml(.cs)       # Application entry
│   ├── MainWindow.xaml(.cs) # Main window
│   ├── Models/             # Data models
│   ├── ViewModels/         # MVVM ViewModels
│   ├── Services/           # Business logic
│   ├── Converters/         # XAML converters
│   └── Assets/             # Images and resources
├── .editorconfig           # Code style rules
├── .gitignore              # Git ignore rules
├── smodr.sln               # Visual Studio solution
└── README.md               # Project overview
```

---

## Common Development Tasks

### Building

```bash
# Restore dependencies
dotnet restore

# Build the solution
dotnet build

# Clean build artifacts
dotnet clean

# Rebuild from scratch
dotnet clean && dotnet build
```

### Code Formatting

```bash
# Format code according to .editorconfig
dotnet format
```

### Working with the Dev Container

```bash
# View container logs
Docker Desktop → Containers → smodr-devcontainer

# Rebuild container after Dockerfile changes
Command Palette (F1) → "Remote-Containers: Rebuild Container"

# Access container terminal
VS Code Terminal (Ctrl+`) automatically connects
```

---

## Troubleshooting

### Dev Container Issues

**Problem**: Container fails to build
- **Solution**: Ensure Docker Desktop is running
- **Solution**: Check Docker has sufficient resources (4GB+ RAM recommended)
- **Solution**: Try rebuilding: F1 → "Remote-Containers: Rebuild Container"

**Problem**: Extensions not installing
- **Solution**: Wait for initial setup to complete
- **Solution**: Manually install from Extensions view (Ctrl+Shift+X)

**Problem**: IntelliSense not working
- **Solution**: Wait for OmniSharp to load (see status in VS Code status bar)
- **Solution**: Reload window: F1 → "Developer: Reload Window"

### Build Issues

**Problem**: Cannot restore packages
- **Solution**: Check internet connection
- **Solution**: Clear NuGet cache: `dotnet nuget locals all --clear`
- **Solution**: Restore manually: `dotnet restore smodr.sln`

**Problem**: Windows SDK not found (on Windows)
- **Solution**: Install Windows SDK 10.0.19041.0 or later
- **Solution**: Visual Studio Installer → Modify → Individual Components → Windows SDK

**Problem**: WinUI3 packages not found
- **Solution**: Ensure WindowsAppSDK package is restored
- **Solution**: Update NuGet package manager

### Runtime Issues (Windows Only)

**Problem**: Application won't start
- **Solution**: Ensure you're on Windows 10 version 1809 or later
- **Solution**: Install latest Windows App Runtime: [Download](https://learn.microsoft.com/windows/apps/windows-app-sdk/downloads)

**Problem**: Media playback not working
- **Solution**: Check that the RSS feed URL is accessible
- **Solution**: Verify media file format is supported

---

## GitHub Copilot Integration

This project includes comprehensive GitHub Copilot instructions in `.github/copilot-instructions.md`. When using Copilot:

- It understands the project structure and conventions
- It follows MVVM patterns specific to this project
- It provides WinUI3-appropriate suggestions
- It maintains consistency with existing code style

To get the best results:
- Ask specific questions about the codebase
- Reference specific files or services
- Copilot will follow the project's coding standards automatically

---

## Additional Resources

- [WinUI 3 Documentation](https://learn.microsoft.com/windows/apps/winui/winui3/)
- [.NET 9.0 Documentation](https://learn.microsoft.com/dotnet/core/whats-new/dotnet-9)
- [Windows App SDK](https://learn.microsoft.com/windows/apps/windows-app-sdk/)
- [MVVM Toolkit](https://learn.microsoft.com/dotnet/communitytoolkit/mvvm/)
- [VS Code Dev Containers](https://code.visualstudio.com/docs/devcontainers/containers)

---

## Getting Help

If you encounter issues:

1. Check the [Troubleshooting](#troubleshooting) section above
2. Search existing GitHub issues
3. Create a new issue with:
   - Your environment details (OS, VS version, etc.)
   - Steps to reproduce
   - Error messages or logs
   - Screenshots if relevant

---

## Contributing

When contributing to this project:

1. Follow the coding conventions in `.editorconfig`
2. Maintain MVVM architecture patterns
3. Add appropriate error handling
4. Test your changes on Windows
5. Use the pre-configured VS Code settings
6. Let GitHub Copilot help with boilerplate code

The dev container setup ensures all contributors have a consistent development environment!
