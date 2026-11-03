FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ENV DOTNET_CLI_TELEMETRY_OPTOUT=1 \
    DOTNET_NOLOGO=1
WORKDIR /source
COPY source/ ./
RUN dotnet publish src/Huishoudboek.Service/Huishoudboek.Service.csproj \
    --configuration Release --runtime linux-x64 --self-contained false \
    --output /out -p:UseAppHost=false -p:PublishSingleFile=false \
    -p:DebugType=None -p:DebugSymbols=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0
LABEL io.hass.name="Huishoudboek" \
      io.hass.description="Lokaal huishoudboek met volledige webinterface" \
      io.hass.version="0.1.0" \
      io.hass.type="addon" \
      io.hass.arch="amd64"
WORKDIR /app
COPY --from=build /out/ ./
ENV ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_EnableDiagnostics=0
ENTRYPOINT ["dotnet", "Huishoudboek.Service.dll", "--home-assistant"]
