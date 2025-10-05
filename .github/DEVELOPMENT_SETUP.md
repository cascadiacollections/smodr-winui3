# Development Environment Setup Overview

This document provides a visual overview of the modern development setup for smodr.

## Architecture

```
┌─────────────────────────────────────────────────────────────────┐
│                         Developer                                │
└───────────────────────────────┬─────────────────────────────────┘
                                │
                ┌───────────────┴───────────────┐
                │                               │
        ┌───────▼────────┐             ┌───────▼────────┐
        │   VS Code      │             │   Visual       │
        │  + Container   │             │   Studio 2022  │
        │   (Any OS)     │             │   (Windows)    │
        └───────┬────────┘             └───────┬────────┘
                │                               │
        ┌───────▼────────┐             ┌───────▼────────┐
        │  Docker Dev    │             │   Native       │
        │  Container     │             │   Build        │
        │                │             │                │
        │ • .NET 9 SDK   │             │ • .NET 9 SDK   │
        │ • Extensions   │             │ • WinUI3 SDK   │
        │ • Settings     │             │ • Windows SDK  │
        │ • Tools        │             │                │
        └───────┬────────┘             └───────┬────────┘
                │                               │
                │     ┌──────────────────┐      │
                └────►│  Source Code     │◄─────┘
                      │  Repository      │
                      └────────┬─────────┘
                               │
                ┌──────────────┼──────────────┐
                │              │              │
        ┌───────▼──────┐ ┌────▼─────┐ ┌─────▼────────┐
        │  smodr/      │ │ Models/  │ │  Services/   │
        │  (Main App)  │ │ ViewModels│ │             │
        └──────────────┘ └──────────┘ └──────────────┘
```

## Configuration Files

```
smodr-winui3/
│
├── .devcontainer/                    🐳 Dev Container Setup
│   ├── devcontainer.json            ⚙️  Container configuration
│   └── Dockerfile                   📦 Container image definition
│
├── .github/                          🤖 GitHub Configuration
│   └── copilot-instructions.md      💡 AI assistant guidelines
│
├── .vscode/                          📝 VS Code Settings
│   ├── extensions.json              🔌 Recommended extensions
│   ├── settings.json                ⚙️  Editor settings
│   ├── tasks.json                   🔨 Build tasks
│   └── launch.json                  🐛 Debug configurations
│
├── .editorconfig                     📏 Code style rules
├── .gitignore                        🚫 Git exclusions
├── .gitattributes                    📄 Git attributes
│
├── README.md                         📖 Project overview
├── DEVELOPMENT.md                    🛠️  Setup guide
├── CONTRIBUTING.md                   🤝 Contribution guide
└── QUICKSTART.md                     ⚡ Quick reference
```

## Feature Matrix

| Feature                    | Dev Container | Windows + VS | Windows + VS Code |
|----------------------------|---------------|--------------|-------------------|
| Code Editing               | ✅            | ✅           | ✅                |
| IntelliSense               | ✅            | ✅           | ✅                |
| Build                      | ⚠️*           | ✅           | ✅                |
| Debug                      | ❌            | ✅           | ✅                |
| Run Application            | ❌            | ✅           | ✅                |
| Auto Extensions            | ✅            | ➖           | ✅                |
| Auto Settings              | ✅            | ➖           | ✅                |
| Copilot Integration        | ✅            | ✅           | ✅                |
| Cross-Platform Dev         | ✅            | ❌           | ❌                |

*Build works for code analysis but cannot produce runnable output due to Windows dependencies.

## Recommended Extensions

All automatically installed in dev container:

1. **C# Dev Kit** - Complete C# development
2. **GitHub Copilot** - AI-powered coding assistance
3. **EditorConfig** - Consistent code formatting
4. **Code Spell Checker** - Catch typos
5. **IntelliCode** - AI-assisted IntelliSense
6. **PowerShell** - Script support
7. **Markdown Lint** - Markdown validation

## Development Workflows

### Workflow 1: Code Review/Editing (Any OS)

```
1. Open in VS Code with dev container
2. Code/review with full IntelliSense
3. Use Copilot for assistance
4. Commit changes
```

**Use Case**: Code reviews, documentation, refactoring

### Workflow 2: Full Development (Windows)

```
1. Open in Visual Studio or VS Code on Windows
2. Code with full IntelliSense
3. Build the application
4. Debug and test
5. Commit changes
```

**Use Case**: Feature development, bug fixes, testing

## Quick Commands Reference

### In Dev Container

```bash
# Restore packages
dotnet restore smodr.sln

# Analyze code (won't produce runnable output)
dotnet build smodr.sln --configuration Debug

# Format code
dotnet format smodr.sln

# Clean artifacts
dotnet clean smodr.sln
```

### On Windows

```bash
# Restore packages
dotnet restore smodr.sln

# Build
dotnet build smodr.sln

# Run (from project directory)
cd smodr
dotnet run
```

### VS Code Tasks (Ctrl+Shift+B)

- **build** - Build the solution
- **clean** - Clean build artifacts
- **restore** - Restore NuGet packages
- **rebuild** - Clean and build
- **format** - Format code

## GitHub Copilot Usage

The project includes comprehensive Copilot instructions that teach it:

- Project structure and patterns
- MVVM architecture
- WinUI3 best practices
- Code conventions
- Common tasks

**To use:**
1. Open any file in the project
2. Ask Copilot questions about the code
3. It will follow project patterns automatically

**Example prompts:**
- "Add a new episode property to cache metadata"
- "Create a service method to delete episodes"
- "How do I add a new XAML page?"

## Troubleshooting

### Dev Container Won't Start
- Ensure Docker Desktop is running
- Check Docker has sufficient resources (4GB+ RAM)
- Try rebuilding: F1 → "Remote-Containers: Rebuild Container"

### Extensions Not Installing
- Wait for initial setup to complete
- Manually install from Extensions view (Ctrl+Shift+X)
- Check extension compatibility with remote containers

### IntelliSense Not Working
- Wait for OmniSharp to fully load (check status bar)
- Reload window: F1 → "Developer: Reload Window"
- Restore packages: `dotnet restore smodr.sln`

## Benefits Summary

✅ **One-Click Setup** - Dev container ready in minutes
✅ **Consistent Environment** - Everyone uses same tools
✅ **Pre-configured Tools** - No manual extension setup
✅ **AI-Powered** - Copilot knows the project
✅ **Documented** - Clear guides for all scenarios
✅ **Flexible** - Choose your preferred workflow

## Next Steps

1. **Clone the repo** and try the dev container
2. **Read DEVELOPMENT.md** for detailed setup
3. **Check CONTRIBUTING.md** before making changes
4. **Use QUICKSTART.md** for quick reference

---

*For detailed setup instructions, see [DEVELOPMENT.md](../DEVELOPMENT.md)*
