$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$publishDirectory = Join-Path $root 'obj\installer-publish'
$distributionDirectory = Join-Path $root 'dist'
$archivePath = Join-Path $distributionDirectory 'CodexClaudeUsageWidget-Setup.zip'

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw 'The .NET 10 SDK is required to build the installer package.'
}

Remove-Item -LiteralPath $publishDirectory -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Path $publishDirectory -Force | Out-Null

dotnet publish (Join-Path $root 'UsageWidget.csproj') `
    --configuration Release `
    --runtime win-x64 `
    --self-contained true `
    --output $publishDirectory `
    -p:DebugType=None `
    -p:DebugSymbols=false
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE"
}

$executable = Join-Path $publishDirectory 'CodexClaudeUsageWidget.exe'
if (-not (Test-Path -LiteralPath $executable -PathType Leaf)) {
    throw "Published executable not found: $executable"
}
Remove-Item -LiteralPath $distributionDirectory -Recurse -Force -ErrorAction SilentlyContinue
Get-ChildItem -LiteralPath $root -Directory -Filter 'dist-*' | Remove-Item -Recurse -Force
New-Item -ItemType Directory -Path $distributionDirectory -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $root 'installer\Install.ps1') -Destination $publishDirectory
Copy-Item -LiteralPath (Join-Path $root 'installer\Uninstall.ps1') -Destination $publishDirectory
Compress-Archive -Path (Join-Path $publishDirectory '*') -DestinationPath $archivePath -CompressionLevel Optimal
Write-Host "Installer package: $archivePath"
