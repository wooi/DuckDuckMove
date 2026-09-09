param([string]$Dotnet = 'dotnet', [string]$PayloadDirectory = 'dist/win-x64')
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
Push-Location $projectRoot
try {
    & $Dotnet run --project tests/DuckDuckMove.Tests/DuckDuckMove.Tests.csproj -c Release
    if ($LASTEXITCODE -ne 0) { throw 'Core tests failed.' }
    & $Dotnet publish src/DuckDuckMove.App/DuckDuckMove.App.csproj -c Release -r win-x64 -o $PayloadDirectory
    if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
    Copy-Item -LiteralPath README.md -Destination (Join-Path $PayloadDirectory 'README.md')
    Get-ChildItem -Path README.*.md | Copy-Item -Destination $PayloadDirectory -Force
    Copy-Item -LiteralPath LICENSE -Destination $PayloadDirectory
    Copy-Item -LiteralPath docs -Destination $PayloadDirectory -Recurse -Force
    Copy-Item -LiteralPath third-party -Destination $PayloadDirectory -Recurse -Force
    Copy-Item -LiteralPath samples -Destination $PayloadDirectory -Recurse -Force
    [xml]$project = Get-Content src/DuckDuckMove.App/DuckDuckMove.App.csproj
    $version = ($project.Project.PropertyGroup | Where-Object Version | Select-Object -First 1).Version
    Compress-Archive -Path (Join-Path $PayloadDirectory '*') -DestinationPath "dist/DuckDuckMove-$version-win-x64.zip" -Force
} finally { Pop-Location }
