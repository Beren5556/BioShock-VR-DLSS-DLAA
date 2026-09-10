[CmdletBinding()]
param(
    [ValidatePattern('^\d+\.\d+\.\d+$')][string]$Version = '0.2.16',
    [Parameter(Mandatory=$true)][string]$BasePayloadDirectory,
    [Parameter(Mandatory=$true)][string]$Bs2BasePayloadDirectory,
    [Parameter(Mandatory=$true)][string]$Bs2BaseManifestPath,
    [Parameter(Mandatory=$true)][string]$CandidateDirectory,
    [Parameter(Mandatory=$true)][string]$ModBuildDirectory
)
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$output = Join-Path $repo "artifacts\distribution-$Version\payloads"
if (Test-Path -LiteralPath $output) { throw 'El payload ya existe: no se sobrescriben archivos congelados.' }
$validatedPath = Join-Path $repo "release\validated-mods-v$Version.json"
$validated = Get-Content -LiteralPath $validatedPath -Raw -Encoding UTF8 | ConvertFrom-Json
if ($validated.schemaVersion -ne 1 -or $validated.version -ne $Version) { throw 'Inventario validado incorrecto.' }
$base = Get-Content -LiteralPath (Join-Path $repo 'release\manifest-v0.2.11.json') -Raw | ConvertFrom-Json
$bs2Base = Get-Content -LiteralPath $Bs2BaseManifestPath -Raw | ConvertFrom-Json
if ($bs2Base.gameId -ne 'bs2' -or $bs2Base.version -ne '0.2.13' -or $bs2Base.testFamily) { throw 'Base BS2 inesperada.' }
$cache = Get-Content -LiteralPath (Join-Path $ModBuildDirectory 'CMakeCache.txt') -Raw
foreach ($flag in @('BVR_DLSS_OVERLAP','BVR_DEPTH_COPY_REUSE','BVR_DLSS_TAIL_OVERLAP','BVR_DLSS_EARLY_DELIVERY')) {
    if ($cache -notmatch "(?m)^${flag}:BOOL=ON\r?$") { throw "Optimización no activada: $flag" }
}
foreach ($flag in @('BVR_PERFORMANCE_PROBE','BVR_LATENCY_PROBE','BVR_CRITICAL_PATH_PROBE','BVR_BS2_TEST_ISOLATION')) {
    if ($cache -notmatch "(?m)^${flag}:BOOL=OFF\r?$") { throw "Sonda de laboratorio activada: $flag" }
}
$stamp = Get-Content -LiteralPath (Join-Path $ModBuildDirectory 'generated\bvr_version.h') -Raw
if (-not $stamp.Contains('"' + $validated.modBuild + '"')) { throw 'Identidad de compilación distinta de la probada.' }
function Resolve-PayloadFile([string]$Root, [string]$Relative) {
    $rootPath = [IO.Path]::GetFullPath($Root).TrimEnd('\')
    $relativePath = $Relative.Replace('/', '\')
    if ([IO.Path]::IsPathRooted($relativePath) -or $relativePath -match '(^|\\)\.\.($|\\)|:') { throw 'Ruta de payload no válida.' }
    $full = [IO.Path]::GetFullPath((Join-Path $rootPath $relativePath))
    if (-not $full.StartsWith($rootPath + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Ruta fuera del payload.' }
    return $full
}
# Verify both frozen source inventories in full, including replaced files.
foreach ($inputSet in @(@{root=$BasePayloadDirectory; manifest=$base}, @{root=$Bs2BasePayloadDirectory; manifest=$bs2Base})) {
    if (@($inputSet.manifest.files).Count -ne 23) { throw 'Inventario base incompleto.' }
    foreach ($file in $inputSet.manifest.files) {
        $path = Resolve-PayloadFile $inputSet.root $file.path
        if ((Get-FileHash -LiteralPath $path).Hash -ne $file.sha256) { throw "Base modificada: $path" }
    }
}
$planned = @{}
foreach ($id in @('bs1','bs2')) {
    $overrides = @{}
    foreach ($file in $validated.games.$id.files) { $overrides[$file.path.Replace('/', '\')] = $file.sha256 }
    $items = New-Object 'System.Collections.Generic.List[object]'
    foreach ($file in $base.files) {
        $relative = $file.path.Replace('/', '\')
        $source = Resolve-PayloadFile $BasePayloadDirectory $relative
        $expected = $file.sha256
        if ($relative -eq 'bioshockvr.dll') {
            $source = Resolve-PayloadFile $CandidateDirectory $relative
        } elseif ($relative -eq 'Lanzador BioShock VR DLSS-DLAA.exe') {
            if ($id -eq 'bs2') { $relative = 'Lanzador BioShock 2 VR DLSS-DLAA.exe' }
            $source = Resolve-PayloadFile $CandidateDirectory ("launchers\$id\$relative")
        } elseif ($id -eq 'bs2' -and $relative -eq 'xinput1_3.dll') {
            $source = Resolve-PayloadFile $Bs2BasePayloadDirectory $relative
        } elseif ($id -eq 'bs2' -and $relative -eq 'host64\dlss-capabilities.ini') {
            $source = Join-Path $repo 'installer\profiles\bs2\dlss-capabilities.ini'
        } elseif ($relative -eq 'BioShockVR-DLSS45\LEEME-DLSS45.md') {
            $source = Join-Path $repo "docs\releases\v$Version.md"
            $expected = ''
        } elseif ($relative -eq 'BioShockVR-DLSS45\RENDIMIENTO.md') {
            $source = Join-Path $repo "docs\releases\v$Version-performance.md"
            $expected = ''
        }
        if ($overrides.ContainsKey($relative)) { $expected = $overrides[$relative] }
        $hash = (Get-FileHash -LiteralPath $source).Hash
        if ($expected -and $hash -ne $expected) { throw "No coincide el archivo validado: $id $relative" }
        $items.Add([pscustomobject]@{path=$relative; sha256=$hash; source=$source})
    }
    if ($items.Count -ne 23) { throw 'Inventario final incompleto.' }
    foreach ($relative in $overrides.Keys) {
        if (@($items | Where-Object path -eq $relative).Count -ne 1) { throw "Override validado no aplicado: $id $relative" }
    }
    $planned[$id] = $items
}
# Generated staging output only. No game folder or existing release is changed.
foreach ($id in @('bs1','bs2')) {
    $payload = Join-Path $output $id
    foreach ($file in $planned[$id]) {
        $destination = Resolve-PayloadFile $payload $file.path
        New-Item -ItemType Directory -Path (Split-Path $destination) -Force | Out-Null
        Copy-Item -LiteralPath $file.source -Destination $destination
        if ((Get-FileHash -LiteralPath $destination).Hash -ne $file.sha256) { throw 'Error verificando copia del payload.' }
    }
    $manifest = [ordered]@{
        kind='verified-game-payload'; version=$Version; gameId=$id; testFamily=''
        modVersion=$validated.modVersion; modBuild=$validated.modBuild; runtime=$validated.runtime
        validatedManifestSha256=(Get-FileHash -LiteralPath $validatedPath).Hash
        files=@($planned[$id] | Select-Object path,sha256)
    }
    [IO.File]::WriteAllText((Join-Path $output "manifest-$id.json"), ($manifest | ConvertTo-Json -Depth 6), (New-Object Text.UTF8Encoding($false)))
}
Write-Output "PASS: 46 archivos preparados a partir de las bases y los binarios validados; sin recompilar. $output"
