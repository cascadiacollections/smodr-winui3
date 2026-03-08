#!/bin/bash

# Post-create script for devcontainer setup
set -e

echo "🚀 Running post-create setup..."

# Configure git safe directory
git config --global --add safe.directory /workspaces/smodr-winui3 || true

# Restore NuGet packages (will fail on Linux for WinUI3, but useful for IntelliSense)
echo "📥 Restoring NuGet packages..."
dotnet restore smodr.sln || echo "⚠️  Note: Package restore expected to fail on Linux for WinUI3 project"

# Install .NET global tools
echo "🛠️  Installing .NET global tools..."
dotnet tool install --global dotnet-outdated-tool || true

echo ""
echo "✅ Post-create setup complete!"
echo ""
echo "📊 Environment Information:"
echo "  .NET Version: $(dotnet --version)"
echo "  Git Version: $(git --version)"
echo "  GitHub CLI: $(gh --version | head -n 1)"
echo ""
echo "🎯 Available commands:"
echo "  dotnet restore   - Restore packages"
echo "  dotnet build     - Build solution (requires Windows for WinUI3)"
echo "  dotnet format    - Format code"
echo "  gh pr list       - List pull requests"
echo ""
echo "💡 Ready for development!"
