#requires -Version 7
[CmdletBinding()]
param(
    [string]$Source = 'D:\SteamLibrary\steamapps\common\BioShock 2 Remastered',
    [string]$LabRoot = 'D:\BioShock2VR-DLSS-Lab'
)
$ErrorActionPreference = 'Stop'
$expected = 'C2A31FB67B285C136203A4A6E739545177BE5753D972E6AEA0F44C65DDF22F8C'
$sourceExe = Join-Path $Source 'Build\Final\Bioshock2HD.exe'
if ((Get-FileHash -LiteralPath $sourceExe).Hash -ne $expected) { throw 'Unsupported source executable.' }
$LabRoot = [IO.Path]::GetFullPath($LabRoot)
if ($LabRoot -ne 'D:\BioShock2VR-DLSS-Lab') { throw 'Unexpected test-copy root.' }
$target = Join-Path $LabRoot ('game-' + [guid]::NewGuid().ToString('D'))
if (Test-Path -LiteralPath $target) { throw 'The new test copy already exists.' }
New-Item -ItemType Directory -Path $target -Force | Out-Null
foreach ($relative in @('Build', 'ContentBaked', 'ShaderCache')) {
    $from = Join-Path $Source $relative
    $to = Join-Path $target $relative
    & robocopy $from $to /E /COPY:DAT /DCOPY:DAT /R:1 /W:1 /NP /NFL /NDL /NJH /NJS
    if ($LASTEXITCODE -ge 8) { throw "Copy failed for $relative (robocopy $LASTEXITCODE)." }
}
$targetExe = Join-Path $target 'Build\Final\Bioshock2HD.exe'
if ((Get-FileHash -LiteralPath $targetExe).Hash -ne $expected) { throw 'Copied executable mismatch.' }
$receipt = [ordered]@{
    schema = 'bs2-dlss-test-copy/v1'
    createdUtc = [DateTime]::UtcNow.ToString('o')
    source = $Source
    target = $target
    executable = $targetExe
    executableSha256 = $expected
    method = 'Physical copies; no links; original game files unchanged.'
}
$repo = Split-Path -Parent $PSScriptRoot
$out = Join-Path $repo 'artifacts\bs2-tests'
New-Item -ItemType Directory -Path $out -Force | Out-Null
$receipt | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $out 'game-copy.json') -Encoding utf8NoBOM
$global:LASTEXITCODE = 0
$receipt | ConvertTo-Json
