#requires -Version 7
[CmdletBinding()]
param(
    [ValidateSet('off','dlaa','sr')][string]$Mode = 'off',
    [switch]$SkipXR,
    [switch]$LoadGame,
    [int]$Render = 1024,
    [int]$Output = 1536
)
$ErrorActionPreference = 'Stop'
if ($LoadGame -and $SkipXR) { throw 'LoadGame uses the laboratory XR controller and requires XR.' }
$repo = Split-Path -Parent $PSScriptRoot
& (Join-Path $PSScriptRoot 'Assert-BS2-RealFiles.ps1')
& (Join-Path $PSScriptRoot 'Prepare-BS2-TestRun.ps1') -Mode $Mode -Render $Render -Output $Output | Out-Null
$receipt = Join-Path $repo 'artifacts\bs2-tests\latest-run.json'
$run = Get-Content -LiteralPath $receipt -Raw | ConvertFrom-Json
# Child-only environment. Preserve the caller's previous values even on error.
$priorVeh = [Environment]::GetEnvironmentVariable('BVR_VEH', 'Process')
$priorSkip = [Environment]::GetEnvironmentVariable('BVR_SKIP', 'Process')
$ownedProcess = $null
function Read-LiveLog([string]$Path) {
    if (!(Test-Path -LiteralPath $Path)) { return '' }
    $stream = $null
    $reader = $null
    try {
        $stream = [IO.File]::Open($Path, [IO.FileMode]::Open, [IO.FileAccess]::Read,
            ([IO.FileShare]::ReadWrite -bor [IO.FileShare]::Delete))
        $reader = [IO.StreamReader]::new($stream)
        return $reader.ReadToEnd()
    } catch [IO.IOException] {
        return '' # The logger can briefly rotate/open its file during startup.
    } finally {
        if ($reader) { $reader.Dispose() }
        elseif ($stream) { $stream.Dispose() }
    }
}
try {
    $env:BVR_VEH = '1'
    [Environment]::SetEnvironmentVariable('BVR_SKIP', $(if($SkipXR){'xr'}else{$null}), 'Process')
    & (Join-Path $PSScriptRoot 'Start-BS2-TestRun.ps1') -Receipt $receipt -Visible
    $processRecord = Get-Content -LiteralPath (Join-Path $run.root 'process.json') -Raw | ConvertFrom-Json
    $ownedProcess = Get-Process -Id $processRecord.pid -ErrorAction Stop
    # Windows can return no MainModule/Path while the x86 loader is still
    # starting. Wait only for a missing value; never accept a different path.
    for ($probe = 0; $probe -lt 20 -and !$ownedProcess.Path; ++$probe) {
        Start-Sleep -Milliseconds 100
        $ownedProcess = Get-Process -Id $processRecord.pid -ErrorAction Stop
    }
    if ($ownedProcess.Path -ne $run.executable) { throw 'PID does not belong to this lab run.' }
    $null = $ownedProcess.Handle
    $clock = [Diagnostics.Stopwatch]::StartNew()
    $readyAt = $null
    while ($clock.Elapsed.TotalSeconds -lt 45) {
        $ownedProcess.Refresh()
        if ($ownedProcess.HasExited) { throw "Game exited during startup: $($ownedProcess.ExitCode)" }
        $log = Read-LiveLog $run.log
        $rendering = $log -match '\[reentry\] beat:'
        if ($SkipXR) { $rendering = $rendering -and $log -match 'OpenXR instance NOT created' }
        else {
            $rendering = $rendering -and $log -match 'xr: session running'
            if ($Mode -ne 'off') {
                $rendering = $rendering -and $log -match '\[dlss45\] eye0 ready:' -and
                    $log -match '\[dlss45\] eye1 ready:'
            }
        }
        if ($ownedProcess.Responding -and $ownedProcess.MainWindowHandle -ne 0 -and $rendering) {
            if ($null -eq $readyAt) { $readyAt = $clock.Elapsed.TotalSeconds }
            if ($clock.Elapsed.TotalSeconds - $readyAt -ge 5) { break }
        } else { $readyAt = $null }
        Start-Sleep -Milliseconds 250
    }
    if ($null -eq $readyAt -or $clock.Elapsed.TotalSeconds - $readyAt -lt 5) {
        throw 'The laboratory game did not reach a stable rendering window.'
    }
    if ($LoadGame) {
        & (Join-Path $repo 'tools\xrsim-cmd.ps1') -Dir $run.sim -Quiet 'btn a press 500' | Out-Null
        $playClock = [Diagnostics.Stopwatch]::StartNew()
        $playingAt = $null
        while ($playClock.Elapsed.TotalSeconds -lt 60) {
            $ownedProcess.Refresh()
            if ($ownedProcess.HasExited) { throw 'Game exited while loading the copied save.' }
            $log = Read-LiveLog $run.log
            $playReady = $log -match '\[b2r\] input drive: [1-9][0-9]*/s'
            if ($Mode -ne 'off') {
                $totals = [regex]::Matches($log, '\[dlss45\] totals processed=(\d+)')
                $playReady = $playReady -and $totals.Count -gt 0 -and
                    [long]$totals[$totals.Count-1].Groups[1].Value -ge 200 -and
                    $log -match 'eye=L [^\r\n]*coherent=1 hist=1 reject=none' -and
                    $log -match 'eye=R [^\r\n]*coherent=1 hist=1 reject=none'
            }
            if ($playReady) {
                if ($null -eq $playingAt) { $playingAt = $playClock.Elapsed.TotalSeconds }
                if ($playClock.Elapsed.TotalSeconds - $playingAt -ge 5) { break }
            } else { $playingAt = $null }
            Start-Sleep -Milliseconds 250
        }
        if ($null -eq $playingAt -or $playClock.Elapsed.TotalSeconds - $playingAt -lt 5) {
            throw 'Copied save did not reach active gameplay; no close was attempted.'
        }
    }
    [ordered]@{
        purpose = 'BS2 clean exit regression'; mode=$Mode; skipXR=[bool]$SkipXR
        observer='BVR_VEH=1'; closePath=$(if($LoadGame){'WM_CLOSE during copied gameplay'}else{'WM_CLOSE from main menu'})
        loadedGame=[bool]$LoadGame
        readyAfterSeconds=$clock.Elapsed.TotalSeconds; testCoreSha256=$run.testCoreSha256
    } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $run.root 'exit-test.json') -Encoding utf8NoBOM
    & (Join-Path $PSScriptRoot 'Close-BS2-TestRun.ps1') -Receipt $receipt -ExpectClean
    "PASS: clean BS2 exit, mode=$Mode skipXR=$SkipXR, evidence=$($run.root)"
} finally {
    [Environment]::SetEnvironmentVariable('BVR_VEH', $priorVeh, 'Process')
    [Environment]::SetEnvironmentVariable('BVR_SKIP', $priorSkip, 'Process')
    # Deliberately no process-name kill here. A failed run stays available for
    # diagnosis; the caller may terminate only its verified, unsaved lab PID.
    & (Join-Path $PSScriptRoot 'Assert-BS2-RealFiles.ps1')
}
