[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$GameExecutable,

    [string[]]$ProtectedPaths = @(),

    [string]$TestOutputRoot = ''
)

$ErrorActionPreference = 'Stop'
$sourceRoot = [IO.Path]::GetFullPath($PSScriptRoot)
$repoRoot = [IO.Path]::GetFullPath((Join-Path $sourceRoot '..'))
$installer = Join-Path $repoRoot 'artifacts\release\Instalador BioShock VR DLSS-DLAA Beta 0.2.exe'
$launcherPayload = Join-Path $sourceRoot 'Payload\Lanzador BioShock VR DLSS-DLAA.exe'
$gameSource = [IO.Path]::GetFullPath($GameExecutable)
if ([string]::IsNullOrWhiteSpace($TestOutputRoot)) {
    $TestOutputRoot = Join-Path ([IO.Path]::GetTempPath()) 'BvrInstallerTests'
}
$testParent = [IO.Path]::GetFullPath($TestOutputRoot)
$testRoot = Join-Path $testParent ('BvrStandaloneTest-' + [Guid]::NewGuid().ToString('N'))
$safePrefix = $testParent.TrimEnd('\') + '\'

function Assert-True([bool]$condition, [string]$message) {
    if (-not $condition) { throw $message }
}

function Invoke-WinExe([string]$file, [string[]]$arguments) {
    $quoted = foreach ($argument in $arguments) {
        if ($argument -match '[\s"]') { '"' + $argument.Replace('"', '\"') + '"' } else { $argument }
    }
    Start-Process -FilePath $file -ArgumentList $quoted -PassThru -Wait
}

function Close-ExactProcess([Diagnostics.Process]$process, [string]$label) {
    $process.Refresh()
    Assert-True (-not $process.HasExited) "$label se cerró antes de mostrar su interfaz."
    $processId = $process.Id
    $null = $process.CloseMainWindow()
    if (-not $process.WaitForExit(5000)) {
        $process.Kill()
        Assert-True ($process.WaitForExit(5000)) "No se pudo cerrar el PID $processId de $label."
    }
}

function Start-Smoke([string]$file, [string[]]$arguments, [string]$label) {
    $quoted = foreach ($argument in $arguments) {
        if ($argument -match '[\s"]') { '"' + $argument.Replace('"', '\"') + '"' } else { $argument }
    }
    $process = Start-Process -FilePath $file -ArgumentList $quoted -PassThru
    try {
        try { $null = $process.WaitForInputIdle(5000) } catch { }
        $deadline = [DateTime]::UtcNow.AddSeconds(10)
        do {
            $process.Refresh()
            if ($process.HasExited -or $process.MainWindowHandle -ne [IntPtr]::Zero) { break }
            Start-Sleep -Milliseconds 100
        } while ([DateTime]::UtcNow -lt $deadline)
        Assert-True (-not $process.HasExited) "$label no permaneció abierto."
        Assert-True ($process.MainWindowHandle -ne [IntPtr]::Zero) "$label no creó una ventana principal."
        Close-ExactProcess $process $label
    }
    finally {
        $process.Refresh()
        if (-not $process.HasExited) {
            $process.Kill()
            $null = $process.WaitForExit(5000)
        }
        $process.Dispose()
    }
}

function Get-Fingerprint([string]$path) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { return '<missing>' }
    $item = Get-Item -LiteralPath $path
    '{0}|{1}|{2}' -f $item.Length, $item.LastWriteTimeUtc.Ticks,
        (Get-FileHash -Algorithm SHA256 -LiteralPath $path).Hash
}

function Snapshot-Paths([string[]]$paths) {
    $snapshot = @{}
    foreach ($path in $paths) { $snapshot[$path] = Get-Fingerprint $path }
    $snapshot
}

function Assert-Snapshot([hashtable]$before, [string]$label) {
    foreach ($path in $before.Keys) {
        $after = Get-Fingerprint $path
        Assert-True ($after -eq $before[$path]) "$label modificó un archivo real: $path"
    }
}

