# Quick Start Guide

Get started with smodr development in under 5 minutes!

## 🚀 Fastest Path: Dev Container

**What you need:**
- VS Code
- Docker Desktop
- Remote - Containers extension

**Steps:**
```bash
git clone https://github.com/cascadiacollections/smodr-winui3.git
cd smodr-winui3
code .
```

When VS Code opens, click **"Reopen in Container"** (or press F1 → "Remote-Containers: Reopen in Container")

**That's it!** Everything is configured:
✅ .NET 10.0 SDK installed
✅ Dependencies restored
✅ Extensions installed
✅ Settings configured
✅ Ready to code!

## 🪟 Windows Development

**Prerequisites:**
- Windows 10 (version 1809+)
- Visual Studio 2022 with WinUI3 workload
- .NET 10.0 SDK

**Steps:**
```bash
git clone https://github.com/cascadiacollections/smodr-winui3.git
cd smodr-winui3
# Open smodr.sln in Visual Studio
```

Press F5 to build and run!

## 📚 Documentation

- **[DEVELOPMENT.md](DEVELOPMENT.md)** - Complete setup guide
- **[CONTRIBUTING.md](CONTRIBUTING.md)** - How to contribute
- **[copilot-instructions.md](.github/copilot-instructions.md)** - AI assistance guidelines

## 🛠️ Common Commands

```bash
# Restore dependencies
dotnet restore

# Build
dotnet build

# Clean
dotnet clean

# Format code
dotnet format
```

## 🎯 VS Code Features

- **Build**: `Ctrl+Shift+B`
- **Command Palette**: `F1`
- **Tasks**: Pre-configured in `.vscode/tasks.json`
- **Debug**: Launch configs in `.vscode/launch.json`
- **Extensions**: Recommended in `.vscode/extensions.json`

## 💡 Pro Tips

1. **Use GitHub Copilot** - It knows this project's patterns
2. **Follow EditorConfig** - Code style is automatic
3. **Check MVVM patterns** - Keep logic in ViewModels/Services
4. **Test on Windows** - WinUI3 apps require Windows to run

## 🆘 Need Help?

- Check [DEVELOPMENT.md](DEVELOPMENT.md#troubleshooting) troubleshooting section
- Review existing code for patterns
- Open an issue on GitHub

---

Happy coding! 🎉
