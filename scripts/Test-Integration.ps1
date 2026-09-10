[CmdletBinding()]
param([string]$BuildDirectory = '', [int]$TimeoutSeconds = 45)
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if (-not $BuildDirectory) { $BuildDirectory = Join-Path $repo 'artifacts\integration-0.2.17\build\src\Release' }
$reportRoot = Join-Path $repo ('artifacts\integration-0.2.17\tests\' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
New-Item -ItemType Directory -Path $reportRoot -Force | Out-Null
$results = @()
$names = @('image_controls_test','image_controls_controller_test','stereo_recovery_test',
    'graphics_options_test','resolution_mailbox_test','close_policy_test32','game_exit_gate_test32',
    'bvr_bs2_exit_guard_test','temporal_guides_test32','temporal_guides_bs2_test32','dlss_overlap_test32','bs2_config_test')
foreach ($name in $names) {
    $exe = [IO.Path]::GetFullPath((Join-Path $BuildDirectory ($name + '.exe')))
    if (-not (Test-Path -LiteralPath $exe -PathType Leaf)) { throw "Build required: $name" }
    $stdout = Join-Path $reportRoot ($name + '.log')
    $stderr = Join-Path $reportRoot ($name + '.err.log')
    $process = Start-Process -FilePath $exe -WorkingDirectory $BuildDirectory -WindowStyle Hidden -PassThru -RedirectStandardOutput $stdout -RedirectStandardError $stderr
    $done = $process.WaitForExit($TimeoutSeconds * 1000)
    if (-not $done) {
        $current = Get-Process -Id $process.Id -ErrorAction SilentlyContinue
        if ($current -and $current.Path -eq $exe) { Stop-Process -Id $process.Id -Force }
        $results += [pscustomobject]@{ name=$name; exitCode=$null; passed=$false; timedOut=$true; log=$stdout }
    } else {
        $results += [pscustomobject]@{ name=$name; exitCode=$process.ExitCode; passed=($process.ExitCode -eq 0); timedOut=$false; log=$stdout }
    }
    Write-Output "$name : $($results[-1].passed)"
}
$report = Join-Path $reportRoot 'results.json'
[IO.File]::WriteAllText($report, ($results | ConvertTo-Json -Depth 4), (New-Object Text.UTF8Encoding($false)))
Write-Output "Report: $report"
if (@($results | Where-Object { -not $_.passed }).Count) { throw 'Some tests failed; inspect their logs.' }
