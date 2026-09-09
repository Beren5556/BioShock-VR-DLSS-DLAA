$ErrorActionPreference = 'Stop'

$projectRoot = Split-Path -Parent $PSCommandPath
$repoRoot = [IO.Path]::GetFullPath((Join-Path $projectRoot '..\..'))
$sourcePath = Join-Path $projectRoot 'src\BioshockVrLauncher.cs'
$iconPath = Join-Path $projectRoot 'assets\BioshockVrLauncher.ico'
$buildDirectory = Join-Path $repoRoot 'artifacts\launcher'
$finalExe = Join-Path $buildDirectory 'Lanzador BioShock VR DLSS-DLAA.exe'
$temporaryDirectory = Join-Path $buildDirectory ('.launcher-build-' + [Guid]::NewGuid().ToString('N'))
$compilerSource = Join-Path $temporaryDirectory 'BioshockVrLauncher.utf8.cs'
$temporaryExe = Join-Path $temporaryDirectory 'Lanzador BioShock VR DLSS-DLAA.exe'

New-Item -ItemType Directory -Path $buildDirectory -Force | Out-Null
New-Item -ItemType Directory -Path $temporaryDirectory | Out-Null

$strictUtf8 = New-Object System.Text.UTF8Encoding($false, $true)
$sourceText = [System.IO.File]::ReadAllText($sourcePath, $strictUtf8)
$utf8WithBom = New-Object System.Text.UTF8Encoding($true)
[System.IO.File]::WriteAllText($compilerSource, $sourceText, $utf8WithBom)

$compilerPath = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compilerPath)) {
    $compilerPath = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
}
if (-not (Test-Path -LiteralPath $compilerPath)) {
    throw 'No se encontró el compilador C# de .NET Framework.'
}

$compilerArguments = @(
    '/nologo',
    '/codepage:65001',
    '/target:winexe',
    '/optimize+',
    '/platform:anycpu',
    ('/win32icon:' + $iconPath),
    ('/out:' + $temporaryExe),
    '/reference:System.dll',
    '/reference:System.Core.dll',
    '/reference:System.Drawing.dll',
    '/reference:System.Windows.Forms.dll',
    $compilerSource,
    (Join-Path $projectRoot 'src\ImageTab.cs'),
    (Join-Path $projectRoot 'src\LauncherLocalization.cs')
)
try {
    & $compilerPath $compilerArguments
    if ($LASTEXITCODE -ne 0) {
        throw "La compilación del lanzador terminó con código $LASTEXITCODE."
    }

    if (-not (Test-Path -LiteralPath $temporaryExe)) {
        throw 'El compilador no generó el ejecutable temporal.'
    }

    Move-Item -LiteralPath $temporaryExe -Destination $finalExe -Force
    $finalExe
}
finally {
    if (Test-Path -LiteralPath $temporaryExe) { Remove-Item -LiteralPath $temporaryExe -Force }
    if (Test-Path -LiteralPath $compilerSource) { Remove-Item -LiteralPath $compilerSource -Force }
    if (Test-Path -LiteralPath $temporaryDirectory) { Remove-Item -LiteralPath $temporaryDirectory -Force }
}
