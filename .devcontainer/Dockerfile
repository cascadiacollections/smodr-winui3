# Use the official .NET SDK image as base
ARG VARIANT="9.0"
FROM mcr.microsoft.com/dotnet/sdk:${VARIANT}

# Avoid warnings by switching to noninteractive
ENV DEBIAN_FRONTEND=noninteractive

# Install additional dependencies for WinUI3 development
RUN apt-get update && apt-get -y install --no-install-recommends \
    git \
    curl \
    wget \
    vim \
    nano \
    less \
    ca-certificates \
    gnupg \
    lsb-release \
    bash-completion \
    zsh \
    && apt-get clean -y && rm -rf /var/lib/apt/lists/*

# Create a non-root user
ARG USERNAME=vscode
ARG USER_UID=1000
ARG USER_GID=$USER_UID

RUN groupadd --gid $USER_GID $USERNAME \
    && useradd --uid $USER_UID --gid $USER_GID -m $USERNAME -s /bin/bash \
    && apt-get update \
    && apt-get install -y sudo \
    && echo $USERNAME ALL=\(root\) NOPASSWD:ALL > /etc/sudoers.d/$USERNAME \
    && chmod 0440 /etc/sudoers.d/$USERNAME \
    && apt-get clean -y && rm -rf /var/lib/apt/lists/*

# Switch to non-root user
USER $USERNAME

# Set up working directory
WORKDIR /workspaces/smodr-winui3

# Install global .NET tools
RUN dotnet tool install --global dotnet-format --version 9.* || true \
    && dotnet tool install --global dotnet-outdated-tool || true

# Add .NET tools to PATH
ENV PATH="${PATH}:/home/${USERNAME}/.dotnet/tools"

# Configure .NET environment
ENV DOTNET_CLI_TELEMETRY_OPTOUT=1 \
    DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1 \
    DOTNET_NOLOGO=1 \
    NUGET_XMLDOC_MODE=skip

# Display .NET info (helps verify the image is working)
RUN dotnet --info

# Keep container running
CMD ["/bin/bash"]

