# Contributing to smodr

Thank you for your interest in contributing to smodr! This guide will help you get started.

## Getting Started

### 1. Set Up Your Development Environment

The easiest way to get started is with our dev container:

```bash
# Clone the repository
git clone https://github.com/cascadiacollections/smodr-winui3.git
cd smodr-winui3

# Open in VS Code
code .

# Reopen in container when prompted
# OR use Command Palette: Remote-Containers: Reopen in Container
```

See [DEVELOPMENT.md](DEVELOPMENT.md) for alternative setup options.

### 2. Create a Branch

```bash
git checkout -b feature/your-feature-name
# or
git checkout -b fix/your-bug-fix
```

### 3. Make Your Changes

- Follow the coding conventions defined in `.editorconfig`
- Maintain the MVVM architecture pattern
- Keep business logic in Services and ViewModels
- Minimize code in code-behind files
- Add appropriate error handling

### 4. Test Your Changes

Since this is a WinUI3 application:
- Build the solution to check for compilation errors
- Run the application on Windows to test functionality
- Verify your changes don't break existing features

### 5. Commit Your Changes

```bash
git add .
git commit -m "Brief description of your changes"
```

Use clear, descriptive commit messages:
- `feat: Add episode search functionality`
- `fix: Correct playback position tracking`
- `docs: Update installation instructions`
- `refactor: Simplify cache service implementation`

### 6. Push and Create a Pull Request

```bash
git push origin feature/your-feature-name
```

Then create a pull request on GitHub with:
- Clear description of changes
- Motivation for the changes
- Screenshots for UI changes
- Any breaking changes noted

## Code Style

### General Guidelines

- Use meaningful variable and method names
- Add XML documentation comments for public APIs
- Keep methods focused and concise
- Use modern C# features appropriately

### Naming Conventions

- **Classes/Methods/Properties**: PascalCase
- **Private fields**: _camelCase (with underscore prefix)
- **Local variables**: camelCase
- **Constants**: UPPER_SNAKE_CASE or PascalCase

### Example

```csharp
public class AudioService : IDisposable
{
    private readonly MediaPlayer _mediaPlayer;
    private string _currentUrl = string.Empty;
    
    public event EventHandler<MediaPlaybackState>? PlaybackStateChanged;
    
    /// <summary>
    /// Plays the specified media URL.
    /// </summary>
    /// <param name="url">The URL of the media to play.</param>
    public async Task PlayAsync(string url)
    {
        if (string.IsNullOrEmpty(url))
            throw new ArgumentException("URL cannot be empty", nameof(url));
            
        _currentUrl = url;
        // Implementation...
    }
}
```

## Architecture Guidelines

### MVVM Pattern

This project follows the MVVM (Model-View-ViewModel) pattern:

**Models** (`Models/`)
- Plain data objects
- No business logic
- Use records for immutability when appropriate

**Views** (`.xaml` files)
- XAML markup only
- Minimal code-behind
- Use x:Bind for data binding

**ViewModels** (`ViewModels/`)
- Implement `INotifyPropertyChanged`
- Contain presentation logic
- Commands for user actions
- No direct UI references

**Services** (`Services/`)
- Business logic and data access
- Reusable across ViewModels
- Implement `IDisposable` when managing resources

### Example ViewModel

```csharp
public class MainViewModel : INotifyPropertyChanged
{
    private readonly DataService _dataService;
    private string _status = string.Empty;
    
    public string Status
    {
        get => _status;
        set => SetProperty(ref _status, value);
    }
    
    public ICommand LoadCommand { get; }
    
    public MainViewModel()
    {
        _dataService = new DataService();
        LoadCommand = new AsyncRelayCommand(LoadAsync);
    }
    
    private async Task LoadAsync()
    {
        Status = "Loading...";
        var data = await _dataService.GetDataAsync();
        Status = "Loaded";
    }
    
    // INotifyPropertyChanged implementation...
}
```

## Areas for Contribution

### Feature Enhancements
- Additional podcast directory integration
- Playlist management
- Episode bookmarking
- Playback speed control
- Sleep timer

### Bug Fixes
- Check the issues page for bugs
- Reproduce the issue first
- Include fixes in your PR description

### Documentation
- Improve code comments
- Enhance README or guides
- Add examples

### Performance
- Optimize caching strategies
- Improve startup time
- Reduce memory usage

### UI/UX
- Enhance visual design
- Improve accessibility
- Better error messages

## Code Review Process

1. **Automated Checks**: Your PR will be checked for build success
2. **Manual Review**: A maintainer will review your code
3. **Feedback**: Address any requested changes
4. **Approval**: Once approved, your PR will be merged

## Development Tools

### VS Code Extensions (Recommended)

All recommended extensions are defined in `.vscode/extensions.json`:
- C# Dev Kit
- GitHub Copilot (optional but helpful)
- EditorConfig support
- Code Spell Checker

### GitHub Copilot

If you use GitHub Copilot, it's configured with project-specific instructions in `.github/copilot-instructions.md`. It will:
- Understand project structure
- Follow coding conventions
- Suggest WinUI3-appropriate solutions
- Maintain MVVM patterns

## Testing

Currently, the project doesn't have automated tests. If you'd like to add testing infrastructure, that would be a valuable contribution!

When adding tests:
- Use xUnit or MSTest
- Focus on ViewModels and Services
- Mock external dependencies
- Aim for meaningful test coverage

## Questions?

- Check [DEVELOPMENT.md](DEVELOPMENT.md) for setup help
- Review existing code for patterns
- Ask questions in your PR or issue
- Use GitHub Discussions for general questions

## Code of Conduct

- Be respectful and inclusive
- Provide constructive feedback
- Help others learn and grow
- Focus on what's best for the project

## License

By contributing, you agree that your contributions will be licensed under the same license as the project (see [LICENSE.txt](LICENSE.txt)).

---

Thank you for contributing to smodr! 🎉
