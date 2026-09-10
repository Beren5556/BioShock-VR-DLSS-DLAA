#requires -Version 7
[CmdletBinding()]
param(
    [ValidateSet('off','dlaa','sr')][string]$Mode = 'off',
    [int]$Render = 1024,
    [int]$Output = 1536
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$copy = Get-Content -LiteralPath (Join-Path $repo 'artifacts\bs2-tests\game-copy.json') -Raw | ConvertFrom-Json
$root = [IO.Path]::GetFullPath($copy.target)
if ($root -notmatch '^D:\\BioShock2VR-DLSS-Lab\\game-[0-9a-f-]{36}$') { throw 'Unexpected test copy.' }
if ((Get-FileHash -LiteralPath $copy.executable).Hash -ne $copy.executableSha256) { throw 'Test executable changed.' }
# A fail-fast process can report HasExited while its last thread/resources
# still exist. Do not bypass the exclusive-run check on that property alone.
if (Get-Process -Name Bioshock2HD -ErrorAction SilentlyContinue) {
    throw 'BioShock 2 is still running.'
}
$run = Join-Path $root ('runs\' + $Mode + '-' + [guid]::NewGuid().ToString('N').Substring(0,8))
$profile = Join-Path $run 'profile'
$roaming = Join-Path $profile 'roaming'
$local = Join-Path $profile 'local'
$documents = Join-Path $profile 'documents'
$config = Join-Path $roaming 'BioshockHD\Bioshock2'
$saves = Join-Path $documents 'BioshockHD\BioShock2\SaveGames'
$data = Join-Path $run 'data'
$sim = Join-Path $run 'sim'
$binary = Join-Path $root 'Build\Final'
foreach($dir in @($config,$saves,$local,$data,$sim,(Join-Path $sim 'capture'),(Join-Path $run 'bin'),(Join-Path $run 'temp'))) {
    New-Item -ItemType Directory -Path $dir -Force | Out-Null
}
Copy-Item -LiteralPath (Join-Path $env:APPDATA 'BioshockHD\Bioshock2\Bioshock2SP.ini') -Destination $config
Copy-Item -LiteralPath (Join-Path $env:APPDATA 'BioshockHD\Bioshock2\Shared.ini') -Destination $config
Copy-Item -LiteralPath (Join-Path $env:APPDATA 'BioshockHD\Bioshock2\User.ini') -Destination $config
$realSaveRoot = Join-Path ([Environment]::GetFolderPath('MyDocuments')) 'BioshockHD\BioShock2\SaveGames'
Get-ChildItem -LiteralPath $realSaveRoot -Filter '*.bsb' -File | Copy-Item -Destination $saves
function Set-IniValue([string]$Path,[string]$Section,[string]$Key,[string]$Value) {
    $text = [IO.File]::ReadAllText($Path)
    $match = [regex]::Match($text, '(?ms)(^\[' + [regex]::Escape($Section) + '\]\r?\n)(.*?)(?=^\[|\z)')
    if (!$match.Success) { throw "Missing section $Section" }
    $pattern = '(?m)^' + [regex]::Escape($Key) + '\s*=[^\r\n]*'
    if (![regex]::IsMatch($match.Groups[2].Value,$pattern)) { throw "Missing key $Section/$Key" }
    $body = [regex]::Replace($match.Groups[2].Value,$pattern,[Text.RegularExpressions.MatchEvaluator]{param($m) "$Key=$Value"})
    $result = $text.Substring(0,$match.Index) + $match.Groups[1].Value + $body + $text.Substring($match.Index+$match.Length)
    [IO.File]::WriteAllText($Path,$result,[Text.UTF8Encoding]::new($false))
}
$gameIni = Join-Path $config 'Bioshock2SP.ini'
$shared = Join-Path $config 'Shared.ini'
foreach($key in @('WindowedViewportX','WindowedViewportY','FullscreenViewportX','FullscreenViewportY')) {
    Set-IniValue $gameIni 'WinDrv.WindowsClient' $key "$Render"
}
Set-IniValue $gameIni 'WinDrv.WindowsClient' 'StartupFullscreen' 'False'
Set-IniValue $gameIni 'Core.System' 'SavePath' $saves
Set-IniValue $gameIni 'Engine.RenderConfig' 'UseFxaa' 'False;'
Set-IniValue $shared 'SharedOptions' 'ViewportX' "$Render"
Set-IniValue $shared 'SharedOptions' 'ViewportY' "$Render"
Set-IniValue $shared 'SharedOptions' 'StartupFullscreen' 'False'
if($Mode -ne 'sr') { $Output = $Render }
$dlss = "[dlss]`r`nmode=$Mode`r`nruntime=310.7.0`r`npreset=auto`r`noutputWidth=$Output`r`noutputHeight=$Output`r`nnearPlaneUu=10.0`r`n"
[IO.File]::WriteAllText((Join-Path $data 'dlss.ini'),$dlss,[Text.UTF8Encoding]::new($false))
[IO.File]::WriteAllText((Join-Path $data 'xr.ini'),"[runtime]`r`nmode=native`r`n",[Text.UTF8Encoding]::new($false))
foreach($file in @('bioshockvr.dll','xinput1_3.dll')) {
    $builtFile = Join-Path $repo "build-bs2-test\src\Release\$file"
    $labFile = Join-Path $binary $file
    if (!(Test-Path -LiteralPath $labFile) -or
        (Get-FileHash -LiteralPath $builtFile).Hash -ne (Get-FileHash -LiteralPath $labFile).Hash) {
        Copy-Item -LiteralPath $builtFile -Destination $labFile -Force
    }
}
Copy-Item -LiteralPath (Join-Path $repo 'installer\Payload\host64') -Destination $binary -Recurse -Force
foreach($file in @('bvr_xrsim32.dll','xr_hello32.exe')) {
    Copy-Item -LiteralPath (Join-Path $repo "build\src\Release\$file") -Destination (Join-Path $run 'bin')
}
$manifest = Join-Path $sim 'bvr_xrsim32.json'
@{file_format_version='1.0.0';runtime=@{name='bvr-xrsim';library_path=(Join-Path $run 'bin\bvr_xrsim32.dll')}} |
    ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $manifest -Encoding utf8NoBOM
$receipt = [ordered]@{
    schema='bs2-dlss-test-run/v1'; mode=$Mode; render=$Render; output=$Output
    productionCoreSha256=(Get-FileHash -LiteralPath (Join-Path $repo 'build\src\Release\bioshockvr.dll')).Hash
    testCoreSha256=(Get-FileHash -LiteralPath (Join-Path $binary 'bioshockvr.dll')).Hash
    testProxySha256=(Get-FileHash -LiteralPath (Join-Path $binary 'xinput1_3.dll')).Hash
    root=$run; gameRoot=$root; executable=$copy.executable; log=(Join-Path $data 'bioshockvr.log'); sim=$sim
    environment=[ordered]@{
        BVR_LAB_GAME_ROOT=$root; BVR_LAB_PROFILE=$profile; BVR_LAB_ROAMING=$roaming
        BVR_LAB_LOCAL=$local; BVR_LAB_DOCUMENTS=$documents; BVR_LAB_DATA_DIR=$data; BVR_LAB_GAME_INI=$gameIni
        XR_RUNTIME_JSON=$manifest; BVR_XRSIM_DIR=$sim
        APPDATA=$roaming; LOCALAPPDATA=$local; TEMP=(Join-Path $run 'temp'); TMP=(Join-Path $run 'temp')
    }
}
$receipt | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $run 'run.json') -Encoding utf8NoBOM
$receipt | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $repo 'artifacts\bs2-tests\latest-run.json') -Encoding utf8NoBOM
$receipt | ConvertTo-Json -Depth 5
