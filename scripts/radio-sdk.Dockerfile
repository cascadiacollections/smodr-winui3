# syntax=docker/dockerfile:1
# Headless portable validation only; never launches WinUI or live streams.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS dependencies
ARG RADIO_NUGET_SOURCE=https://www.nuget.org/api/v2/
ENV RADIO_NUGET_SOURCE=$RADIO_NUGET_SOURCE RADIO_NUGET_AUDIT=false RADIO_ARTIFACTS_DIR=/artifacts
WORKDIR /radio
COPY Directory.Build.props Directory.Packages.props nuget.config .editorconfig ./
COPY portable/ ./portable/
COPY Cascadia.Radio/*.csproj Cascadia.Radio/packages.lock.json ./Cascadia.Radio/
COPY Cascadia.RadioBrowser/*.csproj Cascadia.RadioBrowser/packages.lock.json ./Cascadia.RadioBrowser/
COPY Cascadia.Radio.Metadata/*.csproj Cascadia.Radio.Metadata/packages.lock.json ./Cascadia.Radio.Metadata/
COPY Cascadia.Radio.Services/*.csproj Cascadia.Radio.Services/packages.lock.json ./Cascadia.Radio.Services/
COPY Cascadia.Radio.Tests/*.csproj Cascadia.Radio.Tests/packages.lock.json ./Cascadia.Radio.Tests/
WORKDIR /radio/portable
RUN --mount=type=cache,id=shoutkit-radio-nuget,target=/root/.nuget/packages,sharing=locked \
    dotnet restore Radio.Portable.slnx --locked-mode --source "$RADIO_NUGET_SOURCE" -p:NuGetAudit=false --artifacts-path /artifacts/build
WORKDIR /radio
COPY Cascadia.Radio/ ./Cascadia.Radio/
COPY Cascadia.RadioBrowser/ ./Cascadia.RadioBrowser/
COPY Cascadia.Radio.Metadata/ ./Cascadia.Radio.Metadata/
COPY Cascadia.Radio.Services/ ./Cascadia.Radio.Services/
COPY Cascadia.Radio.Tests/ ./Cascadia.Radio.Tests/
COPY smodr.Tests/ ./smodr.Tests/
COPY smodr/Services/LiveRadioRecovery.cs smodr/Services/PlaybackEnvironmentPolicy.cs ./smodr/Services/
COPY samples/ ./samples/
COPY scripts/validate-portable-radio.sh ./scripts/
FROM dependencies AS tests
ARG RADIO_VALIDATION_RUN=cached
RUN --mount=type=cache,id=shoutkit-radio-nuget,target=/root/.nuget/packages,sharing=locked \
    printf '%s\n' "$RADIO_VALIDATION_RUN" > /artifacts/validation-run-id \
    && bash scripts/validate-portable-radio.sh --quick
FROM tests AS full
RUN --mount=type=cache,id=shoutkit-radio-nuget,target=/root/.nuget/packages,sharing=locked \
    bash scripts/validate-portable-radio.sh
CMD ["/artifacts/trimmed/RadioBrowser.TrimConsumer"]
