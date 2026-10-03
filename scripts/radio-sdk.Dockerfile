# Independent, headless stable-runtime verification; never runs the Windows app.
FROM mcr.microsoft.com/dotnet/sdk:10.0
WORKDIR /radio
COPY Directory.Build.props Directory.Packages.props nuget.config .editorconfig ./
COPY Cascadia.Radio/ ./Cascadia.Radio/
COPY Cascadia.RadioBrowser/ ./Cascadia.RadioBrowser/
COPY Cascadia.Radio.Metadata/ ./Cascadia.Radio.Metadata/
COPY Cascadia.Radio.Services/ ./Cascadia.Radio.Services/
COPY Cascadia.Radio.Tests/ ./Cascadia.Radio.Tests/
COPY smodr.Tests/ ./smodr.Tests/
COPY samples/ ./samples/
WORKDIR /radio/Cascadia.Radio.Tests
RUN dotnet restore --locked-mode --source https://www.nuget.org/api/v2/ -p:NuGetAudit=false --artifacts-path /tmp/radio-artifacts
RUN dotnet test -c Release --no-restore -warnaserror --artifacts-path /tmp/radio-artifacts
RUN dotnet pack ../Cascadia.Radio/Cascadia.Radio.csproj -c Release --no-restore --artifacts-path /tmp/radio-artifacts -o /feed -warnaserror \
    && dotnet pack ../Cascadia.Radio.Metadata/Cascadia.Radio.Metadata.csproj -c Release --no-restore --artifacts-path /tmp/radio-artifacts -o /feed -warnaserror \
    && dotnet pack ../Cascadia.Radio.Services/Cascadia.Radio.Services.csproj -c Release --no-restore --artifacts-path /tmp/radio-artifacts -o /feed -warnaserror \
    && dotnet pack ../Cascadia.RadioBrowser/Cascadia.RadioBrowser.csproj -c Release --no-restore --artifacts-path /tmp/radio-artifacts -o /feed -warnaserror
WORKDIR /radio/samples/RadioSdk.Consumer
RUN dotnet restore --source /feed -p:NuGetAudit=false --artifacts-path /tmp/radio-artifacts \
    && dotnet run -c Release --no-restore --artifacts-path /tmp/radio-artifacts
WORKDIR /radio/samples/RadioBrowser.TrimConsumer
RUN dotnet publish -c Release -r linux-arm64 --self-contained --source /feed --source https://www.nuget.org/api/v2/ -p:NuGetAudit=false -o /trimmed --artifacts-path /tmp/radio-artifacts -warnaserror \
    && /trimmed/RadioBrowser.TrimConsumer
CMD ["/trimmed/RadioBrowser.TrimConsumer"]
