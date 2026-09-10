#requires -Version 7
[CmdletBinding()]
param([string]$Receipt = '',[switch]$SelfTestOnly,[switch]$Visible)
$ErrorActionPreference='Stop'
$repo=Split-Path -Parent $PSScriptRoot
if (!$Receipt) { $Receipt=Join-Path $repo 'artifacts\bs2-tests\latest-run.json' }
$run=Get-Content -LiteralPath $Receipt -Raw | ConvertFrom-Json
if ($run.gameRoot -notmatch '^D:\\BioShock2VR-DLSS-Lab\\game-[0-9a-f-]{36}$') { throw 'Unexpected test root.' }
if (Get-Process -Name Bioshock2HD -ErrorAction SilentlyContinue) {
    throw 'BioShock 2 is already running.'
}
$start=[Diagnostics.ProcessStartInfo]::new()
$start.FileName=if($SelfTestOnly){Join-Path $run.root 'bin\xr_hello32.exe'}else{$run.executable}
$start.WorkingDirectory=Split-Path -Parent $start.FileName
$start.UseShellExecute=$false
$start.CreateNoWindow=$true
$start.WindowStyle=[Diagnostics.ProcessWindowStyle]::Hidden
if($Visible -and !$SelfTestOnly) { $start.WindowStyle=[Diagnostics.ProcessWindowStyle]::Normal }
if (!$SelfTestOnly) { $start.ArgumentList.Add('-windowed'); $start.ArgumentList.Add('-nointro') }
foreach($property in $run.environment.PSObject.Properties) { $start.Environment[$property.Name]=[string]$property.Value }
if($SelfTestOnly) { $start.RedirectStandardOutput=$true; $start.RedirectStandardError=$true }
$process=[Diagnostics.Process]::Start($start)
if($SelfTestOnly) {
    $stdout=$process.StandardOutput.ReadToEndAsync()
    $stderr=$process.StandardError.ReadToEndAsync()
    if (!$process.WaitForExit(30000)) { $process.Kill(); throw 'XR simulator self-test timed out.' }
    $stdout.Result
    $stderr.Result
    if ($process.ExitCode -ne 0 -or $stdout.Result -notmatch "FULL PASS.*bvr-xrsim") { throw 'Simulator self-test failed.' }
} else {
    [ordered]@{pid=$process.Id;executable=$run.executable;startedUtc=[DateTime]::UtcNow.ToString('o');receipt=$Receipt} |
        ConvertTo-Json | Set-Content -LiteralPath (Join-Path $run.root 'process.json') -Encoding utf8NoBOM
    "Started isolated BioShock 2 PID $($process.Id). Log: $($run.log)"
}
