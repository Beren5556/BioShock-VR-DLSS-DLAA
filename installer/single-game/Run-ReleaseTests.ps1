[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][ValidatePattern('^[a-f0-9]{32}$')][string]$TestFamily,
    [Parameter(Mandatory=$true)][string]$ExpectedUserSid,
    [Parameter(Mandatory=$true)][string]$Bs1Exe,
    [Parameter(Mandatory=$true)][string]$Bs2Exe,
    [string]$FailedFixture = ''
)
$ErrorActionPreference='Stop'
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$identity=[Security.Principal.WindowsIdentity]::GetCurrent()
$principal=New-Object Security.Principal.WindowsPrincipal($identity)
if($identity.User.Value -ne $ExpectedUserSid -or -not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'Ejecutar con elevación normal y la misma cuenta del usuario, no con otra cuenta.'
}
$packageRoot=Join-Path $repo "artifacts\single-game-isolated\$TestFamily\single-game-0.2.16"
$manifestPath=Join-Path $packageRoot 'manifest.json'
$manifest=Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
$msi=Join-Path $packageRoot $manifest.installer
if($manifest.testFamily -ne $TestFamily -or $manifest.kind -ne 'native-single-game-msi' -or (Get-FileHash -LiteralPath $msi).Hash -ne $manifest.sha256) { throw 'Solo se acepta el MSI aislado verificado.' }
$report=[ordered]@{passed=$false;error='';sameUserElevated=$true;recoveredFixture='';standard='';upgrade=''}
$batch='D:\BioShock12VR-IntegrationTests\Release-0.2.16-'+(Get-Date -Format 'yyyyMMdd-HHmmss')
$transcript=Join-Path $packageRoot 'release-tests-elevated.log'
Start-Transcript -LiteralPath $transcript -Force | Out-Null
try {
    if($FailedFixture) {
        $fixture=[IO.Path]::GetFullPath($FailedFixture).TrimEnd('\')
        if([IO.Path]::GetFileName($fixture) -ne ('BvrMsiTest-'+$TestFamily) -or -not(Test-Path -LiteralPath $fixture -PathType Container)) { throw 'Carpeta de recuperación incorrecta.' }
        # Recover only the explicitly named failed fixture through MSI itself.
        # No registry/ACL edits and no game resources are copied or executed.
        $id='bs1'
        $game=Join-Path $fixture 'bs1\Game\Build\Final'
        $expectedExe='AEC21A0072CFDB15E4B525E2320C87256F14F16894F714272069270AD099A05B'
        if((Get-FileHash -LiteralPath (Join-Path $game 'BioshockHD.exe')).Hash -ne $expectedExe){throw 'No es el fixture BS1 esperado.'}
        $registration="HKCU:\Software\Beren5556\BioShockVRInstallerTests\$TestFamily\bs1"
        $saved=(Get-ItemProperty -LiteralPath $registration -ErrorAction SilentlyContinue).GameDirectory
        if($saved -and [IO.Path]::GetFullPath($saved).TrimEnd('\') -ne $game){throw 'El registro aislado apunta a otra carpeta.'}
        $product='{'+$manifest.games.bs1.productCode+'}'
        foreach($operation in @('recover','remove')) {
            $arguments=if($operation -eq 'recover'){'/i "'+$msi+'" /n '+$product+' ADDLOCAL=Game_bs1,Desktop_bs1 REINSTALLMODE=amus BVR_BS1_GAMEPATH="'+$game+'"'}else{'/x '+$product}
            $arguments+=' /qn /norestart BVR_TESTROOT="'+$fixture+'" /l*v "'+(Join-Path $fixture ("elevated-$operation-"+(Get-Date -Format 'HHmmss')+'.log'))+'"'
            $process=Start-Process -FilePath (Join-Path $env:WINDIR 'System32\msiexec.exe') -ArgumentList $arguments -WindowStyle Hidden -PassThru
            if(-not $process.WaitForExit(60000) -or $process.ExitCode -ne 0){throw "Error de recuperación MSI: $operation"}
        }
        $report.recoveredFixture=$fixture
        $failedResult=Join-Path $packageRoot 'test-result.json'
        $savedResult=Join-Path $packageRoot 'test-result-unelevated.json'
        if((Test-Path -LiteralPath $failedResult) -and -not(Test-Path -LiteralPath $savedResult)){Copy-Item -LiteralPath $failedResult -Destination $savedResult}
    }
    & (Join-Path $PSScriptRoot 'Test-Msi.ps1') -ManifestPath $manifestPath -Bs1Exe $Bs1Exe -Bs2Exe $Bs2Exe -FixtureBase ($batch+'-standard')
    $report.standard=Join-Path $packageRoot 'test-result.json'
    & (Join-Path $PSScriptRoot 'Test-Msi.ps1') -ManifestPath $manifestPath -PreviousSingleGameManifest (Join-Path $repo "artifacts\single-game-isolated\$TestFamily\single-game-0.2.15\manifest.json") -Bs1Exe $Bs1Exe -Bs2Exe $Bs2Exe -FixtureBase ($batch+'-upgrade')
    $report.upgrade=Join-Path $packageRoot 'test-result-instance-upgrade.json'
    $report.passed=$true
} catch {$report.error=$_.ToString()}
finally {
    [IO.File]::WriteAllText((Join-Path $packageRoot 'release-tests-result.json'),($report | ConvertTo-Json -Depth 4),(New-Object Text.UTF8Encoding($false)))
    Stop-Transcript | Out-Null
}
if(-not $report.passed){exit 1}
