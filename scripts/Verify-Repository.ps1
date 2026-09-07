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
        'release/SHA256SUMS-v0.2.1-beta.txt',
        'apps/launcher/src/BioshockVrLauncher.cs',
        'installer/src/BioshockVrDlss45StandaloneInstaller.cs'
    )
    foreach ($file in $required) { $null = Read-Utf8 $file }

    $manifestText = Read-Utf8 'installer/payload-manifest.json'
    $manifest = $manifestText | ConvertFrom-Json
    Assert-True ($manifest.schemaVersion -eq 1) 'Unexpected payload manifest schema.'
    Assert-True ($manifest.release -eq 'v0.2.1-beta') 'Unexpected payload manifest release.'
    Assert-True (@($manifest.payload).Count -eq 8) 'The payload manifest must contain 8 binary inputs.'
    Assert-True ($manifest.expectedGame.sha256 -match '^[0-9A-F]{64}$') 'Invalid expected game hash.'
    Assert-True ($manifest.installer.sha256 -match '^[0-9A-F]{64}$') 'Invalid installer hash.'
    foreach ($entry in $manifest.payload) {
        Assert-True ([string]$entry.sourcePath -ne '') 'Payload entry without sourcePath.'
        Assert-True ([string]$entry.destinationPath -ne '') 'Payload entry without destinationPath.'
        Assert-True ([string]$entry.sha256 -match '^[0-9A-F]{64}$') "Invalid payload hash: $($entry.sourcePath)"
    }

    $sumLine = (Read-Utf8 'release/SHA256SUMS-v0.2.1-beta.txt').Trim()
    $expectedSum = "$($manifest.installer.sha256) *$($manifest.installer.file)"
    Assert-True ($sumLine -eq $expectedSum) 'Release checksum does not match the payload manifest.'

    $cmake = Read-Utf8 'CMakeLists.txt'
    Assert-True ($cmake.Contains('set(BVR_DISTRIBUTION_VERSION "0.2.1-beta")')) 'CMake distribution version is not v0.2.1-beta.'
    Assert-True ($cmake.Contains('project(BioshockVR VERSION 0.8.2')) 'The upstream base must remain v0.8.2.'

    $launcher = Read-Utf8 'apps/launcher/src/BioshockVrLauncher.cs'
    Assert-True ($launcher.Contains('private const bool FinalDlssEdition = true;')) 'Final launcher policy is not enabled.'
    Assert-True ($launcher.Contains('InitializeHiddenIniEditor();')) 'Hidden INI infrastructure is missing.'
    Assert-True ($launcher.Contains('fxaaGroup.Visible = !FinalDlssEdition;')) 'FXAA visibility guard is missing.'
    Assert-True ($launcher.Contains('upscalerGroup.Visible = !FinalDlssEdition;')) 'Spatial upscaler visibility guard is missing.'
    Assert-True ($launcher.Contains('FormatIniSwitch(fxaa.OriginalValue, false)')) 'FXAA disable policy is missing.'
    $hiddenStart = $launcher.IndexOf('private void InitializeHiddenIniEditor()', [StringComparison]::Ordinal)
    $hiddenEnd = $launcher.IndexOf('private Label MakeToolbarLabel', $hiddenStart, [StringComparison]::Ordinal)
    Assert-True ($hiddenStart -ge 0 -and $hiddenEnd -gt $hiddenStart) 'Could not inspect hidden INI editor method.'
    $hiddenMethod = $launcher.Substring($hiddenStart, $hiddenEnd - $hiddenStart)
    Assert-True (-not $hiddenMethod.Contains('TabPages.Add(page)')) 'The full Bioshock.ini page is exposed.'

    $installer = Read-Utf8 'installer/src/BioshockVrDlss45StandaloneInstaller.cs'
    Assert-True ([regex]::Matches($installer, 'new Payload\(').Count -eq 20) 'Installer source must embed 20 resources.'
    Assert-True ($installer.Contains('[assembly: AssemblyVersion("0.2.1.0")]')) 'Installer version is not 0.2.1.0.'

    & git grep -n -I -E 'C:\\Users\\Beren|E:\\SteamLibrary|gho_[A-Za-z0-9_]{20,}' -- .
    $grepExit = $LASTEXITCODE
    Assert-True ($grepExit -eq 1) 'A personal path or credential-like value is tracked.'
    $global:LASTEXITCODE = 0

    if ($BuildLauncher) {
        & (Join-Path $repoRoot 'apps\launcher\Build-Launcher.ps1') | Out-Host
        Assert-True ($LASTEXITCODE -eq 0) 'Launcher build failed.'
        $launcherExe = Join-Path $repoRoot 'artifacts\launcher\Lanzador BioShock VR DLSS 4.5.exe'
        Assert-True (Test-Path -LiteralPath $launcherExe -PathType Leaf) 'Launcher build output is missing.'
        $process = Start-Process -FilePath $launcherExe -ArgumentList '--self-test' -Wait -PassThru
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
