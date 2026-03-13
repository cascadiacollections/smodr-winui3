# GitHub Copilot Instructions for smodr WinUI3 Project

## Project Overview

**smodr** is a WinUI3 desktop application for Windows built with .NET 10.0 and C# 14 (preview). It's a podcast/media player application that supports RSS feed parsing, episode playback, caching, and downloads.

## Technology Stack

- **Framework**: .NET 10.0 with WinUI 3 (Windows App SDK 1.8)
- **Platform**: Windows 10.0.22621.0 (minimum 10.0.17763.0)
- **Architecture**: MVVM (Model-View-ViewModel)
- **Language**: C# 14 (preview) with `ImplicitUsings`, `Nullable`, `LangVersion=preview`
- **Key Dependencies**:
  - Microsoft.WindowsAppSDK 1.8 (stable, MSFT validated)
  - CommunityToolkit.Mvvm 8.4 (`ObservableObject`, `[ObservableProperty]`, `[RelayCommand]`)
  - System.ServiceModel.Syndication (RSS parsing)

## Project Structure

```
smodr/
├── App.xaml(.cs)           # Application entry point
├── AppConstants.cs         # Shared constants (UserAgent, etc.)
├── MainWindow.xaml(.cs)    # Main application window
├── Models/                 # Data models (Episode, Podcast)
├── ViewModels/             # MVVM ViewModels
├── Services/               # Business logic services
│   ├── AudioService.cs     # Media playback
│   ├── CacheService.cs     # Episode caching
│   ├── DataService.cs      # RSS feed fetching
│   ├── DownloadService.cs  # Episode downloads
│   ├── ImageCacheService.cs    # Image download + local caching
│   └── PodcastDirectoryService.cs  # iTunes Lookup API
├── Converters/             # XAML value converters (one per file)
└── Assets/                 # Application assets
```

## Coding Guidelines

### General Conventions

1. **Naming**: Use PascalCase for public members, camelCase with underscore prefix for private fields (`_fieldName`)
2. **Null Safety**: Project uses nullable reference types (`<Nullable>enable</Nullable>`)
3. **Async/Await**: Use async methods with `Async` suffix, always await async operations
4. **MVVM Pattern**: Keep business logic in ViewModels and Services, keep code-behind minimal

### C# Style

- Use modern C# features (records, pattern matching, null-coalescing)
- Prefer expression-bodied members when concise
- Use `var` for obvious types
- Organize usings: System namespaces first, then third-party, then local

### XAML Patterns

- Use x:Bind for data binding (compiled bindings)
- Leverage value converters in Converters/ folder
- Follow existing control styling patterns
- Use resource dictionaries for shared styles

### Service Layer

- Services should be disposable when they hold resources
- Use dependency injection patterns where applicable
- Services handle async operations and exception handling
- Cache operations should be isolated in CacheService

### Error Handling

- Use try-catch for expected exceptions
- Log errors using `System.Diagnostics.Debug.WriteLine`
- Show user-friendly error messages in UI
- Don't swallow exceptions silently

## Common Development Tasks

### Adding a New Feature

1. Create model classes in `Models/` if needed
2. Add service methods in `Services/` for business logic
3. Update ViewModel in `ViewModels/` to expose to UI
4. Update XAML in views to present the feature
5. Add value converters if needed for data transformation

### Working with RSS Feeds

- Use `System.ServiceModel.Syndication.SyndicationFeed`
- Parse feeds in `DataService.cs`
- Extract episode information following existing patterns
- Cache parsed episodes using `CacheService`

### Media Playback

- Use `AudioService` for all playback operations
- Update UI through ViewModel property notifications
- Handle playback state changes properly
- Support pause/resume/stop operations

### Caching

- Cache episodes using `CacheService`
- Configure cache expiry via `LocalSettings["CacheExpiryHours"]`
- Implement cache metadata for tracking
- Support force refresh to bypass cache

## Build and Run

### Prerequisites (Windows)

- Visual Studio 2022 17.14+ or Visual Studio 2026 with WinUI 3 workload
- .NET 10.0 SDK
- Windows SDK (10.0.22621.0 or later)

### Build Commands

```bash
# Restore dependencies
dotnet restore smodr.slnx

# Build the solution
dotnet build smodr.slnx

# Clean build artifacts
dotnet clean smodr.slnx
```

