param([Parameter(Mandatory=$true)][string]$ManifestPath)
$ErrorActionPreference='Stop'
$manifest=Get-Content -LiteralPath $ManifestPath -Raw | ConvertFrom-Json
if($manifest.testFamily -notmatch '^[a-f0-9]{32}$' -or $manifest.kind -ne 'native-single-game-msi'){throw 'Isolated package required.'}
$fixture='D:\BioShock12VR-IntegrationTests\BvrMsiTest-'+$manifest.testFamily
$msi=Join-Path (Split-Path $ManifestPath) $manifest.installer
if((Get-FileHash -LiteralPath $msi).Hash -ne $manifest.sha256){throw 'Unexpected MSI.'}
$product='{'+$manifest.games.bs1.productCode+'}'
$game=Join-Path $fixture 'bs1\Game\Build\Final'
$args='/i "'+$msi+'" TRANSFORMS=:bs1 MSINEWINSTANCE=1 /qn BVR_TESTROOT="'+$fixture+'" BVR_BS1_GAMEPATH="'+$game+'" /l*v "'+$fixture+'\plan-install.log"'
$p=Start-Process msiexec.exe -ArgumentList $args -WindowStyle Hidden -PassThru
if(-not $p.WaitForExit(60000) -or $p.ExitCode -ne 0){throw 'Fixture install failed.'}
try{
    $output=& (Join-Path (Split-Path $msi) 'work\Test-Selection.exe') $msi $fixture ('bs1|'+$product) 2>&1
    $result=[ordered]@{exitCode=$LASTEXITCODE;output=($output|Out-String)}
    [IO.File]::WriteAllText((Join-Path $fixture 'plan-result.json'),($result|ConvertTo-Json),[Text.UTF8Encoding]::new($false))
}finally{
    $p=Start-Process msiexec.exe -ArgumentList ('/x '+$product+' /qn BVR_TESTROOT="'+$fixture+'" /l*v "'+$fixture+'\plan-cleanup.log"') -WindowStyle Hidden -PassThru
    if(-not $p.WaitForExit(60000) -or $p.ExitCode -ne 0){throw 'Fixture cleanup failed.'}
}
