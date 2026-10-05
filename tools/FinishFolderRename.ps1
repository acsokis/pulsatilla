param([ValidateRange(1,1440)][int]$WaitMinutes = 120)
$ErrorActionPreference = 'Stop'
$newRoot = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
if ((Split-Path -Leaf $newRoot) -ne 'pulsatilla') { throw 'Run this helper from the renamed Pulsatilla project.' }
$oldRoot = [IO.Path]::GetFullPath((Join-Path (Split-Path -Parent $newRoot) 'netw_tool_v05'))
if ((Split-Path -Parent $oldRoot) -ne (Split-Path -Parent $newRoot) -or $oldRoot -eq $newRoot) { throw 'Unexpected cleanup paths.' }
$deadline = [DateTime]::UtcNow.AddMinutes($WaitMinutes)
do {
    if (-not (Test-Path -LiteralPath $oldRoot)) { exit 0 }
    $item = Get-Item -LiteralPath $oldRoot -Force
    if (-not $item.PSIsContainer -or ($item.Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw 'Refusing cleanup of a linked or non-directory path.' }
    if (@(Get-ChildItem -LiteralPath $oldRoot -Force).Count -ne 0) { throw 'Old folder contains files; preserving them.' }
    try { Remove-Item -LiteralPath $oldRoot -ErrorAction Stop; exit 0 }
    catch [IO.IOException] { Start-Sleep -Seconds 5 }
} while ([DateTime]::UtcNow -lt $deadline)
Write-Output 'The old empty folder is still in use. Reopen the workspace from pulsatilla, then run this helper again.'
