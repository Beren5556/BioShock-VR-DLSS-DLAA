[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][ValidatePattern('^[a-f0-9]{32}$')][string]$TestFamily,
    [Parameter(Mandatory=$true)][string]$ExpectedUserSid,
    [Parameter(Mandatory=$true)][string]$SpanishManifest,
    [Parameter(Mandatory=$true)][string]$Bs1Exe,
    [Parameter(Mandatory=$true)][string]$Bs2Exe
)
$ErrorActionPreference='Stop'
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$identity=[Security.Principal.WindowsIdentity]::GetCurrent()
$principal=New-Object Security.Principal.WindowsPrincipal($identity)
if($identity.User.Value -ne $ExpectedUserSid -or -not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)){throw 'Use normal elevation under the same user account.'}
$packageRoot=Join-Path $repo "artifacts\single-game-isolated\$TestFamily\single-game-0.2.17"
$manifestPath=Join-Path $packageRoot 'manifest.json'
$manifest=Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
$previous=Get-Content -LiteralPath $SpanishManifest -Raw | ConvertFrom-Json
if($manifest.testFamily -ne $TestFamily -or $previous.testFamily -ne $TestFamily -or $manifest.version -ne '0.2.17' -or $previous.version -ne '0.2.16'){throw 'Only the verified English/Spanish isolated family is accepted.'}
foreach($pair in @(@{root=$packageRoot;m=$manifest},@{root=(Split-Path $SpanishManifest);m=$previous})){
    if((Get-FileHash -LiteralPath (Join-Path $pair.root $pair.m.installer)).Hash -ne $pair.m.sha256){throw 'Test MSI hash mismatch.'}
}
# Keep fixtures short: the legacy .NET Framework MSI actions retain MAX_PATH.
# This test-only location is separate from every installed game and profile.
$batch='D:\BioShock12VR-IntegrationTests\English-0.2.17-'+(Get-Date -Format 'yyyyMMdd-HHmmss')
$report=[ordered]@{passed=$false;error='';sameUserElevated=$true;standard='';upgrade=''}
Start-Transcript -LiteralPath (Join-Path $packageRoot 'english-release-tests-elevated.log') -Force | Out-Null
try{
    & (Join-Path $PSScriptRoot 'Test-Msi.ps1') -ManifestPath $manifestPath -Bs1Exe $Bs1Exe -Bs2Exe $Bs2Exe -FixtureBase ($batch+'-standard')
    $report.standard=Join-Path $packageRoot 'test-result.json'
    & (Join-Path $PSScriptRoot 'Test-Msi.ps1') -ManifestPath $manifestPath -PreviousSingleGameManifest $SpanishManifest -Bs1Exe $Bs1Exe -Bs2Exe $Bs2Exe -FixtureBase ($batch+'-upgrade')
    $report.upgrade=Join-Path $packageRoot 'test-result-instance-upgrade.json'
    $report.passed=$true
}catch{$report.error=$_.ToString()}
finally{
    [IO.File]::WriteAllText((Join-Path $packageRoot 'english-release-tests-result.json'),($report | ConvertTo-Json -Depth 4),(New-Object Text.UTF8Encoding($false)))
    Stop-Transcript | Out-Null
}
if(-not $report.passed){exit 1}
