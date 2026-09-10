param(
    [ValidateSet('bs1', 'bs2', 'both')]
    [string]$Game = 'both',
    [string]$OutputDirectory
)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSCommandPath
$repoRoot = [IO.Path]::GetFullPath((Join-Path $projectRoot '..\..'))
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $repoRoot 'artifacts\integration-0.2.17\launcher' }
$compilerPath = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compilerPath)) {
    $compilerPath = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
}
if (-not (Test-Path -LiteralPath $compilerPath)) { throw 'The .NET Framework C# compiler was not found.' }
$sources = @('BioshockVrLauncher.cs', 'ImageTab.cs', 'Bioshock2Support.cs', 'Bioshock2LauncherTests.cs', 'GameProfile.cs') |
    ForEach-Object { Join-Path $projectRoot ('src\' + $_) }
$games = if ($Game -eq 'both') { @('bs1', 'bs2') } else { @($Game) }
foreach ($selectedGame in $games) {
    $buildDirectory = Join-Path $OutputDirectory $selectedGame
    New-Item -ItemType Directory -Path $buildDirectory -Force | Out-Null
    $name = if ($selectedGame -eq 'bs2') { 'Lanzador BioShock 2 VR DLSS-DLAA.exe' } else { 'Lanzador BioShock VR DLSS-DLAA.exe' }
    $finalExe = Join-Path $buildDirectory $name
    $temporaryExe = Join-Path $buildDirectory ([Guid]::NewGuid().ToString('N') + '.exe')
    $arguments = @('/nologo', '/codepage:65001', '/target:winexe', '/optimize+', '/platform:anycpu',
        ('/win32icon:' + (Join-Path $projectRoot 'assets\BioshockVrLauncher.ico')),
        ('/out:' + $temporaryExe), '/reference:System.dll', '/reference:System.Core.dll',
        '/reference:System.Drawing.dll', '/reference:System.Windows.Forms.dll')
    if ($selectedGame -eq 'bs2') { $arguments += '/define:BIOSHOCK2' }
    try {
        & $compilerPath @arguments @sources
        if ($LASTEXITCODE -ne 0) { throw "The $selectedGame build exited with code $LASTEXITCODE." }
        if ([Diagnostics.FileVersionInfo]::GetVersionInfo($temporaryExe).FileVersion -ne '0.2.17.0') {
            throw 'Incorrect launcher version.'
        }
        Move-Item -LiteralPath $temporaryExe -Destination $finalExe -Force
        $finalExe
    } finally {
        if (Test-Path -LiteralPath $temporaryExe) { Remove-Item -LiteralPath $temporaryExe -Force }
    }
}
