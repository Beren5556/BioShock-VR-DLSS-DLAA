#requires -Version 7
[CmdletBinding()]
param([string]$Receipt = '', [int]$TimeoutSeconds = 180)
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
if (!$Receipt) { $Receipt = Join-Path $repo 'artifacts\bs2-tests\latest-run.json' }
$run = Get-Content -LiteralPath $Receipt -Raw | ConvertFrom-Json
$root = [IO.Path]::GetFullPath($run.gameRoot)
if ($root -notmatch '^D:\\BioShock2VR-DLSS-Lab\\game-[0-9a-f-]{36}$' -or
    $run.executable -ne (Join-Path $root 'Build\Final\Bioshock2HD.exe') -or
    !$run.root.StartsWith($root + '\runs\', [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Unexpected laboratory receipt.'
}
if ($TimeoutSeconds -lt 1 -or $TimeoutSeconds -gt 600) { throw 'Invalid observation timeout.' }
$record = Get-Content -LiteralPath (Join-Path $run.root 'process.json') -Raw | ConvertFrom-Json
$owned = Get-Process -Id $record.pid -ErrorAction Stop
if ($owned.Path -ne $run.executable) { throw 'PID is not the owned laboratory game.' }
$null = $owned.Handle
Write-Output "Observing menu exit for isolated PID $($owned.Id); no input or termination is performed."
$exited = $owned.WaitForExit($TimeoutSeconds * 1000)
$exitCode = if ($exited) { $owned.ExitCode } else { $null }
$log = [IO.File]::ReadAllText($run.log)
# Use the same time boundary as the WM_CLOSE regression, but with the native
# acceptance marker. Keep earlier first-chance diagnostics explicitly visible:
# they must not be mislabelled as faults during exit, or erased from the report.
$marker = $log.IndexOf('native RequestExit accepted', [StringComparison]::Ordinal)
$exitLog = if ($marker -ge 0) { $log.Substring($marker) } else { $log }
$beforeExit = if ($marker -ge 0) { $log.Substring(0, $marker) } else { '' }
$fatalPattern = 'crash: caught via|crash: fault during|emergency process termination|forced termination for timeout|MINIDUMP WRITE FAILED'
$unhandledInRun = $log -match $fatalPattern
$fault = $exitLog -match ('first-chance AV|game-exit shutdown incomplete|' + $fatalPattern)
$complete = $exitLog -match 'game-exit shutdown complete'
$detach = $exitLog -match 'shutdown: DLL_PROCESS_DETACH'
$nativeRequest = $marker -ge 0
$saves = Get-ChildItem -LiteralPath (Join-Path $run.environment.BVR_LAB_DOCUMENTS 'BioshockHD\BioShock2\SaveGames') -Filter '*.bsb' -File |
    ForEach-Object { [ordered]@{ name=$_.Name; length=$_.Length; modifiedUtc=$_.LastWriteTimeUtc.ToString('o'); sha256=(Get-FileHash -LiteralPath $_.FullName).Hash } }
$clean = $exited -and $exitCode -eq 0 -and $nativeRequest -and $complete -and $detach -and !$fault -and !$unhandledInRun
[ordered]@{
    timeUtc=[DateTime]::UtcNow.ToString('o'); pid=$record.pid; closePath='native in-game menu'
    exited=$exited; exitCode=$exitCode; cleanExit=$clean; faultDuringExit=$fault
    unhandledFaultInRun=$unhandledInRun
    firstChanceLogLinesBeforeExit=[regex]::Matches($beforeExit, 'first-chance AV').Count
    nativeRequestAccepted=$nativeRequest; runtimeShutdownComplete=$complete; orderlyDetach=$detach
    testCoreSha256=$run.testCoreSha256; saves=@($saves); log=$run.log
} | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $run.root 'menu-close-result.json') -Encoding utf8NoBOM
Get-Content -LiteralPath (Join-Path $run.root 'menu-close-result.json') -Raw
& (Join-Path $PSScriptRoot 'Assert-BS2-RealFiles.ps1')
if (!$clean) { throw 'Menu exit did not meet clean-shutdown criteria; no process was killed.' }