### Running the Application

The application must be run on Windows as it's a WinUI3 desktop app. It cannot run in the devcontainer but code editing and analysis work.

## Testing Considerations

- Test RSS feed parsing with various feed formats
- Test playback with different media formats
- Verify cache behavior with various expiry settings
- Test download functionality with edge cases
- Ensure proper error handling for network failures

## Important Notes for Copilot

1. **Windows-Only**: This is a Windows desktop application using WinUI3. It requires Windows to run.
2. **XAML Awareness**: When modifying UI, update both XAML and code-behind/ViewModel as needed.
3. **Async Patterns**: Most I/O operations are async. Maintain this pattern.
4. **Resource Management**: Dispose of services properly to avoid resource leaks.
5. **MVVM Compliance**: Keep ViewModels testable by avoiding direct UI dependencies.
6. **Thread Safety**: UI updates must occur on UI thread using DispatcherQueue.

## Common Patterns to Follow

### Property Change Notification (CommunityToolkit.Mvvm)

```csharp
[ObservableProperty]
private string _title = string.Empty;
```

### Async Command Handling (CommunityToolkit.Mvvm)

```csharp
[RelayCommand]
public async Task LoadAsync()
{
    // Async operation — generates LoadCommand automatically
}
```

### Service Usage

```csharp
private readonly DataService _dataService = new();
private readonly AudioService _audioService = new();

public void Dispose()
{
    _dataService?.Dispose();
    _audioService?.Dispose();
}
```

## When Making Changes

- Follow existing code patterns and conventions
- Update documentation if adding significant features
- Consider backwards compatibility
- Test on Windows before considering changes complete
- Keep changes focused and minimal
- Use meaningful commit messages

## Resources

- <a href="https://learn.microsoft.com/windows/apps/winui/winui3/">WinUI3 Documentation</a>
- <a href="https://github.com/microsoft/WindowsAppSDK-Samples">.NET MAUI and WinUI Samples</a>
- <a href="https://learn.microsoft.com/dotnet/communitytoolkit/mvvm/">MVVM Toolkit Documentation</a>

## CI/CD and Automation

### GitHub Actions

This project uses GitHub Actions for continuous integration:

- **Build Workflow**: Matrix builds for x64 and ARM64 on Windows runners
- **GitHub Pages**: Project website deployed from `www/`
- **CodeQL Analysis**: Security scanning for vulnerabilities
- **Format Check**: Validates code formatting against `.editorconfig`
- **Dev Container Test**: Validates dev container configuration

### Dependabot

Automated dependency updates are configured for:
- NuGet packages (grouped by vendor)
- GitHub Actions
- Docker base images

### Code Quality

- Use `dotnet format` to format code before committing
- CodeQL scans run on every push and pull request
- All warnings should be addressed or suppressed with justification

## Development Environment

### Dev Container / Codespaces

The project includes a fully configured dev container that works with:
- VS Code Dev Containers
- GitHub Codespaces
- Any container-compatible IDE

**Note**: The dev container is for code editing only. WinUI3 apps require Windows to build and run.

### Available Tools

Pre-installed in the dev container:
- .NET 10.0 SDK
- dotnet-format (code formatter)
- dotnet-outdated-tool (dependency checker)
- GitHub CLI (gh)
- Git with enhanced features

## Security Best Practices

- Never commit secrets or credentials
- Use `LocalSettings` for user-specific configuration
- Validate all external inputs (RSS feeds, URLs)
- Handle exceptions gracefully
- Keep dependencies up to date via Dependabot

## Modern C# Patterns

### File-scoped Namespaces

```csharp
namespace smodr.Services;

public class MyService
{
    // Implementation
}
```

### Top-level Statements

Not used in this project due to WinUI3 requirements, but be aware of the pattern.

### Record Types

Use for immutable data transfer objects:

```csharp
public record Episode(string Title, string Url, DateTime PublishDate);
```

### Pattern Matching

```csharp
if (result is { Success: true, Data: var data })
{
    ProcessData(data);
}
```

### Init-only Properties

```csharp
public class CacheMetadata
{
    public DateTime CachedAt { get; init; }
    public string Url { get; init; } = string.Empty;
}
```
