#Requires -Version 5.1
<#
.SYNOPSIS
Runs a private, local Huishoudboek preview with a separate database.
.EXAMPLE
.\preview.ps1
.EXAMPLE
.\preview.ps1 -Port 5100 -NoWatch
#>
[CmdletBinding()]
param(
    [ValidateRange(1024, 65535)]
    [int]$Port = 5099,
    [switch]$NoWatch
)

$ErrorActionPreference = 'Stop'
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw 'Install the .NET 10 SDK first: https://dotnet.microsoft.com/download/dotnet/10.0'
}
$sdks = & dotnet --list-sdks
if ($LASTEXITCODE -ne 0 -or -not ($sdks -match '^10\.')) {
    throw 'The .NET 10 SDK is required: https://dotnet.microsoft.com/download/dotnet/10.0'
}
if (-not $env:LOCALAPPDATA) {
    throw 'This launcher requires Windows and LOCALAPPDATA. See README.md for the direct dotnet commands.'
}

$project = Join-Path $PSScriptRoot 'huishoudboek/source/src/Huishoudboek.Service/Huishoudboek.Service.csproj'
# Separate checkouts/worktrees must not share a writer lease or financial data.
$sha = [System.Security.Cryptography.SHA256]::Create()
try {
    $bytes = [System.Text.Encoding]::UTF8.GetBytes([System.IO.Path]::GetFullPath($PSScriptRoot).ToUpperInvariant())
    $checkoutId = ([System.BitConverter]::ToString($sha.ComputeHash($bytes))).Replace('-', '').Substring(0, 12).ToLowerInvariant()
}
finally { $sha.Dispose() }
$previewRoot = Join-Path $env:LOCALAPPDATA "Huishoudboek/Preview/$checkoutId"

$previousAspnetEnvironment = $env:ASPNETCORE_ENVIRONMENT
$previousDotnetEnvironment = $env:DOTNET_ENVIRONMENT
try {
    $env:ASPNETCORE_ENVIRONMENT = 'Development'
    $env:DOTNET_ENVIRONMENT = 'Development'
    Write-Host "Preview: http://127.0.0.1:$Port"
    Write-Host "Private preview settings and data: $previewRoot"
    Write-Host 'No login required. First start creates fictional demo data.'
    Write-Host 'Home Assistant data is not connected. Stop the preview with Ctrl+C.'

    if ($NoWatch) {
        & dotnet run --project $project --no-launch-profile -- --preview $previewRoot $Port
    }
    else {
        # Leave console input with the app; unsupported hot edits restart it automatically.
        & dotnet watch --non-interactive --project $project --no-launch-profile -- --preview $previewRoot $Port
    }
    if ($LASTEXITCODE -ne 0) { throw "Preview stopped with exit code $LASTEXITCODE." }
}
finally {
    $env:ASPNETCORE_ENVIRONMENT = $previousAspnetEnvironment
    $env:DOTNET_ENVIRONMENT = $previousDotnetEnvironment
}
