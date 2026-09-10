[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$BasePayloadRoot,
    [string]$ModBuildDirectory='',
    [string]$LauncherDirectory=''
)
$ErrorActionPreference='Stop'
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$version='0.2.17'
$buildId='beren5556-bs12-v0.2.17-english'
if(-not $ModBuildDirectory){$ModBuildDirectory=Join-Path $repo 'artifacts\integration-0.2.17\build'}
if(-not $LauncherDirectory){$LauncherDirectory=Join-Path $repo 'artifacts\integration-0.2.17\launcher'}
$output=Join-Path $repo 'artifacts\distribution-0.2.17\payloads'
$validatedPath=Join-Path $repo 'release\validated-mods-v0.2.17.json'
if((Test-Path -LiteralPath $output) -or (Test-Path -LiteralPath $validatedPath)){throw 'English payload/pins already exist; never overwrite a frozen build.'}
Push-Location $repo
try{& node scripts/localization/english.mjs verify; if($LASTEXITCODE -ne 0){throw 'Text-only source verification failed.'}}finally{Pop-Location}
$cache=Get-Content -LiteralPath (Join-Path $ModBuildDirectory 'CMakeCache.txt') -Raw
foreach($flag in @('BVR_DLSS_OVERLAP','BVR_DEPTH_COPY_REUSE','BVR_DLSS_TAIL_OVERLAP','BVR_DLSS_EARLY_DELIVERY')){
    if($cache -notmatch "(?m)^${flag}:BOOL=ON\r?$"){throw "Optimization disabled: $flag"}
}
foreach($flag in @('BVR_PERFORMANCE_PROBE','BVR_LATENCY_PROBE','BVR_CRITICAL_PATH_PROBE','BVR_BS2_TEST_ISOLATION')){
    if($cache -notmatch "(?m)^${flag}:BOOL=OFF\r?$"){throw "Diagnostic enabled: $flag"}
}
$stamp=Get-Content -LiteralPath (Join-Path $ModBuildDirectory 'generated\bvr_version.h') -Raw
if(-not $stamp.Contains('"'+$buildId+'"')){throw 'Wrong English build identity.'}
$published=Get-Content -LiteralPath (Join-Path $repo 'release\manifest-v0.2.16.json') -Raw | ConvertFrom-Json
if($published.sha256 -ne '1C881F0A27416198FC25A068A79044BEED7FCC37160F38E7AFE2AE09CE9A2371'){throw 'Wrong Spanish reference release.'}
function Resolve-Owned([string]$Root,[string]$Relative){
    $absolute=[IO.Path]::GetFullPath($Root).TrimEnd('\')
    if([IO.Path]::IsPathRooted($Relative) -or $Relative -match '(^|[\\/])\.\.([\\/]|$)|:'){throw 'Unsafe payload path.'}
    $path=[IO.Path]::GetFullPath((Join-Path $absolute $Relative))
    if(-not $path.StartsWith($absolute+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'Payload path escapes its root.'}
    return $path
}
function IniValues([string]$Path){
    return ((Get-Content -LiteralPath $Path | Where-Object {$_.Trim() -and $_ -notmatch '^\s*[;#]'}) -join "`n")
}
$documents=@{
    'BioShockVR-DLSS45\LEEME-DLSS45.md'='docs\releases\v0.2.17.md'
    'BioShockVR-DLSS45\RENDIMIENTO.md'='docs\releases\v0.2.17-performance.md'
    'BioShockVR-DLSS45\Licenses\ACKNOWLEDGEMENTS.md'='ACKNOWLEDGEMENTS.md'
    'BioShockVR-DLSS45\Licenses\THIRD_PARTY_NOTICES.md'='THIRD_PARTY_NOTICES.md'
    'BioShockVR-DLSS45\dlss.ini.example'='localization\dlss-example-en.ini'
}
$plan=@{}
$validated=[ordered]@{schemaVersion=1;version=$version;modVersion=$version;modBuild=$buildId;runtime='310.7.0.0';baseTag='v0.2.16';acceptance='English text-only rebuild of the user-accepted Spanish base. Automated validation is recorded separately; no new physical-headset or second-PC acceptance is claimed.';games=[ordered]@{}}
foreach($game in @('bs1','bs2')){
    $inputRoot=Join-Path $BasePayloadRoot $game
    $manifest=Get-Content -LiteralPath (Join-Path $BasePayloadRoot "manifest-$game.json") -Raw | ConvertFrom-Json
    if($manifest.kind -ne 'verified-game-payload' -or $manifest.version -ne '0.2.16' -or $manifest.gameId -ne $game -or @($manifest.files).Count -ne 23 -or $manifest.testFamily){throw 'Wrong base payload identity.'}
    $launcher=if($game -eq 'bs1'){'Lanzador BioShock VR DLSS-DLAA.exe'}else{'Lanzador BioShock 2 VR DLSS-DLAA.exe'}
    $files=New-Object 'System.Collections.Generic.List[object]'
    $seen=@{}
    foreach($entry in $manifest.files){
        $relative=$entry.path.Replace('/','\')
        if($seen.ContainsKey($relative)){throw 'Duplicate payload path.'};$seen[$relative]=$true
        $pin=@($published.games.$game.files | Where-Object {$_.path.Replace('/','\') -eq $relative})
        $source=Resolve-Owned $inputRoot $relative
        if($pin.Count -ne 1 -or $entry.sha256 -ne $pin[0].sha256 -or (Get-FileHash -LiteralPath $source).Hash -ne $entry.sha256){throw "Modified base input: $game $relative"}
        if($relative -eq 'bioshockvr.dll'){$source=Resolve-Owned $ModBuildDirectory 'src\Release\bioshockvr.dll'}
        elseif($relative -eq $launcher){
            $source=Resolve-Owned (Join-Path $LauncherDirectory $game) $launcher
            if([Diagnostics.FileVersionInfo]::GetVersionInfo($source).FileVersion -ne '0.2.17.0'){throw 'Wrong launcher version.'}
        }
        elseif($documents.ContainsKey($relative)){
            $translated=Join-Path $repo $documents[$relative]
            if($relative.EndsWith('dlss.ini.example') -and (IniValues $source) -cne (IniValues $translated)){throw 'Example INI keys/values changed during translation.'}
            $source=$translated
        }
        $files.Add([pscustomobject]@{path=$relative;sha256=(Get-FileHash -LiteralPath $source).Hash;source=$source;previousSha256=$entry.sha256})
    }
    if($files.Count -ne 23){throw 'Expected 23 files per game.'}
    $plan[$game]=$files
    $validated.games[$game]=@{files=@($files | Where-Object {$_.path -eq 'bioshockvr.dll' -or $_.path -eq $launcher -or $_.path -eq 'xinput1_3.dll' -or $_.path -eq 'host64\dlss-capabilities.ini'} | Select-Object path,sha256)}
}
$utf8=New-Object Text.UTF8Encoding($false)
# Generated release pins and staging artifacts only; no installed game is touched.
[IO.File]::WriteAllText($validatedPath,($validated | ConvertTo-Json -Depth 8),$utf8)
$pinHash=(Get-FileHash -LiteralPath $validatedPath).Hash
foreach($game in @('bs1','bs2')){
    foreach($entry in $plan[$game]){
        $destination=Resolve-Owned (Join-Path $output $game) $entry.path
        New-Item -ItemType Directory -Path (Split-Path $destination) -Force | Out-Null
        Copy-Item -LiteralPath $entry.source -Destination $destination
        if((Get-FileHash -LiteralPath $destination).Hash -ne $entry.sha256){throw 'Staged copy differs.'}
    }
    $manifest=[ordered]@{kind='verified-game-payload';version=$version;gameId=$game;testFamily='';modVersion=$version;modBuild=$buildId;runtime='310.7.0.0';validatedManifestSha256=$pinHash;files=@($plan[$game] | Select-Object path,sha256)}
    [IO.File]::WriteAllText((Join-Path $output "manifest-$game.json"),($manifest | ConvertTo-Json -Depth 7),$utf8)
    $changed=@($plan[$game] | Where-Object {$_.sha256 -ne $_.previousSha256} | Select-Object path,sha256,previousSha256)
    [IO.File]::WriteAllText((Join-Path $output "changes-$game.json"),($changed | ConvertTo-Json -Depth 4),$utf8)
}
Write-Output "PASS: 46 verified payload files; English core/launchers/documentation only. $output"