function New-Fixture([string]$name) {
    $final = Join-Path $testRoot ($name + '\Build\Final')
    New-Item -ItemType Directory -Path $final -Force | Out-Null
    Copy-Item -LiteralPath $gameSource -Destination (Join-Path $final 'BioshockHD.exe')
    $final
}

function Invoke-IntegrationInstall([string]$gameDirectory, [string]$stateRoot, [string]$resultPath) {
    $process = Invoke-WinExe $installer @('--integration-install', $gameDirectory, $stateRoot, $resultPath)
    Assert-True ($process.ExitCode -eq 0) "La instalación aislada terminó con código $($process.ExitCode)."
    Assert-True (Test-Path -LiteralPath $resultPath -PathType Leaf) 'La instalación aislada no produjo resultado.'
    Assert-True ((Get-Content -LiteralPath $resultPath -Encoding UTF8 -Raw).StartsWith('PASS:')) 'La instalación aislada no indicó PASS.'
}

function Invoke-IntegrationRestore([string]$stateRoot, [string]$resultPath) {
    $process = Invoke-WinExe $installer @('--integration-restore', $stateRoot, $resultPath)
    Assert-True ($process.ExitCode -eq 0) "La restauración aislada terminó con código $($process.ExitCode)."
    Assert-True ((Get-Content -LiteralPath $resultPath -Encoding UTF8 -Raw).StartsWith('PASS:')) 'La restauración aislada no indicó PASS.'
}

Assert-True (Test-Path -LiteralPath $installer -PathType Leaf) "Falta el instalador: $installer"
Assert-True (Test-Path -LiteralPath $launcherPayload -PathType Leaf) "Falta el lanzador: $launcherPayload"
Assert-True (Test-Path -LiteralPath $gameSource -PathType Leaf) "Falta el juego de referencia: $gameSource"
Assert-True ($testRoot.StartsWith($safePrefix, [StringComparison]::OrdinalIgnoreCase)) 'La raíz de prueba no es segura.'

$realPaths = @($ProtectedPaths | ForEach-Object { [IO.Path]::GetFullPath($_) })

$expected = [ordered]@{
    'xinput1_3.dll' = '441BF1728BB38A2EC2BA57605CF840D786122E862D47A6DFA642BFF484F8E191'
    'bioshockvr.dll' = '44B0FB0946330AB7F471D230A3E27D686CDFD400B2CF251B70BE6A9364986E7F'
    'bvr_steamvr32.dll' = '56537A2EA8F88FCE6A2928EAECDE11EEEEED9C4D04F36B39E466B4D03330B972'
    'openvr_api.dll' = 'AB696E4F218A95B3E396BC310F9FE6485DF48C99C0969762083212B1E1F025A6'
    'host64\BioShockVR-DLSS45-Host64.exe' = '480D4A931C0CA5669B11061EFB28239BE6A041D1452BA50EACC30E0B26291453'
    'host64\nvngx_dlss.dll' = 'BE6E434A94CA32499515EB62CA0E6C274526055D568D0426E4C652DCDFB6EE6E'
    'host64\dlss-capabilities.ini' = '7C52BD6F6F186C40CDA847F0E143BDCFF94F0CB9BAC355977C27C2E27B857D77'
    'Lanzador BioShock VR DLSS-DLAA.exe' = '73572143A1791504BF8207411BD7C0BE37061C8244733013A6D35A8B03718315'
    'BioShockVR-DLSS45\LEEME-DLSS45.md' = 'FE70E0E0741A39B1FB814689D805BE8D870A49438D245DF3AE3AB02244CEF7AC'
    'BioShockVR-DLSS45\NVIDIA-DLSS-LICENSE.txt' = 'DC2778A3283427285984CDB5B3F7F03EAE7D8A06057F672F2999C5EE7FD4F67D'
    'BioShockVR-DLSS45\INFORMACION-DEL-PAQUETE.txt' = '461973B3559470DFA26DABCCE551483CDB6965F9F35BFF7C4262604F9206A50B'
    'BioShockVR-DLSS45\dlss.ini.example' = '0C8D1260BC3A5782106D95E6D374CE87D03CA0F36D3C198527F3C3359B601329'
    'BioShockVR-DLSS45\Licenses\BioShockVR-MIT-LICENSE.txt' = '199384980B6925AA5DA072314C0C265BB097F41C7849A7AB0E6DE9294D3D8114'
    'BioShockVR-DLSS45\Licenses\DLSS-Host-MIT-LICENSE.txt' = '1CE240E402901FB81EB82A60A6BAFD2FB913CD5746860B0A4EC52A5ACB49CED7'
    'BioShockVR-DLSS45\Licenses\THIRD_PARTY_NOTICES.md' = '1237069D15F745E7F76F9B8C8E8FB2CB72FD9392783CEFA8123C16E4F3FB3763'
    'BioShockVR-DLSS45\Licenses\MinHook-LICENSE.txt' = '4F21F857550D7BE854DA6EA5F2DA4E6775CA4E3FBB535E4F3D961C47D0BF3335'
    'BioShockVR-DLSS45\Licenses\Dear-ImGui-LICENSE.txt' = 'F20418B409E53C8C9F4E90917FF395554A60320D4DFBF833DA89B339CAD8628A'
    'BioShockVR-DLSS45\Licenses\OpenVR-LICENSE.txt' = '9E6D1480FB68E86CEAFED312F7E67DADCDC2A99B350B710D624B8F0F0F1A2329'
    'BioShockVR-DLSS45\Licenses\OpenXR-LICENSE.txt' = '3DDF9BE5C28FE27DAD143A5DC76EEA25222AD1DD68934A047064E56ED2FA40C5'
    'BioShockVR-DLSS45\Licenses\OpenXR-COPYING.adoc' = '1B0FF1CFEADAE317A54457E4407EA1AE026AB71555CAF9A20294C492F2514273'
}

