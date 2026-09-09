[CmdletBinding()]
param(
    [switch]$BuildLauncher
)

$ErrorActionPreference = 'Stop'
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))

function Assert-True([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw $Message }
}

function Read-Utf8([string]$RelativePath) {
    $path = Join-Path $repoRoot $RelativePath
    Assert-True (Test-Path -LiteralPath $path -PathType Leaf) "Missing required file: $RelativePath"
    [IO.File]::ReadAllText($path, [Text.Encoding]::UTF8)
}

Push-Location $repoRoot
try {
    $tracked = @(& git ls-files)
    Assert-True ($LASTEXITCODE -eq 0) 'git ls-files failed.'
    Assert-True ($tracked.Count -gt 0) 'No tracked files were found.'

    $forbiddenPathPatterns = @(
        '(^|/)artifacts/',
        '(^|/)installer/Payload/',
        '(^|/)components/dlss-host/external/ngx/',
        '(^|/)BioshockHD\.exe$',
        '(^|/)nvngx_dlss\.dll$'
    )
    foreach ($file in $tracked) {
        $normalized = $file.Replace('\', '/')
        foreach ($pattern in $forbiddenPathPatterns) {
            Assert-True ($normalized -notmatch $pattern) "Forbidden tracked file: $file"
        }
    }

    $required = @(
        'README.md',
        'ACKNOWLEDGEMENTS.md',
        'CHANGELOG.md',
        'PROVENANCE.md',
        'SECURITY.md',
        'LICENSE',
        'installer/payload-manifest.json',
        'release/SHA256SUMS-v0.2.11.txt',
        'release/manifest-v0.2.11.json',
        'docs/releases/v0.2.11.md',
        'docs/releases/v0.2.11-public.md',
        'docs/releases/v0.2.12.md',
        'apps/launcher/src/BioshockVrLauncher.cs',
        'apps/launcher/src/ImageTab.cs',
        'apps/launcher/src/LauncherLocalization.cs',
        'installer/msi/Build-Msi.ps1',
        'installer/msi/Package.wxs',
        'installer/msi/Interface.wxs',
        'installer/msi/MsiActions.cs'
    )
    foreach ($file in $required) { $null = Read-Utf8 $file }

    $manifestText = Read-Utf8 'installer/payload-manifest.json'
    $manifest = $manifestText | ConvertFrom-Json
    Assert-True ($manifest.schemaVersion -eq 1) 'Unexpected payload manifest schema.'
    # The legacy EXE input manifest remains as provenance for shared inputs.
    # The authoritative distribution manifest is the immutable MSI manifest below.
    Assert-True (@($manifest.payload).Count -eq 8) 'The payload manifest must contain 8 binary inputs.'
    Assert-True ($manifest.expectedGame.sha256 -match '^[0-9A-F]{64}$') 'Invalid expected game hash.'
    Assert-True ($manifest.installer.sha256 -match '^[0-9A-F]{64}$') 'Invalid installer hash.'
    Assert-True ($manifest.installer.releaseAsset -match '^[A-Za-z0-9._-]+\.exe$') 'Invalid release asset name.'
    foreach ($entry in $manifest.payload) {
        Assert-True ([string]$entry.sourcePath -ne '') 'Payload entry without sourcePath.'
        Assert-True ([string]$entry.destinationPath -ne '') 'Payload entry without destinationPath.'
        Assert-True ([string]$entry.sha256 -match '^[0-9A-F]{64}$') "Invalid payload hash: $($entry.sourcePath)"
    }

    $msiManifest = (Read-Utf8 'release/manifest-v0.2.11.json') | ConvertFrom-Json
    Assert-True ($msiManifest.version -eq '0.2.11') 'Unexpected MSI manifest version.'
    Assert-True ($msiManifest.installer -eq 'BioShock-VR-DLSS-DLAA-0.2.11.msi') 'Unexpected MSI asset name.'
    Assert-True ($msiManifest.testFamily -eq '') 'An isolated test MSI must not be published.'
    Assert-True ($msiManifest.launcherAutoStart -eq $false) 'MSI must not auto-start the launcher.'
    Assert-True ($msiManifest.runtime -eq '310.7.0.0') 'Unexpected bundled runtime.'
    Assert-True (@($msiManifest.files).Count -eq 23) 'The MSI must have 23 explicit payload files.'
    foreach ($entry in $msiManifest.files) {
        Assert-True (-not [IO.Path]::IsPathRooted($entry.path)) 'Absolute payload path.'
        Assert-True ($entry.path -notmatch '(^|[\\/])\.\.([\\/]|$)|BioshockHD\.exe') 'Unsafe or game payload path.'
        Assert-True ($entry.sha256 -match '^[0-9A-F]{64}$') "Invalid MSI payload hash: $($entry.path)"
    }
    $sumLine = (Read-Utf8 'release/SHA256SUMS-v0.2.11.txt').Trim()
    $expectedSum = "$($msiManifest.sha256)  $($msiManifest.installer)"
    Assert-True ($sumLine -eq $expectedSum) 'Release checksum does not match the MSI manifest.'
    $msiBuilder = Read-Utf8 'installer/msi/Build-Msi.ps1'
    Assert-True ($msiBuilder.Contains('ya se ha entregado. Usa una versión nueva')) 'Delivered MSI version guard is missing.'
    $preset = (Read-Utf8 'CMakePresets.json') | ConvertFrom-Json
    $stable = @($preset.configurePresets | Where-Object name -eq 'stable-win32')[0]
    foreach ($flag in @('BVR_DLSS_OVERLAP','BVR_DEPTH_COPY_REUSE','BVR_DLSS_TAIL_OVERLAP','BVR_DLSS_EARLY_DELIVERY')) {
        Assert-True ($stable.cacheVariables.$flag -eq 'ON') "Stable optimization disabled: $flag"
    }
    foreach ($flag in @('BVR_PERFORMANCE_PROBE','BVR_LATENCY_PROBE','BVR_CRITICAL_PATH_PROBE')) {
        Assert-True ($stable.cacheVariables.$flag -eq 'OFF') "Diagnostic enabled in stable build: $flag"
    }

    $cmake = Read-Utf8 'CMakeLists.txt'
    Assert-True ($cmake.Contains('set(BVR_DISTRIBUTION_VERSION "0.2.12")')) 'CMake distribution version is not 0.2.12.'
    Assert-True ($cmake.Contains('project(BioshockVR VERSION 0.8.2')) 'The upstream base must remain v0.8.2.'

    $launcher = Read-Utf8 'apps/launcher/src/BioshockVrLauncher.cs'
    Assert-True ($launcher.Contains('[assembly: AssemblyVersion("0.2.12.0")]')) 'Launcher version is not 0.2.12.0.'
    Assert-True ($launcher.Contains('UiLanguage.Initialize(_dlssConfigPath)')) 'Launcher language selection is not loaded from dlss.ini.'
    Assert-True ($launcher.Contains('private const bool FinalDlssEdition = true;')) 'Final launcher policy is not enabled.'
    Assert-True ($launcher.Contains('InitializeHiddenIniEditor();')) 'Hidden INI infrastructure is missing.'
    Assert-True ($launcher.Contains('fxaaGroup.Visible = !FinalDlssEdition;')) 'FXAA visibility guard is missing.'
    Assert-True ($launcher.Contains('upscalerGroup.Visible = !FinalDlssEdition;')) 'Spatial upscaler visibility guard is missing.'
    Assert-True ($launcher.Contains('FormatIniSwitch(fxaa.OriginalValue, false)')) 'FXAA disable policy is missing.'
    Assert-True ($launcher.Contains('WarnAboutUntestedRuntime()')) 'Untested DLSS runtime warning is missing.'
    Assert-True ($launcher.Contains('status.RuntimeFound && status.RuntimeIs64Bit')) 'x64 alternate runtime acceptance is missing.'
    Assert-True ($launcher.Contains('string.Equals(status.RuntimeVersion, DlssConfigDocument.TestedRuntimeDisplay')) 'The tested runtime comparison must include all four version fields.'
    Assert-True ($launcher.Contains('BeginSteamLaunchWatch();')) 'Steam launch confirmation is missing.'
    Assert-True ($launcher.Contains('DateTime.UtcNow.AddSeconds(30)')) 'Steam launch timeout is not 30 seconds.'
    Assert-True ($launcher.Contains('Steam ha aceptado la orden, pero BioShock no se ha abierto en 30 segundos.')) 'Steam timeout guidance is missing.'
    Assert-True ($launcher.Contains('TryStartGameDirectly();')) 'Direct launch fallback is missing.'
    Assert-True ($launcher.Contains('SetStatus("BioShock VR se ha iniciado mediante Steam.", Success);')) 'Confirmed Steam startup status is missing.'
    Assert-True ($launcher.Contains('SetStatus("BioShock VR se está iniciando directamente.", Success);')) 'Direct startup status is missing.'
    Assert-True ($launcher -match 'Process\.Start\(steam\);\s+BeginSteamLaunchWatch\(\);') 'The launcher closes or skips confirmation immediately after sending the Steam URI.'
    Assert-True ([regex]::Matches($launcher, '^[ \t]*Close\(\);', [Text.RegularExpressions.RegexOptions]::Multiline).Count -ge 3) 'The launcher does not close after confirmed successful launch paths.'
    $hiddenStart = $launcher.IndexOf('private void InitializeHiddenIniEditor()', [StringComparison]::Ordinal)
    $hiddenEnd = $launcher.IndexOf('private Label MakeToolbarLabel', $hiddenStart, [StringComparison]::Ordinal)
    Assert-True ($hiddenStart -ge 0 -and $hiddenEnd -gt $hiddenStart) 'Could not inspect hidden INI editor method.'
    $hiddenMethod = $launcher.Substring($hiddenStart, $hiddenEnd - $hiddenStart)
    Assert-True (-not $hiddenMethod.Contains('TabPages.Add(page)')) 'The full Bioshock.ini page is exposed.'

    foreach ($releaseFile in @('README.md', 'PROVENANCE.md', 'docs/releases/v0.2.11-public.md')) {
        Assert-True (-not (Read-Utf8 $releaseFile).Contains('PENDIENTE_DE_COMPILACION_FINAL')) "Pending release hash in $releaseFile."
    }

    & git grep -n -I -E 'C:\\Users\\Beren|E:\\SteamLibrary|gho_[A-Za-z0-9_]{20,}' -- .
    $grepExit = $LASTEXITCODE
    Assert-True ($grepExit -eq 1) 'A personal path or credential-like value is tracked.'
    $global:LASTEXITCODE = 0

    if ($BuildLauncher) {
        & (Join-Path $repoRoot 'apps\launcher\Build-Launcher.ps1') | Out-Host
        Assert-True ($LASTEXITCODE -eq 0) 'Launcher build failed.'
        $launcherExe = Join-Path $repoRoot 'artifacts\launcher\Lanzador BioShock VR DLSS-DLAA.exe'
        Assert-True (Test-Path -LiteralPath $launcherExe -PathType Leaf) 'Launcher build output is missing.'
        $process = Start-Process -FilePath $launcherExe -ArgumentList '--self-test' -WindowStyle Hidden -Wait -PassThru
        try {
            Assert-True ($process.ExitCode -eq 0) "Launcher self-test failed with exit code $($process.ExitCode)."
        }
        finally {
            $process.Dispose()
        }
    }

    Write-Host 'PASS: repository policy, provenance, version and checksums'
    if ($BuildLauncher) { Write-Host 'PASS: launcher build and self-test' }
}
finally {
    Pop-Location
}
