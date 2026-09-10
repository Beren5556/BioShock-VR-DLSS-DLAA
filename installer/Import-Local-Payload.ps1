[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$PayloadDirectory
)

$ErrorActionPreference = 'Stop'
$installerRoot = [IO.Path]::GetFullPath($PSScriptRoot)
$sourceRoot = [IO.Path]::GetFullPath($PayloadDirectory)
$destinationRoot = [IO.Path]::GetFullPath((Join-Path $installerRoot 'Payload'))
$manifestPath = Join-Path $installerRoot 'payload-manifest.json'
$manifest = Get-Content -LiteralPath $manifestPath -Raw -Encoding UTF8 | ConvertFrom-Json

if ($manifest.schemaVersion -ne 2 -or $manifest.gameId -ne 'bs2' -or $manifest.release -ne 'v0.1.1-beta' -or @($manifest.payload).Count -ne 8) {
    throw 'Se requiere el manifiesto de payload de BioShock 2 v0.1.1-beta.'
}
if (-not (Test-Path -LiteralPath $sourceRoot -PathType Container)) {
    throw "No existe la carpeta de payload: $sourceRoot"
}

$sourcePrefix = $sourceRoot.TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
$destinationPrefix = $destinationRoot.TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
New-Item -ItemType Directory -Path $destinationRoot -Force | Out-Null

foreach ($entry in $manifest.payload) {
    $relative = ([string]$entry.sourcePath).Replace('/', [IO.Path]::DirectorySeparatorChar)
    $source = [IO.Path]::GetFullPath((Join-Path $sourceRoot $relative))
    $destination = [IO.Path]::GetFullPath((Join-Path $destinationRoot $relative))
    if (-not $source.StartsWith($sourcePrefix, [StringComparison]::OrdinalIgnoreCase) -or
        -not $destination.StartsWith($destinationPrefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Ruta no segura en el manifiesto: $relative"
    }
    if (-not (Test-Path -LiteralPath $source -PathType Leaf)) {
        throw "Falta el componente local: $relative"
    }
    $actual = (Get-FileHash -Algorithm SHA256 -LiteralPath $source).Hash
    if ($actual -ne [string]$entry.sha256) {
        throw "Hash distinto para $relative. Esperado: $($entry.sha256). Actual: $actual"
    }
    New-Item -ItemType Directory -Path (Split-Path -Parent $destination) -Force | Out-Null
    Copy-Item -LiteralPath $source -Destination $destination -Force
    if ((Get-FileHash -Algorithm SHA256 -LiteralPath $destination).Hash -ne [string]$entry.sha256) {
        throw "La copia local no se pudo verificar: $relative"
    }
    Write-Host "OK  $relative"
}

Write-Host "Payload local preparado y verificado: $destinationRoot"
