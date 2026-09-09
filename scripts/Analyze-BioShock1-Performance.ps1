[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$LogPath)
$ErrorActionPreference = 'Stop'
$epochs = New-Object 'System.Collections.Generic.List[object]'
$captureWindows = New-Object 'System.Collections.Generic.List[object]'
$current = $null
$announced = $null
$transition = $false
$lastTotals = $null
$autoOff = 0
$changes = 0
$rejected = 0
function New-Epoch($at, $mode, $rw, $rh, $ow, $oh) {
    [pscustomobject]@{ At=$at; Mode=$mode; Render=('{0}x{1}' -f $rw,$rh); Output=('{0}x{1}' -f $ow,$oh);
        PairRates=(New-Object 'System.Collections.Generic.List[double]') }
}
foreach ($line in Get-Content -LiteralPath $LogPath -Encoding UTF8) {
    if ($line -notmatch '^\[(\d\d:\d\d:\d\d\.\d\d\d)\]') { continue }
    $at = [TimeSpan]::Parse($Matches[1])
    if ($line -match 'stereo auto-off') { ++$autoOff }
    if ($line -match '\[dlss45\] active: render (\d+)x(\d+) -> OpenXR (\d+)x(\d+), mode=(DLAA|DLSS)') {
        $announced = New-Epoch $at $Matches[5] $Matches[1] $Matches[2] $Matches[3] $Matches[4]
        if ($null -eq $current -and -not $transition) { $current=$announced; $epochs.Add($current) }
    }
    if ($line -match '\[image-controls\] begin ') { $transition=$true; $lastTotals=$null }
    if ($line -match '\[image-controls\] APPLIED (NORMAL|DLSS|DLAA) (\d+)x(\d+) -> (\d+)x(\d+)') {
        $current=New-Epoch $at $Matches[1] $Matches[2] $Matches[3] $Matches[4] $Matches[5]
        $epochs.Add($current); $transition=$false; $lastTotals=$null; ++$changes
    }
    if ($line -match '\[image-controls\] rejected:') {
        ++$rejected
        if ($announced) { $current=New-Epoch $at $announced.Mode 0 0 0 0; $current.Render=$announced.Render; $current.Output=$announced.Output; $epochs.Add($current) }
        $transition=$false; $lastTotals=$null
    }
    if ($transition -or $null -eq $current) { continue }
    # 2nd/s is the successful second-eye build rate, NOT all Present calls.
    # Exclude menus/loading (zero second eye) and the first 3 s of each mode.
    if ($line -match '\[reentry\].* 2nd=(\d+)/s ' -and ($at-$current.At).TotalSeconds -ge 3) {
        $rate=[double]$Matches[1]; if ($rate -gt 0) { $current.PairRates.Add($rate) }
    }
    if ($line -match '\[dlss45\] totals processed=(\d+) fallback=(\d+).* copies=(\d+) ') {
        $sample=[pscustomobject]@{At=$at; Processed=[long]$Matches[1]; Fallback=[long]$Matches[2]; Copies=[long]$Matches[3]}
        if ($lastTotals) {
            $p=$sample.Processed-$lastTotals.Processed; $copies=$sample.Copies-$lastTotals.Copies
            $fallback=$sample.Fallback-$lastTotals.Fallback
            if ($p -gt 0 -and $copies -ge 0 -and $fallback -eq 0) {
                $captureWindows.Add([pscustomobject]@{At=$at.ToString(); Mode=$current.Mode; Render=$current.Render;
                    Output=$current.Output; ProcessedEyes=$p; Copies=$copies; CopiesPerProcessedEye=[Math]::Round($copies/[double]$p,3)})
            }
        }
        $lastTotals=$sample
    }
}
$summary=@(foreach($epoch in $epochs) {
    if ($epoch.PairRates.Count -lt 3) { continue }
    $stats=$epoch.PairRates | Measure-Object -Minimum -Maximum -Average
    [pscustomobject]@{Start=$epoch.At.ToString(); Mode=$epoch.Mode; Render=$epoch.Render; Output=$epoch.Output;
        Samples=$epoch.PairRates.Count; PairBuildMin=$stats.Minimum; PairBuildMax=$stats.Maximum; PairBuildMean=[Math]::Round($stats.Average,2)}
})
[pscustomobject]@{
    SourceSHA256=(Get-FileHash -LiteralPath $LogPath -Algorithm SHA256).Hash
    SuccessfulChanges=$changes; RejectedChanges=$rejected; StereoAutoOff=$autoOff
    Note='Observed build rates, not controlled headset FPS. No water/pose annotation exists. Epoch warmup 3s; at least 3 positive-rate samples. Copy windows exclude changing modes and any fallback growth.'
    Epochs=$summary; CaptureWindows=$captureWindows.ToArray()
} | ConvertTo-Json -Depth 6
