# syntax=docker/dockerfile:1
# Linux editing and headless builds, not a WinUI runtime.
FROM mcr.microsoft.com/devcontainers/base:noble AS tooling
RUN apt-get update \
    && apt-get install -y --no-install-recommends ca-certificates curl \
    && rm -rf /var/lib/apt/lists/* \
    && curl --fail --silent --show-error --location https://dot.net/v1/dotnet-install.sh -o /tmp/dotnet-install.sh \
    && bash /tmp/dotnet-install.sh --version 10.0.401 --install-dir /usr/share/dotnet
COPY global.json /tmp/radio-global.json
RUN sdk_version=$(sed -n 's/.*"version": "\([^"]*\)".*/\1/p' /tmp/radio-global.json) \
    && test -n "$sdk_version" \
    && bash /tmp/dotnet-install.sh --version "$sdk_version" --install-dir /usr/share/dotnet \
    && ln -s /usr/share/dotnet/dotnet /usr/local/bin/dotnet \
    && rm /tmp/dotnet-install.sh /tmp/radio-global.json \
    && mkdir -p /home/vscode/.nuget/packages /var/tmp/shoutkit-radio-artifacts \
    && chown vscode:vscode /home/vscode/.nuget /home/vscode/.nuget/packages /var/tmp/shoutkit-radio-artifacts
ENV DOTNET_ROOT=/usr/share/dotnet PATH=/usr/share/dotnet:$PATH \
    DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1 NUGET_XMLDOC_MODE=skip
FROM tooling AS validation
ARG RADIO_NUGET_SOURCE=https://www.nuget.org/api/v2/
ENV RADIO_NUGET_SOURCE=$RADIO_NUGET_SOURCE RADIO_NUGET_AUDIT=false RADIO_ARTIFACTS_DIR=/artifacts
WORKDIR /radio
COPY Directory.Build.props Directory.Packages.props nuget.config .editorconfig global.json ./
COPY portable/ ./portable/
COPY Cascadia.Radio/ ./Cascadia.Radio/
COPY Cascadia.RadioBrowser/ ./Cascadia.RadioBrowser/
COPY Cascadia.Radio.Metadata/ ./Cascadia.Radio.Metadata/
COPY Cascadia.Radio.Services/ ./Cascadia.Radio.Services/
COPY Cascadia.Radio.Tests/ ./Cascadia.Radio.Tests/
COPY smodr.Tests/ ./smodr.Tests/
COPY smodr.RadioCore/ ./smodr.RadioCore/
COPY smodr.RadioSmoke/ ./smodr.RadioSmoke/
COPY scripts/validate-portable-radio.sh ./scripts/
RUN --mount=type=cache,id=shoutkit-radio-nuget,target=/root/.nuget/packages,sharing=locked \
    bash scripts/validate-portable-radio.sh --quick --include-compat
FROM tooling AS development
