#requires -Version 7
[CmdletBinding()]
param([string]$Receipt = '', [switch]$ExpectClean)
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
if (!$Receipt) { $Receipt = Join-Path $repo 'artifacts\bs2-tests\latest-run.json' }
$run = Get-Content -LiteralPath $Receipt -Raw | ConvertFrom-Json
$root = [IO.Path]::GetFullPath($run.gameRoot)
if ($root -notmatch '^D:\\BioShock2VR-DLSS-Lab\\game-[0-9a-f-]{36}$') { throw 'Unexpected test root.' }
$expected = Join-Path $root 'Build\Final\Bioshock2HD.exe'
if ($run.executable -ne $expected -or !$run.root.StartsWith($root + '\runs\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Unexpected run paths.' }
$processRecord = Get-Content -LiteralPath (Join-Path $run.root 'process.json') -Raw | ConvertFrom-Json
$testProcess = Get-Process -Id $processRecord.pid -ErrorAction Stop
if ($testProcess.Path -ne $expected) { throw 'PID no longer belongs to this test game.' }
$null = $testProcess.Handle # Hold a handle so ExitCode remains available after exit.
$requested = $testProcess.CloseMainWindow()
if (!$requested -or !$testProcess.WaitForExit(25000)) { throw 'Normal close did not complete within 25 seconds. No process was killed.' }
$exitCode = $testProcess.ExitCode
$logText = if (Test-Path -LiteralPath $run.log) { [IO.File]::ReadAllText($run.log) } else { '' }
$closeMarker = $logText.LastIndexOf('crash: BS2 close requested', [StringComparison]::Ordinal)
$exitText = if ($closeMarker -ge 0) { $logText.Substring($closeMarker) } else { $logText }
$teardown = $exitText -match 'crash: fault during|emergency process termination|forced termination for timeout'
$firstChance = $exitText -match 'first-chance AV|crash: caught via|crash: minidump|MINIDUMP WRITE FAILED'
$orderlyDetach = $exitText -match 'shutdown: DLL_PROCESS_DETACH'
$runtimeComplete = $exitText -match 'game-exit shutdown complete'
$clean = $exitCode -eq 0 -and $orderlyDetach -and $runtimeComplete -and !$teardown -and !$firstChance
[ordered]@{
    timeUtc = [DateTime]::UtcNow.ToString('o')
    pid = $processRecord.pid
    wmClose = $requested
    exited = $true
    exitCode = $exitCode
    teardownFaultInLog = $teardown
    firstChanceFaultInLog = $firstChance
    orderlyDetach = $orderlyDetach
    runtimeShutdownComplete = $runtimeComplete
    cleanExit = $clean
    log = $run.log
} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $run.root 'close-result.json') -Encoding utf8NoBOM
Get-Content -LiteralPath (Join-Path $run.root 'close-result.json') -Raw
& (Join-Path $PSScriptRoot 'Assert-BS2-RealFiles.ps1')
if ($ExpectClean -and !$clean) { throw 'Exit failed clean-shutdown criteria; inspect close-result.json and the preserved log.' }
