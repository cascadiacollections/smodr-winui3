# GitHub Copilot Instructions for smodr WinUI3 Project

## Project Overview

**smodr** is a WinUI3 desktop application for Windows built with .NET 8.0. It's a podcast/media player application that supports RSS feed parsing, episode playback, caching, and downloads.

## Technology Stack

- **Framework**: .NET 8.0 with WinUI3 (Windows App SDK)
- **Platform**: Windows 10.0.19041.0 (minimum 10.0.17763.0)
- **Architecture**: MVVM (Model-View-ViewModel)
- **Key Dependencies**:
  - Microsoft.WindowsAppSDK
  - CommunityToolkit.Mvvm
  - System.ServiceModel.Syndication (RSS parsing)

## Project Structure

```
smodr/
├── App.xaml(.cs)           # Application entry point
├── MainWindow.xaml(.cs)    # Main application window
├── Models/                 # Data models (Episode, etc.)
├── ViewModels/             # MVVM ViewModels
├── Services/               # Business logic services
│   ├── AudioService.cs     # Media playback
│   ├── DataService.cs      # RSS feed fetching
│   ├── CacheService.cs     # Episode caching
│   └── DownloadService.cs  # Episode downloads
├── Converters/             # XAML value converters
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

- Visual Studio 2022 or later with WinUI3 workload
- .NET 8.0 SDK
- Windows 10 SDK (10.0.19041.0 or later)

### Build Commands

```bash
# Restore dependencies
dotnet restore smodr.sln

# Build the solution
dotnet build smodr.sln

# Clean build artifacts
dotnet clean smodr.sln
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

### Property Change Notification

```csharp
private string _title = string.Empty;
public string Title
{
    get => _title;
    set => SetProperty(ref _title, value);
}
```

### Async Command Handling

```csharp
public ICommand LoadCommand { get; }

public MainViewModel()
{
    LoadCommand = new AsyncRelayCommand(LoadAsync);
}

private async Task LoadAsync()
{
    // Async operation
}
```

### Service Usage

```csharp
private readonly DataService _dataService;
private readonly AudioService _audioService;

public MainViewModel()
{
    _dataService = new DataService();
    _audioService = new AudioService();
}

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

- [WinUI3 Documentation](https://learn.microsoft.com/windows/apps/winui/winui3/)
- [.NET MAUI and WinUI Samples](https://github.com/microsoft/WindowsAppSDK-Samples)
- [MVVM Toolkit Documentation](https://learn.microsoft.com/dotnet/communitytoolkit/mvvm/)
