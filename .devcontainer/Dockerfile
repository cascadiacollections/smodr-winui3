# Use the official .NET SDK image as base
FROM mcr.microsoft.com/dotnet/sdk:8.0

# Install additional dependencies for WinUI3 development
RUN apt-get update && export DEBIAN_FRONTEND=noninteractive \
    && apt-get -y install --no-install-recommends \
    git \
    curl \
    wget \
    vim \
    nano \
    less \
    ca-certificates \
    gnupg \
    lsb-release \
    && apt-get clean -y && rm -rf /var/lib/apt/lists/*

# Install Windows SDK build tools (for cross-platform development)
# Note: Full WinUI3 apps require Windows to run, but we can still edit, restore, and analyze code
RUN dotnet --info

# Create a non-root user
ARG USERNAME=vscode
ARG USER_UID=1000
ARG USER_GID=$USER_UID

RUN groupadd --gid $USER_GID $USERNAME \
    && useradd --uid $USER_UID --gid $USER_GID -m $USERNAME \
    && apt-get update \
    && apt-get install -y sudo \
    && echo $USERNAME ALL=\(root\) NOPASSWD:ALL > /etc/sudoers.d/$USERNAME \
    && chmod 0440 /etc/sudoers.d/$USERNAME

# Set the default user
USER $USERNAME

# Set up working directory
WORKDIR /workspace

# Install global .NET tools that might be useful
RUN dotnet tool install --global dotnet-format || true
RUN dotnet tool install --global dotnet-outdated-tool || true

ENV PATH="${PATH}:/home/${USERNAME}/.dotnet/tools"

# Keep container running
CMD ["/bin/bash"]
