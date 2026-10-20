$ErrorActionPreference = 'Stop'
$packageExe = Join-Path $PSScriptRoot 'CodexClaudeUsageWidget.exe'
if (-not (Test-Path -LiteralPath $packageExe -PathType Leaf)) {
    throw "Application executable not found: $packageExe"
}

$installDirectory = Join-Path $env:LOCALAPPDATA 'Programs\CodexClaudeUsageWidget'
$startMenuDirectory = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs'
$shortcutPath = Join-Path $startMenuDirectory 'Codex + Claude Usage Widget.lnk'
New-Item -ItemType Directory -Path $installDirectory -Force | Out-Null
Get-ChildItem -LiteralPath $installDirectory -File | Remove-Item -Force
Get-ChildItem -LiteralPath $PSScriptRoot -File |
    Where-Object { $_.Extension -notin '.ps1', '.pdb' } |
    Copy-Item -Destination $installDirectory -Force

$shortcut = (New-Object -ComObject WScript.Shell).CreateShortcut($shortcutPath)
$shortcut.TargetPath = Join-Path $installDirectory 'CodexClaudeUsageWidget.exe'
$shortcut.WorkingDirectory = $installDirectory
$shortcut.Description = 'Codex and Claude usage widget'
$shortcut.Save()
Write-Host "Installed to $installDirectory"
Write-Host "Start menu shortcut: $shortcutPath"
