[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$toolsRoot = Join-Path $repoRoot 'artifacts\msi-tools'
$version = '6.0.2'
Add-Type -AssemblyName System.IO.Compression.FileSystem
foreach ($package in @('wix', 'wixtoolset.ui.wixext', 'wixtoolset.dtf.customaction', 'wixtoolset.dtf.windowsinstaller')) {
    $destination = Join-Path $toolsRoot ($package + '.' + $version)
    $archive = Join-Path $toolsRoot ($package + '.' + $version + '.nupkg')
    New-Item -ItemType Directory -Path $toolsRoot -Force | Out-Null
    if (-not (Test-Path -LiteralPath $archive)) {
        Invoke-WebRequest -UseBasicParsing -Uri "https://api.nuget.org/v3-flatcontainer/$package/$version/$package.$version.nupkg" -OutFile $archive
    }
    if (-not (Test-Path -LiteralPath $destination)) {
        [IO.Compression.ZipFile]::ExtractToDirectory($archive, $destination)
    }
    Get-FileHash -Algorithm SHA256 -LiteralPath $archive | Select-Object Path, Hash
}
foreach ($download in @(
    @('WiX-6.0.2-LICENSE.txt', 'https://raw.githubusercontent.com/wixtoolset/wix/v6.0.2/LICENSE.TXT'),
    @('WiX-6.0.2-source.zip', 'https://codeload.github.com/wixtoolset/wix/zip/refs/tags/v6.0.2')
)) {
    $target = Join-Path $toolsRoot $download[0]
    if (-not (Test-Path -LiteralPath $target)) {
        Invoke-WebRequest -UseBasicParsing -Uri $download[1] -OutFile $target
    }
    Get-FileHash -Algorithm SHA256 -LiteralPath $target | Select-Object Path, Hash
}
