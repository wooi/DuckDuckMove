param([string]$Dotnet = 'dotnet')
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
Push-Location $projectRoot
try {
    & $Dotnet run --project tests/DuckDuckMove.Tests/DuckDuckMove.Tests.csproj -c Release
    if ($LASTEXITCODE -ne 0) { throw 'Core tests failed.' }
    & $Dotnet publish src/DuckDuckMove.App/DuckDuckMove.App.csproj -c Release -r win-x64 -o dist/win-x64
    if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
    Copy-Item -LiteralPath README.md -Destination dist/win-x64/README.md
    Copy-Item -LiteralPath LICENSE -Destination dist/win-x64/LICENSE
    Copy-Item -LiteralPath docs -Destination dist/win-x64 -Recurse -Force
    Copy-Item -LiteralPath third-party -Destination dist/win-x64 -Recurse -Force
    Copy-Item -LiteralPath samples -Destination dist/win-x64 -Recurse -Force
    Compress-Archive -Path dist/win-x64/* -DestinationPath dist/DuckDuckMove-0.1.1-win-x64.zip -Force
} finally { Pop-Location }
