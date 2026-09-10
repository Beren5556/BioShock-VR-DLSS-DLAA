#requires -Version 7
[CmdletBinding()]
param([switch]$Capture)
$ErrorActionPreference='Stop'
$repo=Split-Path -Parent $PSScriptRoot
$path=Join-Path $repo 'artifacts\bs2-tests\real-files-before.json'
$roots=@(
    (Join-Path $env:APPDATA 'BioshockHD\Bioshock'),
    (Join-Path $env:APPDATA 'BioshockHD\Bioshock2'),
    (Join-Path ([Environment]::GetFolderPath('MyDocuments')) 'BioshockHD\BioShock2\SaveGames')
)
$files=@(foreach($dir in $roots) { if(Test-Path -LiteralPath $dir) { Get-ChildItem -LiteralPath $dir -Recurse -File } })
$gameBin='D:\SteamLibrary\steamapps\common\BioShock 2 Remastered\Build\Final'
foreach($leaf in @('Bioshock2HD.exe','bioshockvr.dll','xinput1_3.dll','bvr_steamvr32.dll','openvr_api.dll')) {
    $files+=Get-Item -LiteralPath (Join-Path $gameBin $leaf)
}
$entries=@($files | Sort-Object FullName -Unique | ForEach-Object {
    [ordered]@{path=$_.FullName;sha256=(Get-FileHash -LiteralPath $_.FullName).Hash}
})
if($Capture) {
    if(Test-Path -LiteralPath $path) { throw 'Baseline already exists; preserve it for verification.' }
    New-Item -ItemType Directory -Path (Split-Path -Parent $path) -Force | Out-Null
    $entries | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $path -Encoding utf8NoBOM
    "Captured $($entries.Count) original files."
    return
}
$before=@(Get-Content -LiteralPath $path -Raw | ConvertFrom-Json)
$differences=Compare-Object @($before | ForEach-Object {"$($_.path)|$($_.sha256)"}) @($entries | ForEach-Object {"$($_.path)|$($_.sha256)"})
if($differences) { $differences | Format-Table; throw 'Original files changed during testing.' }
"PASS: all $($before.Count) original files preserved byte for byte."
