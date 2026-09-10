[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$ManifestPath,
    [Parameter(Mandatory=$true)][string]$Bs1Exe,
    [Parameter(Mandatory=$true)][string]$Bs2Exe,
    [string]$FixtureBase = 'D:\BioShock12VR-IntegrationTests',
    [string]$PredecessorManifest = '',
    [switch]$BasicUI
)
$ErrorActionPreference = 'Stop'
$manifest = Get-Content -LiteralPath $ManifestPath -Raw | ConvertFrom-Json
$family = $manifest.testFamily
if ($family -notmatch '^[a-f0-9]{32}$' -or $manifest.kind -ne 'native-combined-msi') { throw 'Solo se prueban MSI conjuntos con identidad aislada.' }
$msi = Join-Path (Split-Path ([IO.Path]::GetFullPath($ManifestPath))) $manifest.installer
if ((Get-FileHash -LiteralPath $msi).Hash -ne $manifest.sha256) { throw 'MSI distinto del manifiesto.' }
$fixture = Join-Path ([IO.Path]::GetFullPath($FixtureBase)) ('BvrMsiTest-' + $family)
if (Test-Path -LiteralPath $fixture) { throw "La prueba necesita una carpeta nueva: $fixture" }
$checks = New-Object 'System.Collections.Generic.List[string]'
$logs = New-Object 'System.Collections.Generic.List[string]'
$utf8 = New-Object Text.UTF8Encoding($false)
Add-Type -TypeDefinition 'using System.Runtime.InteropServices; public static class CombinedTestState { [DllImport("msi.dll", CharSet=CharSet.Unicode, ExactSpelling=true)] public static extern int MsiQueryProductStateW(string product); [DllImport("msi.dll", CharSet=CharSet.Unicode, ExactSpelling=true)] public static extern int MsiQueryFeatureStateW(string product, string feature); }'
function Assert([bool]$Pass, [string]$Name) { if (-not $Pass) { throw "FALLA: $Name" }; $checks.Add($Name); Write-Output "PASS: $Name" }
function Hash([string]$Path) { if (Test-Path -LiteralPath $Path -PathType Leaf) { (Get-FileHash -LiteralPath $Path).Hash } else { '' } }
function Write-Fixture([string]$Path, [string]$Text) {
    $full = [IO.Path]::GetFullPath($Path)
    if (-not $full.StartsWith($fixture + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Escritura fuera del fixture.' }
    New-Item -ItemType Directory -Path (Split-Path $full) -Force | Out-Null
    [IO.File]::WriteAllText($full, $Text, $utf8)
}
$data = @{}
$real = @{}
$realRegistry = @{}
foreach ($id in @('bs1','bs2')) {
    $info = $manifest.games.$id
    $expectedRegistration = "HKCU:\Software\Beren5556\BioShockVRInstallerTests\$family\$id"
    if ($info.registration -ne $expectedRegistration -or (Get-ItemProperty -LiteralPath $expectedRegistration -ErrorAction SilentlyContinue).GameDirectory) { throw 'Registro de prueba incorrecto u ocupado.' }
    $exe = if ($id -eq 'bs1') { $Bs1Exe } else { $Bs2Exe }
    $expected = if ($id -eq 'bs1') { 'AEC21A0072CFDB15E4B525E2320C87256F14F16894F714272069270AD099A05B' } else { 'C2A31FB67B285C136203A4A6E739545177BE5753D972E6AEA0F44C65DDF22F8C' }
    if ((Hash $exe) -ne $expected) { throw 'Se requiere el ejecutable legítimo compatible; nunca se ejecutará.' }
    $game = Join-Path $fixture "$id\Game\Build\Final"
    $profileRelative = if ($id -eq 'bs1') { 'BioshockVR' } else { 'BioshockVR\bs2' }
    $iniRelative = if ($id -eq 'bs1') { 'BioshockHD\Bioshock\Bioshock.ini' } else { 'BioshockHD\Bioshock2\Bioshock2SP.ini' }
    $ini = Join-Path $fixture ('Roaming\' + $iniRelative)
    $profile = Join-Path $fixture ('Local\' + $profileRelative)
    $baseShortcut = if ($id -eq 'bs1') { 'BioShock VR DLSS-DLAA' } else { 'BioShock 2 VR DLSS-DLAA' }
    $data[$id] = @{ game=$game; profile=$profile; ini=$ini; registration=$info.registration
        original=(Join-Path $profile 'WindowsInstaller\Original.xml')
        shortcut=(Join-Path $fixture ("Desktop\" + $baseShortcut + " " + $manifest.version + ".lnk"))
        expected=$expected; exeName=[IO.Path]::GetFileName($exe) }
    New-Item -ItemType Directory -Path $game -Force | Out-Null
    Copy-Item -LiteralPath $exe -Destination (Join-Path $game ([IO.Path]::GetFileName($exe)))
    Write-Fixture (Join-Path $game 'xinput1_3.dll') ("pre-mod-original-" + $id)
    Write-Fixture (Join-Path $game 'unrelated.txt') ("do-not-touch-" + $id)
    Write-Fixture $ini "[Engine.RenderConfig]`r`nRealTimeReflection=True`r`nKeepMe=123`r`n[Other]`r`nUnchanged=True`r`n"
    foreach ($file in $info.files) { $path = Join-Path (Split-Path $exe) $file.path; $real[$path] = Hash $path }
    foreach ($path in @($exe, (Join-Path $env:LOCALAPPDATA ($profileRelative + '\dlss.ini')), (Join-Path $env:APPDATA $iniRelative))) { $real[$path] = Hash $path }
    $reg = if ($id -eq 'bs1') { 'HKCU:\Software\Beren5556\BioShockVRDLSSDLAA' } else { 'HKCU:\Software\Beren5556\BioShock2VRDLSSDLAA' }
    $realRegistry[$reg] = Get-ItemProperty -LiteralPath $reg -ErrorAction SilentlyContinue | Select-Object Version,GameDirectory,TestRoot | ConvertTo-Json -Compress
}
function Run-Msi([string]$Name, [string]$Extra='', [int]$Expected=0, [string]$Action='/i', [string]$Package=$msi) {
    $log = Join-Path $fixture ($Name + '.log'); $logs.Add($log)
    $ui = if ($BasicUI) { '/qb!' } else { '/qn' }
    $arguments = "$Action `"$Package`" $ui /norestart BVR_TESTROOT=`"$fixture`" BVR_BS1_GAMEPATH=`"$($data.bs1.game)`" BVR_BS2_GAMEPATH=`"$($data.bs2.game)`" $Extra /l*v `"$log`""
    $process = Start-Process -FilePath (Join-Path $env:WINDIR 'System32\msiexec.exe') -ArgumentList $arguments -WindowStyle Hidden -PassThru
    if (-not $process.WaitForExit(60000)) { throw "MSI aún activo: PID $($process.Id), $log. No se interrumpe a la fuerza." }
    if ($process.ExitCode -ne $Expected) { throw "$Name devolvió $($process.ExitCode), esperado $Expected. $log" }
    Assert $true "$Name (MSI $Expected)"
    if (Select-String -LiteralPath $log -Pattern 'Error 1926\.' | Where-Object Line -NotMatch 'ErrorDialog') { throw "Avisos 1926 en $log" }
    if (Select-String -LiteralPath $log -Pattern 'Note: 1: 1406|Error 1406\.') { throw "Error de registro/rollback 1406 en $log" }
}
function Check-Payload([string]$Id) {
    foreach ($file in $manifest.games.$Id.files) { if ((Hash (Join-Path $data[$Id].game $file.path)) -ne $file.sha256) { throw "$Id no coincide: $($file.path)" } }
    Assert $true "$($Id): 23 archivos idénticos a su payload de origen"
    Assert (Test-Path -LiteralPath $data[$Id].original) "$($Id): copia original conservada"
}
function Count-Transactions([string]$Id) {
    @(Get-ChildItem -LiteralPath (Join-Path $data[$Id].profile 'WindowsInstaller\Transactions') -Directory -ErrorAction SilentlyContinue).Count
}
function Check-Selection([string]$Mode) {
    $test = Join-Path (Split-Path $msi) 'work\Test-Selection.exe'
    & $test $msi $fixture $Mode
    if ($LASTEXITCODE -ne 0) { throw "Plan de selección MSI incorrecto: $Mode" }
    Assert $true ("Plan de interfaz nativa: " + $Mode)
}
function Check-Removed([string]$Id) {
    Assert ((Get-Content -LiteralPath (Join-Path $data[$Id].game 'xinput1_3.dll') -Raw) -eq ("pre-mod-original-" + $Id)) "$($Id): proxy pre-mod restaurado"
    Assert (-not (Test-Path -LiteralPath (Join-Path $data[$Id].game 'bioshockvr.dll'))) "$($Id): mod retirado"
    Assert (-not (Test-Path -LiteralPath $data[$Id].shortcut)) "$($Id): acceso retirado"
}
$failure = $null
try {
    if ($PredecessorManifest) {
        $previous = Get-Content -LiteralPath $PredecessorManifest -Raw | ConvertFrom-Json
        if ($previous.testFamily -ne $family -or $previous.testPredecessor -ne 'bs1') { throw 'Predecesor ajeno a esta prueba.' }
        $oldMsi = Join-Path (Split-Path $PredecessorManifest) $previous.installer
        if ((Hash $oldMsi) -ne $previous.sha256) { throw 'Predecesor modificado.' }
        Run-Msi '00-predecessor-bs1' 'ADDLOCAL=Game_bs1,Desktop_bs1' 0 '/i' $oldMsi
        $originalHash = Hash $data.bs1.original
        Add-Type -Path (Join-Path (Split-Path $msi) 'work\WixToolset.Dtf.WindowsInstaller.dll')
        Add-Type -Path (Join-Path (Split-Path $msi) 'work\LegacyFixtures.dll')
        $legacyManifest = [BioShockMsi.Actions]::CreateMsiMigrationFixture($fixture)
        $legacyFiles = @{}
        foreach ($line in (Get-Content -LiteralPath $legacyManifest)) {
            if ($line.StartsWith('File|')) {
                $parts = $line.Split('|')
                $name = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($parts[1]))
                $legacyFiles[$name] = $parts[5]
            }
        }
        Run-Msi '01-adopt-previous-and-install-bs2' 'ADDLOCAL=Game_bs2,Desktop_bs2'
        Check-Payload 'bs1'; Check-Payload 'bs2'
        Assert ((Hash $data.bs1.original) -eq $originalHash) 'Migración: conserva la copia previa al primer mod'
        Assert ((Get-ItemProperty -LiteralPath $data.bs1.registration).Version -eq $manifest.version) 'Migración: registro actualizado'
        Assert (-not (Test-Path -LiteralPath $legacyManifest)) 'Migración: manifiesto beta retirado a copia recuperable'
        Assert ([CombinedTestState]::MsiQueryProductStateW(('{' + $previous.productCode + '}')) -eq -1) 'Migración: MSI anterior ya no registrado'
        Run-Msi '02-uninstall-migration' '' 0 '/x'
        Check-Removed 'bs1'
        foreach ($name in $legacyFiles.Keys) {
            Assert ((Hash (Join-Path $data.bs2.game $name)) -eq $legacyFiles[$name]) "Migración BS2: original PRE-beta recuperado: $name"
        }
        Assert (-not (Test-Path -LiteralPath $data.bs2.shortcut)) 'Migración BS2: acceso nuevo retirado'
        Assert ((Get-Content -LiteralPath (Join-Path $fixture 'Desktop\BioShock 2 VR DLSS-DLAA.lnk') -Raw) -eq 'BEFORE BETA shortcut') 'Migración BS2: acceso PRE-beta recuperado'
        Assert ([CombinedTestState]::MsiQueryProductStateW(('{' + $manifest.productCode + '}')) -eq -1) 'Migración: MSI conjunto ya no registrado'
    } else {
        Check-Selection 'fresh'
        Run-Msi '01-install-bs1' 'ADDLOCAL=Game_bs1,Desktop_bs1'
        Check-Payload 'bs1'
        Assert (-not (Test-Path -LiteralPath (Join-Path $data.bs2.game 'bioshockvr.dll'))) 'Instalar BS1 no instala BS2'
        Assert (Test-Path -LiteralPath $data.bs1.shortcut) 'BS1: acceso apunta al lanzador en el juego'
        Write-Fixture (Join-Path $data.bs1.profile 'dlss.ini') "mode=DLAA`r`nuser=bs1"
        $bs1Transactions = Count-Transactions 'bs1'
        $bs1Original = Hash $data.bs1.original
        Check-Selection 'add'
        Run-Msi '02-add-bs2-rollback' 'ADDLOCAL=Game_bs2,Desktop_bs2 BVR_TESTFAIL=1 BVR_TESTFAILGAME=bs2' 1603
        Check-Payload 'bs1'; Check-Removed 'bs2'
        Assert ((Count-Transactions 'bs1') -eq $bs1Transactions) 'Fallo al añadir BS2 no toca BS1'
        Run-Msi '03-add-bs2' 'ADDLOCAL=Game_bs2,Desktop_bs2'
        Check-Payload 'bs1'; Check-Payload 'bs2'
        Assert ((Count-Transactions 'bs1') -eq $bs1Transactions) 'Añadir BS2 no reinstala BS1'
        Assert ((Hash $data.bs1.original) -eq $bs1Original) 'Añadir BS2 no reemplaza la copia de BS1'
        Check-Selection 'repair'
        Check-Selection 'remove'
        Write-Fixture (Join-Path $data.bs2.profile 'dlss.ini') "mode=DLSS`r`nuser=bs2"
        $originalBs2 = Hash $data.bs2.original
        $profileBs2 = Hash (Join-Path $data.bs2.profile 'dlss.ini')
        Write-Fixture (Join-Path $data.bs2.game 'bioshockvr.dll') 'deliberately-damaged-test-mod'
        Run-Msi '04-repair-bs2' 'REINSTALL=Game_bs2,Desktop_bs2 REINSTALLMODE=amus'
        Check-Payload 'bs2'
        Assert ((Count-Transactions 'bs1') -eq $bs1Transactions) 'Reparar BS2 no toca BS1'
        Assert ((Hash $data.bs2.original) -eq $originalBs2 -and (Hash (Join-Path $data.bs2.profile 'dlss.ini')) -eq $profileBs2) 'Reparar conserva originales y preferencias'
        Run-Msi '05-repair-rollback-after-retire' 'REINSTALL=Game_bs2,Desktop_bs2 REINSTALLMODE=amus BVR_TESTFAIL=after-retire BVR_TESTFAILGAME=bs2' 1603
        Check-Payload 'bs2'
        Run-Msi '06-remove-bs1-only' 'REMOVE=Game_bs1,Desktop_bs1'
        Check-Removed 'bs1'; Check-Payload 'bs2'
        Assert ((Get-Content -LiteralPath (Join-Path $data.bs1.profile 'dlss.ini') -Raw).Contains('user=bs1')) 'Desinstalar BS1 conserva sus preferencias'
        $bs2Transactions = Count-Transactions 'bs2'
        Run-Msi '07-add-bs1-back' 'ADDLOCAL=Game_bs1,Desktop_bs1'
        Check-Payload 'bs1'; Check-Payload 'bs2'
        Assert ((Count-Transactions 'bs2') -eq $bs2Transactions) 'Añadir BS1 no toca BS2'
        Run-Msi '08-disable-bs2-shortcut' 'REINSTALL=Game_bs2,Desktop_bs2 REINSTALLMODE=amus BVR_BS2_DESKTOPSHORTCUT=0'
        Assert (-not (Test-Path -LiteralPath $data.bs2.shortcut) -and (Test-Path -LiteralPath $data.bs1.shortcut)) 'Accesos de escritorio independientes'
        Run-Msi '09-enable-bs2-shortcut' 'REINSTALL=Game_bs2 REINSTALLMODE=amus BVR_BS2_DESKTOPSHORTCUT=1'
        Assert (Test-Path -LiteralPath $data.bs2.shortcut) 'Se puede volver a crear el acceso de BS2'
        Run-Msi '10-uninstall-both-rollback' 'BVR_TESTFAIL=1 BVR_TESTFAILGAME=bs2' 1603 '/x'
        Check-Payload 'bs1'; Check-Payload 'bs2'
        $product = '{' + $manifest.productCode + '}'
        Assert ([CombinedTestState]::MsiQueryProductStateW($product) -eq 5) 'Rollback: registro del producto restaurado'
        foreach ($id in @('bs1','bs2')) {
            Assert ([CombinedTestState]::MsiQueryFeatureStateW($product, ('Game_' + $id)) -eq 3) "Rollback: característica $id restaurada"
        }
        Run-Msi '11-uninstall-both' '' 0 '/x'
        Check-Removed 'bs1'; Check-Removed 'bs2'
        Assert ([CombinedTestState]::MsiQueryProductStateW($product) -eq -1) 'Desinstalación: producto ya no registrado'
    }
} catch { $failure = $_ }
finally {
    foreach ($id in @('bs1','bs2')) {
        if ((Get-ItemProperty -LiteralPath $data[$id].registration -ErrorAction SilentlyContinue).GameDirectory) {
            try { Run-Msi 'cleanup-suite' '' 0 '/x' } catch { if (-not $failure) { $failure = $_ } }
            break
        }
    }
    try {
        foreach ($path in $real.Keys) { if ((Hash $path) -ne $real[$path]) { throw "Archivo real modificado: $path" } }
        foreach ($reg in $realRegistry.Keys) {
            $after = Get-ItemProperty -LiteralPath $reg -ErrorAction SilentlyContinue | Select-Object Version,GameDirectory,TestRoot | ConvertTo-Json -Compress
            if ($after -ne $realRegistry[$reg]) { throw "Registro real modificado: $reg" }
        }
        foreach ($id in @('bs1','bs2')) {
            Assert ((Hash (Join-Path $data[$id].game $data[$id].exeName)) -eq $data[$id].expected) "$($id): ejecutable de juego intacto"
            Assert ((Get-Content -LiteralPath (Join-Path $data[$id].game 'unrelated.txt') -Raw) -eq ("do-not-touch-" + $id)) "$($id): archivos ajenos intactos"
        }
        Assert $true 'Payloads, perfiles y registros de los juegos reales sin cambios'
    } catch { if (-not $failure) { $failure = $_ } }
    $report = [ordered]@{ passed=($null -eq $failure); error=("$failure"); fixture=$fixture; installerSha256=$manifest.sha256; checks=$checks.ToArray(); logs=$logs.ToArray() }
    [IO.File]::WriteAllText((Join-Path (Split-Path $ManifestPath) 'test-result.json'), ($report | ConvertTo-Json -Depth 6), $utf8)
}
if ($failure) { throw $failure }
Write-Output "MSI conjunto: $($checks.Count) comprobaciones, $($logs.Count) operaciones."
