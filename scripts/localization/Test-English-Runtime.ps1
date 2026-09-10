[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$BasePayloadRoot)
$ErrorActionPreference='Stop'
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
if(Get-Process BioshockHD,Bioshock2HD -ErrorAction SilentlyContinue){throw 'Close the game before the isolated GPU test.'}
$root=Join-Path $repo ('artifacts\integration-0.2.17\runtime-tests\'+(Get-Date -Format 'yyyyMMdd-HHmmss'))
$testExe=Join-Path $repo 'artifacts\integration-0.2.17\build\src\Release\dlss_runtime_test32.exe'
$reports=@()
foreach($game in @('bs1','bs2')){
    $fixture=Join-Path $root $game
    $hostDir=Join-Path $fixture 'host64'
    $data=Join-Path $fixture 'data'
    New-Item -ItemType Directory -Path $hostDir,$data -Force | Out-Null
    $manifest=Get-Content -LiteralPath (Join-Path $BasePayloadRoot "manifest-$game.json") -Raw | ConvertFrom-Json
    $published=Get-Content -LiteralPath (Join-Path $repo 'release\manifest-v0.2.16.json') -Raw | ConvertFrom-Json
    foreach($name in @('BioShockVR-DLSS45-Host64.exe','nvngx_dlss.dll','dlss-capabilities.ini')){
        $relative='host64\'+$name
        $entry=@($manifest.files | Where-Object {$_.path.Replace('/','\') -eq $relative})
        $pin=@($published.games.$game.files | Where-Object {$_.path.Replace('/','\') -eq $relative})
        $source=Join-Path (Join-Path $BasePayloadRoot $game) $relative
        if($entry.Count -ne 1 -or $pin.Count -ne 1 -or $entry[0].sha256 -ne $pin[0].sha256 -or (Get-FileHash -LiteralPath $source).Hash -ne $pin[0].sha256){throw "Unverified $game host input: $name"}
        Copy-Item -LiteralPath $source -Destination (Join-Path $hostDir $name)
    }
    $log=Join-Path $fixture 'transitions.log'
    $process=Start-Process -FilePath $testExe -ArgumentList @($game,('"'+(Join-Path $hostDir 'BioShockVR-DLSS45-Host64.exe')+'"'),('"'+$data+'"')) -WindowStyle Hidden -PassThru -RedirectStandardOutput $log -RedirectStandardError (Join-Path $fixture 'transitions.err')
    if(-not $process.WaitForExit(55000)){throw "GPU test still running, PID $($process.Id); inspect before continuing."}
    $reports+= [pscustomobject]@{game=$game;exitCode=$process.ExitCode;passed=($process.ExitCode -eq 0);log=$log}
    Get-Content -LiteralPath $log -Tail 5
}
[IO.File]::WriteAllText((Join-Path $root 'results.json'),($reports | ConvertTo-Json -Depth 4),(New-Object Text.UTF8Encoding($false)))
if(@($reports | Where-Object {-not $_.passed}).Count){throw "GPU test failed; see $root"}
Write-Output "PASS: both actual mod clients and verified NVIDIA runtime; report $root"
