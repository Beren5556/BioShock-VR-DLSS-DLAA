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
        '(^|/)Bioshock(2)?HD\.exe$',
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
        'apps/launcher/src/BioshockVrLauncher.cs',
        'apps/launcher/src/ImageTab.cs',
        'installer/msi/Build-Msi.ps1',
        'installer/msi/Package.wxs',
        'installer/msi/Interface.wxs',
        'installer/msi/MsiActions.cs',
        'installer/msi/GamePackage.cs',
        'installer/msi/LegacyMigration.cs',
        'installer/unified/Bundle.cs',
        'apps/launcher/src/GameProfile.cs',
        'src/game/shared/temporal_adapter.cpp',
        'src/game/shared/image_adapter.cpp',
        'docs/INTEGRATION-0.2.13.md'
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
    $dualManifestPath = Join-Path $repoRoot 'release/manifest-v0.2.16.json'
    if (Test-Path -LiteralPath $dualManifestPath) {
        $dual = (Read-Utf8 'release/manifest-v0.2.16.json') | ConvertFrom-Json
        $validated = (Read-Utf8 'release/validated-mods-v0.2.16.json') | ConvertFrom-Json
        Assert-True ($dual.kind -eq 'native-single-game-msi' -and $dual.version -eq '0.2.16') 'Wrong final dual MSI identity.'
        Assert-True ($dual.testFamily -eq '' -and $dual.releaseReady -eq $true) 'Only the production package can close the release.'
        Assert-True ($dual.installer -eq 'BioShock-1-2-VR-DLSS-DLAA-0.2.16.msi' -and -not $dual.launcherAutoStart) 'Unexpected final asset/startup policy.'
        Assert-True ((Read-Utf8 'release/SHA256SUMS-v0.2.16.txt').Trim() -eq "$($dual.sha256)  $($dual.installer)") 'Final MSI checksum mismatch.'
        foreach ($id in @('bs1','bs2')) {
            Assert-True (@($dual.games.$id.files).Count -eq 23) 'Each mod must contain exactly 23 inventoried files.'
            Assert-True ($dual.games.$id.modBuild -eq $validated.modBuild -and $dual.games.$id.runtime -eq '310.7.0.0') 'Wrong tested core/runtime.'
            foreach ($entry in $dual.games.$id.files) {
                Assert-True (-not [IO.Path]::IsPathRooted($entry.path) -and $entry.path -notmatch '(^|[\\/])\.\.([\\/]|$)|Bioshock(2)?HD\.exe|:') 'Unsafe payload path or game executable.'
                Assert-True ($entry.sha256 -match '^[0-9A-F]{64}$') 'Invalid final file hash.'
            }
            foreach ($entry in $validated.games.$id.files) {
                $actual = @($dual.games.$id.files | Where-Object { $_.path.Replace('/', '\') -eq $entry.path.Replace('/', '\') })
                Assert-True ($actual.Count -eq 1 -and $actual[0].sha256 -eq $entry.sha256) "Validated binary/fix missing: $id $($entry.path)"
            }
        }
        Assert-True ($dual.games.bs1.productCode -ne $dual.games.bs2.productCode -and $dual.games.bs1.registration -ne $dual.games.bs2.registration) 'Game ownership must remain independent.'
        foreach ($path in @('docs/releases/v0.2.16.md','docs/releases/v0.2.16-performance.md','docs/RELEASE-0.2.16.md','installer/single-game/Prepare-Release.ps1','installer/single-game/Build-Msi.ps1','installer/single-game/Verify-Package.ps1')) { $null = Read-Utf8 $path }
    }
    $english = (Read-Utf8 'release/manifest-v0.2.17.json') | ConvertFrom-Json
    $englishPins = (Read-Utf8 'release/validated-mods-v0.2.17.json') | ConvertFrom-Json
    $englishValidation = (Read-Utf8 'release/validation-v0.2.17.json') | ConvertFrom-Json
    Assert-True ($english.version -eq '0.2.17' -and $english.kind -eq 'native-single-game-msi') 'Wrong English release identity.'
    Assert-True ($english.installer -eq 'BioShock-1-2-VR-DLSS-DLAA-0.2.17-EN.msi') 'Wrong English asset name.'
    Assert-True ($english.testFamily -eq '' -and $english.releaseReady -and -not $english.launcherAutoStart) 'English release must be the production MSI without auto-start.'
    Assert-True ($english.sha256 -match '^[0-9A-F]{64}$') 'Invalid English installer hash.'
    Assert-True ((Read-Utf8 'release/SHA256SUMS-v0.2.17.txt').Trim() -eq "$($english.sha256)  $($english.installer)") 'English release checksum mismatch.'
    Assert-True ($englishPins.version -eq '0.2.17' -and $englishPins.baseTag -eq 'v0.2.16') 'Wrong English binary pin provenance.'
    foreach ($id in @('bs1','bs2')) {
        $game = $english.games.$id
        Assert-True (@($game.files).Count -eq 23) 'Each English mod must contain 23 explicit files.'
        Assert-True ($game.modVersion -eq '0.2.17' -and $game.modBuild -eq $englishPins.modBuild -and $game.runtime -eq '310.7.0.0') 'Wrong English core/runtime metadata.'
        foreach ($entry in $game.files) {
            Assert-True (-not [IO.Path]::IsPathRooted($entry.path) -and $entry.path -notmatch '(^|[\\/])\.\.([\\/]|$)|Bioshock(2)?HD\.exe|:') 'Unsafe English payload path.'
            Assert-True ($entry.sha256 -match '^[0-9A-F]{64}$') 'Invalid English payload hash.'
        }
        foreach ($entry in $englishPins.games.$id.files) {
            $actual = @($game.files | Where-Object { $_.path -eq $entry.path })
            Assert-True ($actual.Count -eq 1 -and $actual[0].sha256 -eq $entry.sha256) "English binary pin mismatch: $id $($entry.path)"
        }
        foreach ($unchangedPath in @('host64\BioShockVR-DLSS45-Host64.exe','host64\nvngx_dlss.dll','xinput1_3.dll','host64\dlss-capabilities.ini')) {
            $before = @($dual.games.$id.files | Where-Object path -eq $unchangedPath)
            $after = @($game.files | Where-Object path -eq $unchangedPath)
            Assert-True ($before.Count -eq 1 -and $after.Count -eq 1 -and $before[0].sha256 -eq $after[0].sha256) "English edition changed a frozen dependency: $id $unchangedPath"
        }
        Assert-True ($game.registration -eq $dual.games.$id.registration -and $game.legacyUpgradeCode -eq $dual.games.$id.legacyUpgradeCode) 'English edition changed game upgrade ownership.'
    }
    Assert-True ($english.games.bs1.productCode -ne $english.games.bs2.productCode -and $english.games.bs1.registration -ne $english.games.bs2.registration) 'English game ownership must remain independent.'
    Assert-True ($englishValidation.installerSha256 -eq $english.sha256 -and $englishValidation.version -eq '0.2.17') 'English validation is for a different installer.'
    Assert-True ($englishValidation.sourceEquivalence.passed -and $englishValidation.coreTests.passed -and $englishValidation.nvidiaRuntimeTests.passed -and $englishValidation.msi.passed) 'English validation is incomplete.'
    Assert-True ($englishValidation.msi.isolatedStandardChecks -eq 74 -and $englishValidation.msi.isolatedUpgradeChecks -eq 56 -and $englishValidation.msi.isolatedStandardOperations -eq 12 -and $englishValidation.msi.isolatedUpgradeOperations -eq 10) 'English isolated transaction evidence is incomplete.'
    Assert-True (-not $englishValidation.installedGamesModified -and -not $englishValidation.nvidiaRuntimeTests.usesGameOrHeadset) 'English validation must not overclaim game acceptance.'
    foreach ($path in @('docs/ENGLISH-0.2.17.md','docs/releases/v0.2.17.md','docs/releases/v0.2.17-public.md','docs/releases/v0.2.17-performance.md','localization/en-US.json','scripts/localization/english.mjs')) { $null = Read-Utf8 $path }
    $msiBuilder = Read-Utf8 'installer/msi/Build-Msi.ps1'
    Assert-True ($msiBuilder.Contains('ya se ha entregado. Usa una versión nueva')) 'Delivered MSI version guard is missing.'
    $preset = (Read-Utf8 'CMakePresets.json') | ConvertFrom-Json
    $stable = @($preset.configurePresets | Where-Object name -eq 'integration-win32')[0]
    foreach ($flag in @('BVR_DLSS_OVERLAP','BVR_DEPTH_COPY_REUSE','BVR_DLSS_TAIL_OVERLAP','BVR_DLSS_EARLY_DELIVERY')) {
        Assert-True ($stable.cacheVariables.$flag -eq 'ON') "Stable optimization disabled: $flag"
    }
    foreach ($flag in @('BVR_PERFORMANCE_PROBE','BVR_LATENCY_PROBE','BVR_CRITICAL_PATH_PROBE')) {
        Assert-True ($stable.cacheVariables.$flag -eq 'OFF') "Diagnostic enabled in stable build: $flag"
    }

    $cmake = Read-Utf8 'CMakeLists.txt'
    Assert-True ($cmake.Contains('set(BVR_DISTRIBUTION_VERSION "0.2.17")')) 'CMake distribution version is not 0.2.17.'
    Assert-True ($cmake.Contains('project(BioshockVR VERSION 0.8.2')) 'The upstream base must remain v0.8.2.'

    $launcher = Read-Utf8 'apps/launcher/src/BioshockVrLauncher.cs'
    Assert-True ($launcher.Contains('[assembly: AssemblyVersion("0.2.17.0")]')) 'Launcher version is not 0.2.17.0.'
    Assert-True ($launcher.Contains('private const bool FinalDlssEdition = true;')) 'Final launcher policy is not enabled.'
    Assert-True ($launcher.Contains('InitializeHiddenIniEditor();')) 'Hidden INI infrastructure is missing.'
    Assert-True ($launcher.Contains('fxaaGroup.Visible = !FinalDlssEdition;')) 'FXAA visibility guard is missing.'
    Assert-True ($launcher.Contains('upscalerGroup.Visible = !FinalDlssEdition;')) 'Spatial upscaler visibility guard is missing.'
    Assert-True ($launcher.Contains('FormatIniSwitch(fxaa.OriginalValue, false)')) 'FXAA disable policy is missing.'
    Assert-True ($launcher.Contains('WarnAboutUntestedRuntime()')) 'Untested DLSS runtime warning is missing.'
    Assert-True ($launcher.Contains('status.RuntimeFound && status.RuntimeIs64Bit')) 'x64 alternate runtime acceptance is missing.'
    Assert-True ($launcher.Contains('string.Equals(status.RuntimeVersion, DlssConfigDocument.TestedRuntimeDisplay')) 'The tested runtime comparison must include all four version fields.'
    $support = Read-Utf8 'apps/launcher/src/Bioshock2Support.cs'
    Assert-True ($launcher.Contains('new GameLaunchTracker(')) 'Game launch confirmation is missing.'
    Assert-True ($support.Contains('TotalSeconds >= 60')) 'Game launch timeout must be bounded to 60 seconds.'
    Assert-True ($support.Contains('TotalSeconds >= 3')) 'Responsive-window stability check is missing.'
    Assert-True ($support.Contains('GameLaunchEvidence.Matches(')) 'Exact game process evidence is missing.'
    Assert-True ($launcher.Contains('LaunchOutcome.Started')) 'The launcher must close on confirmed startup.'
    Assert-True ($launcher.Contains('GameProfile.VerifyExecutable(')) 'Compatible executable validation is missing.'
    $profile = Read-Utf8 'apps/launcher/src/GameProfile.cs'
    Assert-True ($profile.Contains('"409710"') -and $profile.Contains('"409720"')) 'Both game identities are required.'
    Assert-True ($profile.Contains('Path.Combine(local, "bs2")')) 'BS2 profile must be isolated.'
    $bundle = Read-Utf8 'installer/unified/Build-Bundle.ps1'
    Assert-True ($bundle.Contains($msiManifest.sha256)) 'The selector must embed the exact accepted BS1 MSI.'
    Assert-True ($bundle.Contains('$bs2.testFamily')) 'A test-family MSI must never be embedded.'
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
        foreach ($game in @('bs1','bs2')) {
            $name = if ($game -eq 'bs2') { 'Lanzador BioShock 2 VR DLSS-DLAA.exe' } else { 'Lanzador BioShock VR DLSS-DLAA.exe' }
            $launcherExe = Join-Path $repoRoot ('artifacts/integration-0.2.17/launcher/' + $game + '/' + $name)
            Assert-True (Test-Path -LiteralPath $launcherExe -PathType Leaf) 'Launcher build output is missing.'
            $process = Start-Process -FilePath $launcherExe -ArgumentList '--self-test' -WindowStyle Hidden -PassThru
            try {
                Assert-True ($process.WaitForExit(45000)) 'Launcher self-test did not complete in 45 seconds.'
                Assert-True ($process.ExitCode -eq 0) "$game launcher self-test failed with exit code $($process.ExitCode)."
            } finally { $process.Dispose() }
        }
    }

    Write-Host 'PASS: repository policy, provenance, version and checksums'
    if ($BuildLauncher) { Write-Host 'PASS: launcher build and self-test' }
}
finally {
    Pop-Location
}
