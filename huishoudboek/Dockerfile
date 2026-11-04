FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG TARGETARCH
ENV DOTNET_CLI_TELEMETRY_OPTOUT=1 \
    DOTNET_NOLOGO=1
WORKDIR /source
COPY source/ ./
RUN case "$TARGETARCH" in \
      "amd64") RID="linux-x64" ;; \
      "arm64") RID="linux-arm64" ;; \
      "arm") RID="linux-arm" ;; \
      "386") RID="linux-x86" ;; \
      *) echo "Unsupported architecture: $TARGETARCH" && exit 1 ;; \
    esac && \
    dotnet publish src/Huishoudboek.Service/Huishoudboek.Service.csproj \
    --configuration Release --runtime "$RID" --self-contained false \
    --output /out -p:UseAppHost=false -p:PublishSingleFile=false \
    -p:DebugType=None -p:DebugSymbols=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0
LABEL io.hass.name="Huishoudboek" \
      io.hass.description="Lokaal huishoudboek met volledige webinterface" \
      io.hass.version="0.1.10" \
      io.hass.type="addon" \
      io.hass.arch="aarch64|amd64|armhf|armv7|i386"
WORKDIR /app
COPY --from=build /out/ ./
ENV ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_EnableDiagnostics=0
ENTRYPOINT ["dotnet", "Huishoudboek.Service.dll", "--home-assistant"]
