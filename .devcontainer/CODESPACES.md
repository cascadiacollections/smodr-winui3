# Codespaces Configuration for smodr-winui3

This repository is optimized for GitHub Codespaces development.

## Quick Start

1. Click the "Code" button above
2. Select "Codespaces" tab
3. Click "Create codespace on main" (or your branch)
4. Wait for the environment to set up (1-2 minutes)
5. Start coding!

## What's Pre-configured

- ✅ .NET 9.0 SDK
- ✅ All recommended VS Code extensions
- ✅ Git and GitHub CLI
- ✅ Code formatting tools
- ✅ IntelliSense and code navigation

## Limitations

⚠️ **Important**: WinUI3 applications require Windows to run. Codespaces provides a Linux environment, which is perfect for:

- 📝 Code editing and review
- 🔍 Code analysis and navigation
- 📚 Documentation updates
- 🔧 Refactoring
- 🤝 Collaboration

To run and test the application, you'll need a Windows machine.

## Available Commands

```bash
# Restore packages (will show expected warnings)
dotnet restore smodr.sln

# Format code
dotnet format smodr.sln

# View available tasks
code --list-tasks

# GitHub CLI commands
gh pr list
gh issue list
```

## Tips

- Use GitHub Copilot for AI-assisted coding
- All code style rules are enforced via `.editorconfig`
- Git commands work seamlessly
- Terminal has bash completion enabled

## Resources

- [Codespaces Documentation](https://docs.github.com/en/codespaces)
- [WinUI3 Documentation](https://learn.microsoft.com/windows/apps/winui/winui3/)
- [Project README](../README.md)
