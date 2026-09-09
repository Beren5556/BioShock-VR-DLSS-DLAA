[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$GameExeSource,
    [string]$Version = '0.2.11',
    [string]$UpgradeMsi = '',
    [string]$ManifestPath = '',
    [string]$UpgradeManifestPath = '',
    [string]$FixtureBase = '',
    [switch]$TestShortcutChoice,
    [switch]$ReproducePackageCollision,
    [switch]$AllowRollbackSecurityWarnings
)
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$release = Join-Path $repo ('artifacts\stable-' + $Version)
if ($ManifestPath) { $release = Split-Path -Parent ([IO.Path]::GetFullPath($ManifestPath)) }
else { $ManifestPath = Join-Path $release "manifest-$Version.json" }
$manifest = Get-Content -LiteralPath $ManifestPath -Raw | ConvertFrom-Json
$Version = $manifest.version
$msi = Join-Path $release $manifest.installer
$realRegistration = 'HKCU:\Software\Beren5556\BioShockVRDLSSDLAA'
$realRegisteredBefore = Get-ItemProperty -LiteralPath $realRegistration -ErrorAction SilentlyContinue | Select-Object Version,GameDirectory,TestRoot | ConvertTo-Json -Compress
$registration = $realRegistration
if ($manifest.testFamily) {
    if ($manifest.testFamily -notmatch '^[a-f0-9]{32}$' -or $manifest.registration -ne ('HKCU:\Software\Beren5556\BioShockVRInstallerTests\' + $manifest.testFamily)) { throw 'Identidad de prueba inválida' }
    $registration = $manifest.registration
}
$registered = Get-ItemProperty -LiteralPath $registration -ErrorAction SilentlyContinue
if ($registered.GameDirectory) { throw 'Ya hay un MSI registrado con esta identidad. No se ejecutan pruebas sobre él.' }
if ((Get-FileHash -LiteralPath $msi).Hash -ne $manifest.sha256) { throw 'MSI distinto de su manifiesto' }
$upgradeManifest = $null
if ($UpgradeManifestPath) {
    $upgradeManifest = Get-Content -LiteralPath $UpgradeManifestPath -Raw | ConvertFrom-Json
    if (-not $manifest.testFamily -or $upgradeManifest.testFamily -ne $manifest.testFamily -or $upgradeManifest.registration -ne $registration) { throw 'La actualización no pertenece a la misma familia aislada.' }
    $UpgradeMsi = Join-Path (Split-Path -Parent ([IO.Path]::GetFullPath($UpgradeManifestPath))) $upgradeManifest.installer
    if ((Get-FileHash -LiteralPath $UpgradeMsi).Hash -ne $upgradeManifest.sha256) { throw 'MSI de actualización distinto de su manifiesto' }
}
$expectedExe = 'AEC21A0072CFDB15E4B525E2320C87256F14F16894F714272069270AD099A05B'
if ((Get-FileHash -LiteralPath $GameExeSource -Algorithm SHA256).Hash -ne $expectedExe) {
    throw 'La prueba necesita una copia local legítima del ejecutable compatible.'
}
if (-not $FixtureBase) { $FixtureBase = [IO.Path]::GetTempPath() }
$battery = Join-Path ([IO.Path]::GetFullPath($FixtureBase)) ('BvrMsiBattery-' + [Guid]::NewGuid().ToString('N'))
$fixture = Join-Path $battery ('BvrMsiTest-' + $(if ($manifest.testFamily) { $manifest.testFamily } else { [Guid]::NewGuid().ToString('N') }))
$game = Join-Path $fixture 'Game\Build\Final'
$ini = Join-Path $fixture 'Roaming\BioshockHD\Bioshock\Bioshock.ini'
$dlss = Join-Path $fixture 'Local\BioshockVR\dlss.ini'
$shortcutName = if ($manifest.desktopShortcut) { $manifest.desktopShortcut } else { 'BioShock VR DLSS-DLAA.lnk' }
$shortcut = Join-Path $fixture ('Desktop\' + $shortcutName)
$legacyShortcut = Join-Path $fixture 'Desktop\BioShock VR DLSS-DLAA.lnk'
$original = Join-Path $fixture 'Local\BioshockVR\WindowsInstaller\Original.xml'
$checks = New-Object 'System.Collections.Generic.List[string]'
$logs = New-Object 'System.Collections.Generic.List[string]'
$rollbackSecurityWarnings = 0
$utf8 = New-Object Text.UTF8Encoding($false)
foreach ($dir in @($game, (Split-Path $ini), (Split-Path $dlss), (Split-Path $shortcut))) {
    New-Item -ItemType Directory -Path $dir -Force | Out-Null
}
Copy-Item -LiteralPath $GameExeSource -Destination (Join-Path $game 'BioshockHD.exe')
[IO.File]::WriteAllText((Join-Path $game 'unrelated-test.txt'), 'Never change this unrelated file.', $utf8)
$graphics = @('HighDetailShaders','Shadows','RealTimeReflection','PostProcessing','UseRippleSystem',
    'UseHighDetailSoftParticles','UseDistortion','UseHighDetailPostProcEffects')
$iniText = "[WinDrv.WindowsClient]`r`nWindowedViewportX=1920`r`nWindowedViewportY=1080`r`nFullscreenViewportX=1920`r`nFullscreenViewportY=1080`r`n[Engine.RenderConfig]`r`n"
foreach ($key in $graphics) { $iniText += "$key=False`r`n" }
$iniText += "FluidSurfaceDetail=Low`r`nKeepMe=123`r`n[Other.Section]`r`nUntouched=True`r`n"
[IO.File]::WriteAllText($ini, $iniText, $utf8)
$realBefore = @{}
foreach ($path in @($GameExeSource, (Join-Path (Split-Path $GameExeSource) 'bioshockvr.dll'),
    (Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'BioshockVR\dlss.ini'),
    (Join-Path ([Environment]::GetFolderPath('ApplicationData')) 'BioshockHD\Bioshock\Bioshock.ini'),
    (Join-Path ([Environment]::GetFolderPath('DesktopDirectory')) 'BioShock VR DLSS-DLAA.lnk'))) {
    if (Test-Path -LiteralPath $path) { $realBefore[$path] = (Get-FileHash -LiteralPath $path).Hash }
}
function Assert([bool]$Pass, [string]$Name) {
    if (-not $Pass) { throw "FALLA: $Name" }
    $checks.Add($Name)
    Write-Output "PASS: $Name"
}
function Hash([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { return '' }
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash
}
function Run-Msi([string]$Action, [string]$Name, [int]$Expected=0, [string]$Extra='', [string]$Package=$msi) {
    $log = Join-Path $fixture ($Name + '.log')
    $logs.Add($log)
    $arguments = "$Action `"$Package`" /qn /norestart BVR_GAMEPATH=`"$game`" BVR_TESTROOT=`"$fixture`" $Extra /l*v `"$log`""
    $process = Start-Process -FilePath (Join-Path $env:WINDIR 'System32\msiexec.exe') -ArgumentList $arguments -WindowStyle Hidden -PassThru
    if (-not $process.WaitForExit(60000)) { throw "MSI sigue ejecutándose; no se termina a la fuerza. PID $($process.Id); log $log" }
    if ($process.ExitCode -ne $Expected) {
        Get-Content -LiteralPath $log -Tail 35 | Write-Output
        throw "$Name terminó con $($process.ExitCode), se esperaba $Expected. Log: $log"
    }
    Assert $true "$Name (código $Expected)"
    $securityErrors = @(Select-String -LiteralPath $log -Pattern 'Error 1926\.' | Where-Object Line -NotMatch 'ErrorDialog').Count
    $script:rollbackSecurityWarnings += $securityErrors
    # A comparison may explicitly allow the OLD package's known warning, never
    # a warning from the corrected upgrade package under evaluation.
    if ($securityErrors -and (-not $AllowRollbackSecurityWarnings -or ($UpgradeMsi -and $Package -eq $UpgradeMsi))) { throw "$Name produjo $securityErrors avisos de seguridad 1926, aunque devolviera éxito. Log: $log" }
}
function Check-Payload($ExpectedManifest = $manifest) {
    foreach ($file in $ExpectedManifest.files) {
        if ((Hash (Join-Path $game $file.path)) -ne $file.sha256) { throw "Hash distinto: $($file.path)" }
    }
    Assert $true 'Todos los archivos instalados coinciden con el manifiesto'
}
function Check-Preserved {
    Assert ((Hash (Join-Path $game 'BioshockHD.exe')) -eq $expectedExe) 'Ejecutable del juego intacto'
    Assert ((Get-Content (Join-Path $game 'unrelated-test.txt') -Raw) -eq 'Never change this unrelated file.') 'Archivos ajenos intactos'
    foreach ($path in $realBefore.Keys) {
        if ((Hash $path) -ne $realBefore[$path]) { throw "Cambió un archivo real: $path" }
    }
    Assert $true 'Instalación e INI reales sin modificar'
    $realRegisteredAfter = Get-ItemProperty -LiteralPath $realRegistration -ErrorAction SilentlyContinue | Select-Object Version,GameDirectory,TestRoot | ConvertTo-Json -Compress
    if ($manifest.testFamily -and $realRegisteredAfter -ne $realRegisteredBefore) { throw 'Cambió el registro del producto real.' }
}
$failure = $null
try {
    Run-Msi '/i' '01-install-fresh'
    Check-Payload
    Assert (Test-Path -LiteralPath $shortcut) 'Acceso directo solo en el escritorio aislado'
    Assert (Test-Path -LiteralPath $original) 'Copia original recuperable creada'
    $newIni = Get-Content -LiteralPath $ini -Raw
    foreach ($key in $graphics) {
        $expected = if ($key -in @('RealTimeReflection','UseRippleSystem')) { 'False' } else { 'True' }
        Assert ($newIni -match "(?m)^$key=$expected\r?$") "Predeterminado: $key=$expected"
    }
    Assert ($newIni.Contains('FluidSurfaceDetail=High') -and $newIni.Contains('KeepMe=123') -and $newIni.Contains('WindowedViewportY=1080')) 'Fluidos Alto sin cambiar ajustes ajenos ni resolución'
    $launched = @(Get-Process -Name 'Lanzador BioShock VR DLSS-DLAA','BioshockHD' -ErrorAction SilentlyContinue)
    Assert ($launched.Count -eq 0) 'No inicia el lanzador ni el juego al terminar'
    if ($ReproducePackageCollision) {
        # Reproduce a changed PackageCode under an already-installed ProductCode.
        # Only edit a copy of the ISOLATED package, never a delivered MSI.
        if (-not $manifest.testFamily) { throw 'La reproducción 1638 requiere un paquete de prueba aislado.' }
        $collisionMsi = Join-Path $fixture 'same-version-different-package.msi'
        Copy-Item -LiteralPath $msi -Destination $collisionMsi
        [void][Reflection.Assembly]::LoadFrom((Join-Path $repo 'artifacts\msi-tools\wixtoolset.dtf.windowsinstaller.6.0.2\lib\net20\WixToolset.Dtf.WindowsInstaller.dll'))
        $summary = New-Object WixToolset.Dtf.WindowsInstaller.SummaryInfo($collisionMsi, $true)
        try {
            $summary.RevisionNumber = '{' + [Guid]::NewGuid().ToString().ToUpperInvariant() + '}'
            $summary.Persist()
        } finally { $summary.Dispose() }
        Run-Msi '/i' '01b-reproduce-1638-old-package' 1638 '' $collisionMsi
        Check-Payload
        Check-Preserved
    }
    $customIni = $newIni.Replace('RealTimeReflection=True', 'RealTimeReflection=False')
    [IO.File]::WriteAllText($ini, $customIni, $utf8)
    [IO.File]::WriteAllText($dlss, "[DLSS]`r`nmode=dlaa`r`n# user preference`r`n", $utf8)
    $iniHash = Hash $ini
    $dlssHash = Hash $dlss
    [IO.File]::WriteAllText((Join-Path $game 'host64\nvngx_dlss.dll'), 'TEST-ONLY alternative runtime', $utf8)
    Run-Msi '/i' '02-repair' 0 'REINSTALL=ALL REINSTALLMODE=amus'
    Check-Payload
    if ([version]$Version -ge [version]'0.2.11') { Assert (Test-Path -LiteralPath $shortcut) 'Reparar reinstala también el acceso seleccionado' }
    Assert ((Hash $ini) -eq $iniHash -and (Hash $dlss) -eq $dlssHash) 'Reparar conserva ambas configuraciones personales'
    Run-Msi '/fa' '02b-standard-windows-repair'
    Check-Payload
    if ([version]$Version -ge [version]'0.2.11') { Assert (Test-Path -LiteralPath $shortcut) 'Reparación estándar conserva el acceso seleccionado' }
    Assert ((Hash $ini) -eq $iniHash -and (Hash $dlss) -eq $dlssHash) 'Reparación estándar también conserva ajustes y aislamiento'
    Check-Preserved
    Run-Msi '/x' '03-uninstall-fresh'
    foreach ($file in $manifest.files) { if (Test-Path -LiteralPath (Join-Path $game $file.path)) { throw "Queda archivo del paquete: $($file.path)" } }
    Assert (-not (Test-Path -LiteralPath $shortcut)) 'Retira el acceso directo propio'
    Assert ((Hash $ini) -eq $iniHash -and (Hash $dlss) -eq $dlssHash) 'Desinstalar conserva preferencias'
    Check-Preserved

    if ($TestShortcutChoice) {
        # Full UI unchecking clears the property after DetectGame ran. The
        # initialized marker must preserve that empty value in execute mode.
        Run-Msi '/i' '03a-install-unchecked' 0 'BVR_SHORTCUTINITIALIZED=1'
        Check-Payload
        Assert (-not (Test-Path -LiteralPath $shortcut)) 'Casilla desmarcada no crea acceso directo'
        Assert ((Get-ItemProperty -LiteralPath $registration).DesktopShortcut -eq '0') 'Recuerda la elección sin acceso'
        Run-Msi '/fa' '03b-repair-unchecked'
        Check-Payload
        Assert (-not (Test-Path -LiteralPath $shortcut)) 'Reparar respeta la elección sin acceso'
        Run-Msi '/i' '03c-failed-enable-shortcut' 1603 'REINSTALL=ALL REINSTALLMODE=amus BVR_DESKTOPSHORTCUT=1 BVR_TESTFAIL=after-retire'
        Assert (-not (Test-Path -LiteralPath $shortcut)) 'Fallo al activar acceso no deja uno nuevo'
        Assert ((Get-ItemProperty -LiteralPath $registration).DesktopShortcut -eq '0') 'Fallo conserva la preferencia anterior sin acceso'
        Run-Msi '/i' '03d-enable-shortcut' 0 'REINSTALL=ALL REINSTALLMODE=amus BVR_DESKTOPSHORTCUT=1'
        Assert (Test-Path -LiteralPath $shortcut) 'Activar la opción crea el acceso versionado'
        $shortcutHash = Hash $shortcut
        Run-Msi '/i' '03e-failed-disable-shortcut' 1603 'REINSTALL=ALL REINSTALLMODE=amus BVR_DESKTOPSHORTCUT=0 BVR_TESTFAIL=after-retire'
        Assert ((Hash $shortcut) -eq $shortcutHash) 'Fallo al desactivar recupera el acceso byte a byte'
        Assert ((Get-ItemProperty -LiteralPath $registration).DesktopShortcut -eq '1') 'Fallo conserva la preferencia anterior con acceso'
        Run-Msi '/i' '03f-disable-shortcut' 0 'REINSTALL=ALL REINSTALLMODE=amus BVR_DESKTOPSHORTCUT=0'
        Check-Payload
        Assert (-not (Test-Path -LiteralPath $shortcut)) 'Desactivar la opción retira solo el acceso propio'
        Run-Msi '/x' '03g-uninstall-unchecked'
        Assert (-not (Test-Path -LiteralPath $shortcut)) 'Desinstalar no inventa un acceso desmarcado'
        Assert ((Hash $ini) -eq $iniHash -and (Hash $dlss) -eq $dlssHash) 'Cambiar el acceso conserva las configuraciones'
        Check-Preserved
    }

    # These are deliberate fixture files, not real mod binaries.
    $prior = @{}
    foreach ($relative in @('bioshockvr.dll','dxgi.dll','host64\nvngx_dlss.dll')) {
        $path = Join-Path $game $relative
        New-Item -ItemType Directory -Path (Split-Path -Parent $path) -Force | Out-Null
        [IO.File]::WriteAllText($path, ('Previous fixture file: ' + $relative), $utf8)
        $prior[$path] = Hash $path
    }
    [IO.File]::WriteAllText($shortcut, 'Previous fixture shortcut', $utf8)
    $prior[$shortcut] = Hash $shortcut
    if ($legacyShortcut -ne $shortcut) {
        [IO.File]::WriteAllText($legacyShortcut, 'Previous unversioned fixture shortcut', $utf8)
        $prior[$legacyShortcut] = Hash $legacyShortcut
    }
    if ([version]$Version -ge [version]'0.2.10') {
        Run-Msi '/i' '04a-rollback-after-retire' 1603 'BVR_TESTFAIL=after-retire'
        foreach ($path in $prior.Keys) { Assert ((Hash $path) -eq $prior[$path]) ("Recuperación antes de InstallFiles: " + [IO.Path]::GetFileName($path)) }
        Assert (-not (Test-Path -LiteralPath $original)) 'Fallo tras retirada no deja un original sin confirmar'
        Check-Preserved
    }
    Run-Msi '/i' '04-controlled-rollback' 1603 'BVR_TESTFAIL=1'
    foreach ($path in $prior.Keys) { Assert ((Hash $path) -eq $prior[$path]) ("Rollback exacto: " + [IO.Path]::GetFileName($path)) }
    Assert (-not (Test-Path -LiteralPath $original)) 'Rollback retira el manifiesto nuevo no confirmado'
    Assert ((Hash $ini) -eq $iniHash -and (Hash $dlss) -eq $dlssHash) 'Rollback conserva las configuraciones'
    Run-Msi '/i' '05-install-over-existing'
    Check-Payload
    if ($legacyShortcut -ne $shortcut) {
        Assert (-not (Test-Path -LiteralPath $legacyShortcut)) 'Retira el acceso sin versión conservando su copia original'
    }
    Assert ((Hash $ini) -eq $iniHash -and (Hash $dlss) -eq $dlssHash) 'Actualizar desde mod previo conserva ajustes'
    if ($UpgradeMsi) {
        if ($upgradeManifest -and [version]$upgradeManifest.version -ge [version]'0.2.10') {
            Run-Msi '/i' '05b-failed-upgrade-after-retire' 1603 'BVR_TESTFAIL=after-retire' $UpgradeMsi
            Check-Payload
            Assert (Test-Path -LiteralPath $shortcut) 'Una actualización fallida recupera el acceso de la versión anterior'
            $failedUpgradeRegistration = Get-ItemProperty -LiteralPath $registration
            Assert ($failedUpgradeRegistration.Version -eq $Version) 'Una actualización fallida conserva el producto anterior registrado'
        }
        Run-Msi '/i' '06-major-upgrade' 0 '' $UpgradeMsi
        if ($upgradeManifest) { Check-Payload $upgradeManifest } else { Check-Payload }
        if ($upgradeManifest) {
            Assert (Test-Path -LiteralPath (Join-Path $fixture ('Desktop\' + $upgradeManifest.desktopShortcut))) 'Acceso directo de la versión actualizada creado'
            Assert (-not (Test-Path -LiteralPath $shortcut)) 'El acceso de la versión anterior se retira al actualizar'
            $upgradedRegistration = Get-ItemProperty -LiteralPath $registration
            Assert ($upgradedRegistration.Version -eq $upgradeManifest.version) 'Registro aislado pasa a la versión nueva'
        }
        Assert ((Hash $ini) -eq $iniHash -and (Hash $dlss) -eq $dlssHash) 'Actualización MSI conserva ajustes'
        if ($upgradeManifest -and [version]$upgradeManifest.version -ge [version]'0.2.11') {
            Run-Msi '/i' '06b-reinstall-same-upgraded-package' 0 'REINSTALL=ALL REINSTALLMODE=amus' $UpgradeMsi
            Check-Payload $upgradeManifest
            Assert (Test-Path -LiteralPath (Join-Path $fixture ('Desktop\' + $upgradeManifest.desktopShortcut))) 'Reinstalar el mismo MSI conserva el acceso de la versión actual'
            Assert ((Hash $ini) -eq $iniHash -and (Hash $dlss) -eq $dlssHash) 'Reinstalar después de actualizar conserva los INI'
        }
        Run-Msi '/x' '07-uninstall-upgraded' 0 '' $UpgradeMsi
        if ($upgradeManifest) { Assert (-not (Test-Path -LiteralPath (Join-Path $fixture ('Desktop\' + $upgradeManifest.desktopShortcut)))) 'Desinstalar retira el acceso actualizado' }
    } else {
        Run-Msi '/x' '06-uninstall-existing'
    }
    foreach ($path in $prior.Keys) { Assert ((Hash $path) -eq $prior[$path]) ("Desinstalar restaura el original: " + [IO.Path]::GetFileName($path)) }
    Assert (-not (Test-Path -LiteralPath $original)) 'Archiva el manifiesto original tras desinstalar'
    Check-Preserved
    $registrationAfter = Get-ItemProperty -LiteralPath $registration -ErrorAction SilentlyContinue
    Assert (-not $registrationAfter.GameDirectory) 'No queda un producto MSI de prueba registrado'
    Run-Msi '/i' '08-invalid-folder' 1603 ("BVR_GAMEPATH=`"" + (Join-Path $fixture 'MissingGame') + '"')
    Check-Preserved
}
catch { $failure = $_.ToString(); Write-Output $failure }
finally {
    $remaining = Get-ItemProperty -LiteralPath $registration -ErrorAction SilentlyContinue
    if ($remaining.GameDirectory -and $remaining.GameDirectory.TrimEnd('\') -eq $game) {
        try {
            $cleanup = if ($remaining.Version -ne $Version -and $UpgradeMsi) { $UpgradeMsi } else { $msi }
            Run-Msi '/x' 'cleanup' 0 '' $cleanup
        } catch { $failure += "`nNo se completó la limpieza del MSI de prueba: $_" }
    }
    $report = [ordered]@{ date = (Get-Date).ToString('o'); version=$Version; fixture=$fixture; passed=@($checks); failure=$failure; rollbackSecurityWarnings=$rollbackSecurityWarnings; logs=@($logs) }
    [IO.File]::WriteAllText((Join-Path $fixture 'result.json'), ($report | ConvertTo-Json -Depth 4), $utf8)
    [IO.File]::WriteAllText((Join-Path $release 'msi-test-result.json'), ($report | ConvertTo-Json -Depth 4), $utf8)
    Write-Output "Informe: $fixture\result.json"
}
if ($failure) { throw $failure }
