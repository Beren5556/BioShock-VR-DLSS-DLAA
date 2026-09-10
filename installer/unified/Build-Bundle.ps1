[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$BioShock1Msi,
    [Parameter(Mandatory=$true)][string]$BioShock2Manifest
)
$ErrorActionPreference = 'Stop'
Write-Warning 'Arquitectura EXE descartada por Carlos. La entrega actual debe construirse con installer/combined/Build-Msi.ps1.'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$version = '0.2.13'
$acceptedHash = '2A5365E332DC311C0E010AD0BECEEAB34E905F66C6F40AB5CF44824EDAD9F660'
$BioShock1Msi = [IO.Path]::GetFullPath($BioShock1Msi)
if ((Get-FileHash -LiteralPath $BioShock1Msi -Algorithm SHA256).Hash -ne $acceptedHash) {
    throw 'El MSI del 1 no es el 0.2.11 aceptado e inmutable.'
}
$BioShock2Manifest = [IO.Path]::GetFullPath($BioShock2Manifest)
$bs2 = Get-Content -LiteralPath $BioShock2Manifest -Raw | ConvertFrom-Json
if ($bs2.gameId -ne 'bs2' -or $bs2.version -ne $version -or $bs2.testFamily) {
    throw 'Se requiere un MSI candidato del 2, no una familia de pruebas ni un paquete de otro juego.'
}
if ([IO.Path]::GetFileName($bs2.installer) -ne $bs2.installer) { throw 'Nombre de MSI no válido.' }
$msi2 = Join-Path (Split-Path -Parent $BioShock2Manifest) $bs2.installer
if ((Get-FileHash -LiteralPath $msi2 -Algorithm SHA256).Hash -ne $bs2.sha256) { throw 'El MSI del 2 ha cambiado.' }
$directory = Join-Path $repo ('artifacts\integration-' + $version + '\bundle')
New-Item -ItemType Directory -Path $directory -Force | Out-Null
$catalog = Join-Path $directory 'Catalog.g.cs'
$content = 'namespace BioShockBundle { internal static class Catalog { internal static readonly Entry[] Items = {' +
    'new Entry("bs1","BioShock Remastered","bs1.msi","' + $acceptedHash + '"),' +
    'new Entry("bs2","BioShock 2 Remastered","bs2.msi","' + $bs2.sha256 + '") }; } }'
[IO.File]::WriteAllText($catalog, $content, (New-Object Text.UTF8Encoding($false)))
# Explicit UTF-8 validation keeps Spanish labels intact in Windows PowerShell.
[void][IO.File]::ReadAllText((Join-Path $PSScriptRoot 'Bundle.cs'), (New-Object Text.UTF8Encoding($false, $true)))
$temporary = Join-Path $directory ([Guid]::NewGuid().ToString('N') + '.exe')
$final = Join-Path $directory ('BioShock-1-2-VR-DLSS-DLAA-' + $version + '-CANDIDATO.exe')
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
try {
    & $compiler /nologo /target:winexe /platform:anycpu /optimize+ /codepage:65001 ("/out:" + $temporary) /reference:System.dll /reference:System.Core.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll ("/resource:" + $BioShock1Msi + ",bs1.msi") ("/resource:" + $msi2 + ",bs2.msi") (Join-Path $PSScriptRoot 'Bundle.cs') $catalog
    if ($LASTEXITCODE -ne 0) { throw 'No se ha compilado el selector común.' }
    $report = Join-Path $directory 'embedded-verification.txt'
    $process = Start-Process -FilePath $temporary -ArgumentList ('--verify "' + $report + '"') -WindowStyle Hidden -PassThru
    if (-not $process.WaitForExit(45000)) { throw "Sigue verificando el paquete, PID $($process.Id)." }
    if ($process.ExitCode -ne 0) { throw 'No se han verificado ambos MSI dentro del ejecutable.' }
    Move-Item -LiteralPath $temporary -Destination $final -Force
    $manifest = [ordered]@{
        version=$version; status='development-candidate-not-published';
        installer=[IO.Path]::GetFileName($final); sha256=(Get-FileHash -LiteralPath $final).Hash;
        packages=@(
            @{ gameId='bs1'; version='0.2.11'; sha256=$acceptedHash; immutable=$true },
            @{ gameId='bs2'; version=$version; sha256=$bs2.sha256; immutable=$false }
        )
    }
    [IO.File]::WriteAllText((Join-Path $directory 'manifest.json'), ($manifest | ConvertTo-Json -Depth 6), (New-Object Text.UTF8Encoding($false)))
    Write-Output $final
} finally {
    if (Test-Path -LiteralPath $temporary) { Remove-Item -LiteralPath $temporary -Force }
}
