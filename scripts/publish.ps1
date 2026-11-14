# Builds a single self-contained CamBar.exe into ./publish (no .NET install needed to run it).
$ErrorActionPreference = 'Stop'
Set-Location (Join-Path $PSScriptRoot '..')
dotnet publish src/CamBar.App -c Release -r win-x64 --self-contained `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish
Write-Host "OK: $(Resolve-Path publish/CamBar.exe)"