$success = $false
New-Item -ItemType Directory -Path $testParent -Force | Out-Null
New-Item -ItemType Directory -Path $testRoot | Out-Null
try {
    $realBefore = Snapshot-Paths $realPaths

    $selfTestResult = Join-Path $testRoot 'self-test.txt'
    $selfTest = Invoke-WinExe $installer @('--self-test', $selfTestResult)
    Assert-True ($selfTest.ExitCode -eq 0) "El self-test terminó con código $($selfTest.ExitCode)."
    $selfText = Get-Content -LiteralPath $selfTestResult -Encoding UTF8 -Raw
    Assert-True ($selfText.StartsWith('PASS: 20 recursos')) 'El self-test no verificó los 20 recursos.'

    Start-Smoke $installer @() 'El instalador'

    $cleanGame = New-Fixture 'Clean'
    $cleanState = Join-Path $testRoot 'Clean-State'
    $cleanInstallResult = Join-Path $testRoot 'clean-install.txt'
    $gameHashBefore = (Get-FileHash -Algorithm SHA256 -LiteralPath (Join-Path $cleanGame 'BioshockHD.exe')).Hash
    Invoke-IntegrationInstall $cleanGame $cleanState $cleanInstallResult
    foreach ($entry in $expected.GetEnumerator()) {
        $installed = Join-Path $cleanGame $entry.Key
        Assert-True (Test-Path -LiteralPath $installed -PathType Leaf) "Falta tras instalar: $($entry.Key)"
        Assert-True ((Get-FileHash -Algorithm SHA256 -LiteralPath $installed).Hash -eq $entry.Value) "Hash incorrecto tras instalar: $($entry.Key)"
    }
    Assert-True ((Get-FileHash -Algorithm SHA256 -LiteralPath (Join-Path $cleanGame 'BioshockHD.exe')).Hash -eq $gameHashBefore) 'La instalación alteró BioshockHD.exe.'

    $installedLauncher = Join-Path $cleanGame 'Lanzador BioShock VR DLSS-DLAA.exe'
    Start-Smoke $installedLauncher @('--game', (Join-Path $cleanGame 'BioshockHD.exe')) 'El lanzador'

    $cleanRestoreResult = Join-Path $testRoot 'clean-restore.txt'
    Invoke-IntegrationRestore $cleanState $cleanRestoreResult
    foreach ($relative in $expected.Keys) {
        Assert-True (-not (Test-Path -LiteralPath (Join-Path $cleanGame $relative) -PathType Leaf)) "Restaurar no retiró: $relative"
    }
    Assert-True ((Get-FileHash -Algorithm SHA256 -LiteralPath (Join-Path $cleanGame 'BioshockHD.exe')).Hash -eq $gameHashBefore) 'Restaurar alteró BioshockHD.exe.'
    Assert-True (-not (Test-Path -LiteralPath (Join-Path $cleanState 'install.manifest') -PathType Leaf)) 'Restaurar dejó el manifiesto activo.'

    $upgradeGame = New-Fixture 'Upgrade'
    $upgradeState = Join-Path $testRoot 'Upgrade-State'
    $sentinelSource = Join-Path $repoRoot 'docs\release\INFORMACION-DEL-PAQUETE.txt'
    $sentinelHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $sentinelSource).Hash
    foreach ($relative in $expected.Keys) {
        $destination = Join-Path $upgradeGame $relative
        New-Item -ItemType Directory -Path (Split-Path -Parent $destination) -Force | Out-Null
        Copy-Item -LiteralPath $sentinelSource -Destination $destination
    }
    Invoke-IntegrationInstall $upgradeGame $upgradeState (Join-Path $testRoot 'upgrade-install.txt')
    foreach ($entry in $expected.GetEnumerator()) {
        Assert-True ((Get-FileHash -Algorithm SHA256 -LiteralPath (Join-Path $upgradeGame $entry.Key)).Hash -eq $entry.Value) "La actualización no instaló: $($entry.Key)"
    }
    Invoke-IntegrationRestore $upgradeState (Join-Path $testRoot 'upgrade-restore.txt')
    foreach ($relative in $expected.Keys) {
        Assert-True ((Get-FileHash -Algorithm SHA256 -LiteralPath (Join-Path $upgradeGame $relative)).Hash -eq $sentinelHash) "No se restauró byte a byte: $relative"
    }

    $badGame = Join-Path $testRoot 'Bad\Build\Final'
    New-Item -ItemType Directory -Path $badGame -Force | Out-Null
    $badState = Join-Path $testRoot 'Bad-State'
    $badResult = Join-Path $testRoot 'bad-install.txt'
    $badProcess = Invoke-WinExe $installer @('--integration-install', $badGame, $badState, $badResult)
    Assert-True ($badProcess.ExitCode -eq 1) 'Una carpeta sin BioshockHD.exe no fue rechazada.'
    Assert-True (-not (Test-Path -LiteralPath $badState)) 'El rechazo de carpeta creó estado de instalación.'
    Assert-True ((Get-Content -LiteralPath $badResult -Encoding UTF8 -Raw).StartsWith('FAIL:')) 'El rechazo no devolvió un resultado claro.'

    Assert-Snapshot $realBefore 'La batería de pruebas'
    $success = $true
    Write-Output 'PASS: self-test de 20 recursos'
    Write-Output 'PASS: instalador y lanzador permanecen abiertos y se cierran por PID exacto'
    Write-Output 'PASS: instalación limpia autónoma y restauración'
    Write-Output 'PASS: actualización y restauración byte a byte de 20 archivos previos'
    Write-Output 'PASS: rechazo sin escrituras de carpeta no compatible'
    Write-Output "PASS: $($realPaths.Count) archivos protegidos vigilados sin cambios"
}
finally {
    if ($success -and $testRoot.StartsWith($safePrefix, [StringComparison]::OrdinalIgnoreCase) -and
        (Test-Path -LiteralPath $testRoot -PathType Container)) {
        [IO.Directory]::Delete($testRoot, $true)
    }
    elseif (-not $success) {
        Write-Warning "Se conserva la prueba fallida para diagnóstico: $testRoot"
    }
}
