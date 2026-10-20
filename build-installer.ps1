$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$publishDirectory = Join-Path $root 'obj\installer-publish'
$distributionDirectory = Join-Path $root 'dist'
$setupPath = Join-Path $distributionDirectory 'CodexClaudeUsageWidget-Setup.exe'

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw 'The .NET 10 SDK is required to build the installer.'
}

$compiler = Get-Command ISCC.exe -ErrorAction SilentlyContinue
if ($compiler) {
    $compilerPath = $compiler.Source
}
else {
    $compilerPath = Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'
}
if (-not (Test-Path -LiteralPath $compilerPath -PathType Leaf)) {
    throw 'Inno Setup 6 is required. Install it from https://jrsoftware.org/isinfo.php'
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

$application = Join-Path $publishDirectory 'CodexClaudeUsageWidget.exe'
if (-not (Test-Path -LiteralPath $application -PathType Leaf)) {
    throw "Published executable not found: $application"
}

$version = if ($env:GITHUB_REF_NAME -match '^v(.+)$') { $Matches[1] } else { '0.0.0-local' }
Remove-Item -LiteralPath $distributionDirectory -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Path $distributionDirectory -Force | Out-Null

$setupScript = Join-Path $root 'installer\Setup.iss'
$isccArguments = @(
    "/DAppVersion=$version"
    "/DPublishDir=$publishDirectory"
    "/O$distributionDirectory"
    '/FCodexClaudeUsageWidget-Setup'
    $setupScript
)
& $compilerPath @isccArguments
if ($LASTEXITCODE -ne 0) {
    throw "Inno Setup compilation failed with exit code $LASTEXITCODE"
}
if (-not (Test-Path -LiteralPath $setupPath -PathType Leaf)) {
    throw "Setup executable not found: $setupPath"
}

$hash = (Get-FileHash -LiteralPath $setupPath -Algorithm SHA256).Hash.ToLowerInvariant()
"$hash  $(Split-Path -Leaf $setupPath)" |
    Set-Content -LiteralPath (Join-Path $distributionDirectory 'SHA256SUMS.txt') -Encoding ascii
Write-Host "Installer: $setupPath"
