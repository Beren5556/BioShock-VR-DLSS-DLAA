[CmdletBinding()]
param(
    [ValidateSet('DLAA', 'SR', 'Both')]
    [string]$Mode = 'Both',

    [ValidateRange(1, 100000)]
    [int]$Frames = 300,

    [string]$PackageDir,

    [string]$DlssRuntime
)

$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$testsRoot = [IO.Path]::GetFullPath($PSScriptRoot).TrimEnd('\') + '\'
$clientExe = Join-Path $PSScriptRoot 'bvr-stereo-client32.exe'

if ([string]::IsNullOrWhiteSpace($PackageDir)) {
    $PackageDir = Join-Path $repo 'dist\BioShockVR-DLSS45'
}
$PackageDir = [IO.Path]::GetFullPath($PackageDir)
$hostExe = Join-Path $PackageDir 'BioShockVR-DLSS45-Host64.exe'
$capabilitiesIni = Join-Path $PackageDir 'dlss-capabilities.ini'

if ([string]::IsNullOrWhiteSpace($DlssRuntime)) {
    $DlssRuntime = Join-Path $PackageDir 'nvngx_dlss.dll'
}
$DlssRuntime = [IO.Path]::GetFullPath($DlssRuntime)

if (-not (Test-Path -LiteralPath $hostExe -PathType Leaf)) {
    throw "Packaged host not found: $hostExe"
}
if (-not (Test-Path -LiteralPath $DlssRuntime -PathType Leaf)) {
    throw "Official NVIDIA DLSS runtime not found: $DlssRuntime"
}
if (-not (Test-Path -LiteralPath $capabilitiesIni -PathType Leaf)) {
    throw "Package capability manifest not found: $capabilitiesIni"
}

& (Join-Path $PSScriptRoot 'build-bvr-stereo-client.bat')
if ($LASTEXITCODE -ne 0) { throw "The x86 stereo client build failed ($LASTEXITCODE)." }

$modes = if ($Mode -eq 'Both') { @('dlaa', 'sr') } else { @($Mode.ToLowerInvariant()) }
foreach ($testMode in $modes) {
    $runtimeRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot "bvr-stereo-runtime\$testMode"))

    # Recursive cleanup is allowed only after proving the target is beneath tests\.
    if (-not $runtimeRoot.StartsWith($testsRoot, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to prepare a runtime outside the tests directory: $runtimeRoot"
    }
    if (Test-Path -LiteralPath $runtimeRoot) {
        Remove-Item -LiteralPath $runtimeRoot -Recurse -Force
    }

    foreach ($eye in 0, 1) {
        $eyeDir = Join-Path $runtimeRoot "eye$eye"
        New-Item -ItemType Directory -Path $eyeDir -Force | Out-Null
        Copy-Item -LiteralPath $hostExe -Destination (Join-Path $eyeDir 'BioShockVR-DLSS45-Host64.exe')
        Copy-Item -LiteralPath $DlssRuntime -Destination (Join-Path $eyeDir 'nvngx_dlss.dll')
        Copy-Item -LiteralPath $capabilitiesIni -Destination (Join-Path $eyeDir 'dlss-capabilities.ini')
    }

    Write-Host ""
    Write-Host "=== Stereo $($testMode.ToUpperInvariant()): $Frames frames per eye ==="
    & $clientExe --mode $testMode --runtime-root $runtimeRoot --frames $Frames
    if ($LASTEXITCODE -ne 0) {
        throw "Stereo $testMode failed ($LASTEXITCODE). Logs remain under $runtimeRoot."
    }
}

Write-Host ""
Write-Host 'All requested BioShock VR stereo bridge tests passed.'
