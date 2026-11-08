param([string]$GameDirectory = 'E:\SteamLibrary\steamapps\common\Slay the Spire 2')
$ErrorActionPreference = 'Stop'
dotnet build (Join-Path $PSScriptRoot 'TacticalRequests.csproj') -c Release "-p:GameDirectory=$GameDirectory" -p:EnableSoloTesting=false --nologo
if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
dotnet run --project (Join-Path $PSScriptRoot 'tests\TacticalRequests.Tests.csproj') -c Release
if ($LASTEXITCODE -ne 0) { throw 'Logic tests failed' }
$package = Join-Path $PSScriptRoot 'dist\TacticalRequests'
New-Item -ItemType Directory -Force $package | Out-Null
$allowedFiles = @('TacticalRequests.dll', 'TacticalRequests.json', 'README.md', 'HELP-CATALOG.md')
$unexpectedFiles = @(Get-ChildItem -LiteralPath $package -Force | Where-Object {
    $_.PSIsContainer -or $_.Name -notin $allowedFiles
})
if ($unexpectedFiles.Count -gt 0) {
    throw ('Package contains unapproved files or directories: ' + ($unexpectedFiles.Name -join ', '))
}
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'bin\Release\net9.0\TacticalRequests.dll') -Destination $package -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'TacticalRequests.json'), (Join-Path $PSScriptRoot 'README.md'), (Join-Path $PSScriptRoot 'HELP-CATALOG.md') -Destination $package -Force
$version = (Get-Content -Raw -LiteralPath (Join-Path $PSScriptRoot 'TacticalRequests.json') | ConvertFrom-Json).version
$archive = Join-Path $PSScriptRoot "dist\TacticalRequests-$version.zip"
Compress-Archive -LiteralPath $package -DestinationPath $archive -Force
Get-FileHash -Algorithm SHA256 -LiteralPath $archive






