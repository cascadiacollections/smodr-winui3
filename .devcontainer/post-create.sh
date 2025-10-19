#!/bin/bash

# Post-create script for devcontainer setup
set -e

echo "🚀 Running post-create setup..."

# Update package lists
echo "📦 Updating package lists..."
sudo apt-get update

# Install additional useful tools
echo "🔧 Installing additional development tools..."
sudo apt-get install -y \
    jq \
    tree \
    htop \
    ncdu

# Configure git safe directory
echo "🔒 Configuring git safe directory..."
git config --global --add safe.directory /workspaces/smodr-winui3 || true

# Restore NuGet packages (will fail on Linux for WinUI3, but useful for IntelliSense)
echo "📥 Restoring NuGet packages..."
dotnet restore smodr.sln || echo "⚠️  Note: Package restore expected to fail on Linux for WinUI3 project"

# Install/update global .NET tools
echo "🛠️  Installing .NET global tools..."
dotnet tool update --global dotnet-format || true
dotnet tool update --global dotnet-outdated-tool || true

# Ensure tools are in PATH
export PATH="$PATH:/home/vscode/.dotnet/tools"

# Display environment info
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
echo "  dotnet build     - Build solution (will fail on Linux, use Windows)"
echo "  dotnet format    - Format code"
echo "  gh pr list       - List pull requests"
echo ""
echo "💡 Ready for development!"
