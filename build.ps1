$ErrorActionPreference = 'Stop'

dotnet publish .\PortalMonitor.csproj `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:DebugType=None `
    -p:DebugSymbols=false `
    -o .\publish

Write-Host "Gotowe: $PWD\publish\PortalMonitor.exe"
