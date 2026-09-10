[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$ManifestPath,
    [Parameter(Mandatory=$true)][string]$Bs1Exe,
    [Parameter(Mandatory=$true)][string]$Bs2Exe,
    [string]$FixtureBase = 'D:\BioShock12VR-IntegrationTests',
    [string]$PredecessorManifest = '',
    [string]$PreviousSingleGameManifest = '',
    [ValidateSet('standard','beta-new','beta-original')][string]$BetaShortcut = 'standard'
)
$ErrorActionPreference = 'Stop'
$testIdentity=[Security.Principal.WindowsIdentity]::GetCurrent()
$testPrincipal=New-Object Security.Principal.WindowsPrincipal($testIdentity)
if(-not $testPrincipal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'Las pruebas MSI silenciosas y su rollback necesitan una consola elevada del mismo usuario. No se modifica ningún archivo.'
}
$manifest = Get-Content -LiteralPath $ManifestPath -Raw | ConvertFrom-Json
$family = $manifest.testFamily
if ($family -notmatch '^[a-f0-9]{32}$' -or $manifest.kind -ne 'native-single-game-msi') { throw 'Solo se prueban MSI conjuntos con identidad aislada.' }
$msi = Join-Path (Split-Path ([IO.Path]::GetFullPath($ManifestPath))) $manifest.installer
if ((Get-FileHash -LiteralPath $msi).Hash -ne $manifest.sha256) { throw 'MSI distinto del manifiesto.' }
$fixture = Join-Path ([IO.Path]::GetFullPath($FixtureBase)) ('BvrMsiTest-' + $family)
if (Test-Path -LiteralPath $fixture) { throw "La prueba necesita una carpeta nueva: $fixture" }
$checks = New-Object 'System.Collections.Generic.List[string]'
$logs = New-Object 'System.Collections.Generic.List[string]'
$utf8 = New-Object Text.UTF8Encoding($false)
if(-not ('CombinedTestState' -as [type])) {
    Add-Type -TypeDefinition 'using System.Runtime.InteropServices; public static class CombinedTestState { [DllImport("msi.dll", CharSet=CharSet.Unicode, ExactSpelling=true)] public static extern int MsiQueryProductStateW(string product); [DllImport("msi.dll", CharSet=CharSet.Unicode, ExactSpelling=true)] public static extern int MsiQueryFeatureStateW(string product, string feature); }'
}
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
function Run-Msi([string]$Id,[string]$Name,[string]$Extra='',[int]$Expected=0,[string]$Operation='install',[object]$Package=$manifest) {
    $log=Join-Path $fixture ($Name+'.log');$logs.Add($log)
    $product='{'+$Package.games.$Id.productCode+'}'
    $packageMsi=if($Package -eq $manifest){$msi}else{$oldInstanceMsi}
    if ($Operation -eq 'remove') { $arguments='/x '+$product }
    elseif ([CombinedTestState]::MsiQueryProductStateW($product) -eq 5) { $arguments='/i "'+$packageMsi+'" /n '+$product }
    else { $arguments='/i "'+$packageMsi+'" TRANSFORMS=:'+$Id+' MSINEWINSTANCE=1' }
    $arguments += ' /qn /norestart BVR_TESTROOT="'+$fixture+'" BVR_'+$Id.ToUpperInvariant()+'_GAMEPATH="'+$data[$Id].game+'" '+$Extra+' /l*v "'+$log+'"'
    $process=Start-Process -FilePath (Join-Path $env:WINDIR 'System32\msiexec.exe') -ArgumentList $arguments -WindowStyle Hidden -PassThru
    if(-not $process.WaitForExit(60000)) { throw "MSI still running: $($process.Id), $log" }
    Assert ($process.ExitCode -eq $Expected) "$Name (MSI $($process.ExitCode), expected $Expected)"
    # The generic End(Checksum=0) / Return: 5 marker also occurs in successful
    # historical rollback fixtures. Check concrete native failures plus actual
    # product state and file hashes below, not that marker by itself.
    if(Select-String -LiteralPath $log -Pattern 'Error in rollback skipped\.\s+Return: 3|Note: 1: (?:1401|1406)|(?:Error|Información) (?:1401|1406|1926)\.') { throw "Native registry/rollback error: $log" }
}
function Check-Payload([string]$Id,[object]$Package=$manifest) {
    foreach ($file in $Package.games.$Id.files) { if ((Hash (Join-Path $data[$Id].game $file.path)) -ne $file.sha256) { throw "$Id no coincide: $($file.path)" } }
    Assert $true "$($Id): 23 archivos idénticos a su payload de origen"
    Assert (Test-Path -LiteralPath $data[$Id].original) "$($Id): copia original conservada"
}
function Count-Transactions([string]$Id) {
    @(Get-ChildItem -LiteralPath (Join-Path $data[$Id].profile 'WindowsInstaller\Transactions') -Directory -ErrorAction SilentlyContinue).Count
}
function Check-Selection() {
    $installedProducts=@()
    foreach($id in @('bs1','bs2')){
        $product='{'+$manifest.games.$id.productCode+'}'
        if([CombinedTestState]::MsiQueryProductStateW($product) -eq 5){$installedProducts+=($id+'|'+$product)}
    }
    $selectionOutput=& (Join-Path (Split-Path $msi) 'work\Test-Selection.exe') $msi $fixture @installedProducts 2>&1
    $selectionExit=$LASTEXITCODE
    [IO.File]::AppendAllText((Join-Path $fixture 'selection-plans.log'),($selectionOutput|Out-String),$utf8)
    if($selectionExit -ne 0){throw ('Selector dispatch test failed: '+($selectionOutput|Out-String))}
    Assert $true 'Selector opens only the chosen native game instance'
}
function Check-Removed([string]$Id) {
    Assert ((Get-Content -LiteralPath (Join-Path $data[$Id].game 'xinput1_3.dll') -Raw) -eq ("pre-mod-original-" + $Id)) "$($Id): proxy pre-mod restaurado"
    Assert (-not (Test-Path -LiteralPath (Join-Path $data[$Id].game 'bioshockvr.dll'))) "$($Id): mod retirado"
    Assert (-not (Test-Path -LiteralPath $data[$Id].shortcut)) "$($Id): acceso retirado"
}
function Run-Previous([string]$Name,[string]$Extra='',[string]$Operation='install') {
    $log=Join-Path $fixture ($Name+'.log');$logs.Add($log)
    $arguments=if($Operation -eq 'remove'){'/x {'+$previous.productCode+'}'}else{'/i "'+$oldMsi+'"'}
    $arguments+=' /qn /norestart BVR_TESTROOT="'+$fixture+'" BVR_BS1_GAMEPATH="'+$data.bs1.game+'" BVR_BS2_GAMEPATH="'+$data.bs2.game+'" '+$Extra+' /l*v "'+$log+'"'
    $p=Start-Process msiexec.exe -ArgumentList $arguments -WindowStyle Hidden -PassThru
    if(-not $p.WaitForExit(60000)){throw "Previous MSI still running: $log"}
    Assert ($p.ExitCode -eq 0) "$Name (MSI $($p.ExitCode))"
}
$failure=$null
try {
    if($PreviousSingleGameManifest) {
        if($PredecessorManifest){throw 'Choose only one predecessor type.'}
        $previousSingle=Get-Content -LiteralPath $PreviousSingleGameManifest -Raw | ConvertFrom-Json
        if($previousSingle.testFamily -ne $family -or $previousSingle.kind -ne 'native-single-game-msi' -or
            [version]$previousSingle.version -ge [version]$manifest.version){throw 'Invalid isolated single-game predecessor.'}
        $oldInstanceMsi=Join-Path (Split-Path ([IO.Path]::GetFullPath($PreviousSingleGameManifest))) $previousSingle.installer
        if((Hash $oldInstanceMsi) -ne $previousSingle.sha256){throw 'Predecessor MSI hash mismatch.'}
        foreach($id in @('bs1','bs2')) {
            if($previousSingle.games.$id.registration -ne $manifest.games.$id.registration -or
                $previousSingle.games.$id.legacyUpgradeCode -ne $manifest.games.$id.legacyUpgradeCode -or
                $previousSingle.games.$id.productCode -eq $manifest.games.$id.productCode){throw 'Invalid isolated upgrade identities.'}
            if([CombinedTestState]::MsiQueryProductStateW(('{'+$previousSingle.games.$id.productCode+'}')) -eq 5){throw 'Predecessor already installed.'}
        }
        # Reverse the standard test's installation order; both old payloads are
        # genuinely different from the final corrected binaries and profiles.
        Run-Msi 'bs2' 'upgrade-00-install-old-bs2' '' 0 'install' $previousSingle
        Run-Msi 'bs1' 'upgrade-01-install-old-bs1' '' 0 'install' $previousSingle
        foreach($id in @('bs1','bs2')) {
            Check-Payload $id $previousSingle
            Write-Fixture (Join-Path $data[$id].profile 'dlss.ini') ("[user]`r`nmode=DLAA`r`ngame="+$id)
        }
        $oldSnapshot=Get-Content -LiteralPath $data.bs1.original -Raw
        Write-Fixture $data.bs1.original ($oldSnapshot.Replace('host64\','host64/'))
        foreach($id in @('bs1','bs2')) {
            $other=if($id -eq 'bs1'){'bs2'}else{'bs1'}
            $originalHash=Hash $data[$id].original
            $prefsHash=Hash (Join-Path $data[$id].profile 'dlss.ini')
            $otherFiles=@{}
            foreach($file in Get-ChildItem -LiteralPath $data[$other].game -File -Recurse){$otherFiles[$file.FullName]=Hash $file.FullName}
            $otherOriginal=Hash $data[$other].original
            $otherPrefs=Hash (Join-Path $data[$other].profile 'dlss.ini')
            $otherTransactions=Count-Transactions $other
            foreach($failurePoint in @('after-retire','1')) {
                Run-Msi $id ("upgrade-$id-rollback-$failurePoint") ("BVR_TESTFAIL=$failurePoint BVR_TESTFAILGAME=$id") 1603
                Assert ([CombinedTestState]::MsiQueryProductStateW(('{'+$previousSingle.games.$id.productCode+'}')) -eq 5) "$id failed upgrade preserves old product"
                Assert ([CombinedTestState]::MsiQueryProductStateW(('{'+$manifest.games.$id.productCode+'}')) -eq -1) "$id failed upgrade does not register the new product"
                Check-Payload $id $previousSingle
            }
            Run-Msi $id ("upgrade-$id-success")
            Check-Payload $id
            Assert ([CombinedTestState]::MsiQueryProductStateW(('{'+$previousSingle.games.$id.productCode+'}')) -eq -1) "$id old instance unregistered after successful upgrade"
            Assert ((Hash $data[$id].original) -eq $originalHash -and (Hash (Join-Path $data[$id].profile 'dlss.ini')) -eq $prefsHash) "$id upgrade preserves first backup and user settings"
            foreach($path in $otherFiles.Keys){if((Hash $path) -ne $otherFiles[$path]){throw "Other game changed during upgrade: $path"}}
            Assert ((Hash $data[$other].original) -eq $otherOriginal -and (Hash (Join-Path $data[$other].profile 'dlss.ini')) -eq $otherPrefs -and (Count-Transactions $other) -eq $otherTransactions) "$id upgrade leaves other game files, settings and transactions untouched"
            $oldShortcut=$data[$id].shortcut.Replace(' '+$manifest.version+'.lnk',' '+$previousSingle.version+'.lnk')
            Assert (-not(Test-Path -LiteralPath $oldShortcut) -and (Test-Path -LiteralPath $data[$id].shortcut)) "$id upgrade replaces only its versioned shortcut"
        }
        Check-Selection
        Run-Msi 'bs1' 'upgrade-remove-bs1' '' 0 'remove'
        Check-Removed 'bs1';Check-Payload 'bs2'
        Run-Msi 'bs2' 'upgrade-remove-bs2' '' 0 'remove'
        Check-Removed 'bs2'
    } elseif($PredecessorManifest) {
        $previous=Get-Content -LiteralPath $PredecessorManifest -Raw | ConvertFrom-Json
        if($previous.testFamily -ne $family -or $previous.kind -ne 'native-combined-msi'){throw 'Invalid isolated predecessor.'}
        $oldMsi=Join-Path (Split-Path $PredecessorManifest) $previous.installer
        if((Hash $oldMsi) -ne $previous.sha256){throw 'Predecessor hash mismatch.'}
        $legacy=$previous.testPredecessor -eq 'bs1'
        Run-Previous '00-previous' $(if($legacy){'ADDLOCAL=Game_bs1,Desktop_bs1'}else{'ADDLOCAL=ALL'})
        # Reproduce the accepted historical BS1 snapshot format, without editing
        # any real backup. This is only the fixture's own Original.xml.
        $originalText=Get-Content -LiteralPath $data.bs1.original -Raw
        Write-Fixture $data.bs1.original ($originalText.Replace('host64\','host64/'))
        $originalBs1=Hash $data.bs1.original
        if($legacy) {
            Add-Type -Path (Join-Path (Split-Path $msi) 'work\WixToolset.Dtf.WindowsInstaller.dll')
            Add-Type -Path (Join-Path (Split-Path $msi) 'work\LegacyFixtures.dll')
            $legacyManifest=if($BetaShortcut -eq 'standard'){
                [BioShockMsi.Actions]::CreateMsiMigrationFixture($fixture)
            }else{[BioShockMsi.Actions]::CreateNamedBetaMsiFixture($fixture,($BetaShortcut -eq 'beta-original'))}
            $oldShortcut=Join-Path $fixture ('Desktop\BioShock 2 VR DLSS-DLAA'+$(if($BetaShortcut -eq 'standard'){''}else{' Beta'})+'.lnk')
            $oldShortcutHash=Hash $oldShortcut
            $legacyHash=Hash $legacyManifest
            $legacyFiles=@{}
            foreach($line in Get-Content -LiteralPath $legacyManifest){
                if($line.StartsWith('File|')){$parts=$line.Split('|');$legacyFiles[[Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($parts[1]))]=$parts[5]}
            }
        }
        $bs2Transactions=Count-Transactions 'bs2'
        $originalBs2=Hash $data.bs2.original
        $beforeBs2=@{}
        foreach($file in Get-ChildItem -LiteralPath $data.bs2.game -File -Recurse){$beforeBs2[$file.FullName]=Hash $file.FullName}
        Run-Msi 'bs1' '01-upgrade-bs1-rollback' 'BVR_TESTFAIL=1 BVR_TESTFAILGAME=bs1' 1603
        Assert ([CombinedTestState]::MsiQueryProductStateW(('{'+$previous.productCode+'}')) -eq 5) 'Failed upgrade restores previous native product registration'
        Assert ([CombinedTestState]::MsiQueryFeatureStateW(('{'+$previous.productCode+'}'),'Game_bs1') -eq 3) 'Failed upgrade restores previous BS1 feature'
        Check-Payload 'bs1'
        Run-Msi 'bs1' '02-upgrade-bs1-only'
        Check-Payload 'bs1'
        Assert ((Hash $data.bs1.original) -eq $originalBs1) 'BS1 upgrade preserves historical slash snapshot byte for byte'
        foreach($path in $beforeBs2.Keys){if((Hash $path) -ne $beforeBs2[$path]){throw "BS2 changed during BS1 upgrade: $path"}}
        Assert ((Count-Transactions 'bs2') -eq $bs2Transactions -and (Hash $data.bs2.original) -eq $originalBs2) 'BS1 upgrade leaves all BS2 files and transactions untouched'
        if($legacy){
            Assert ((Hash $legacyManifest) -eq $legacyHash) 'Choosing BS1 leaves BS2 beta and manifest untouched'
            Assert ([CombinedTestState]::MsiQueryProductStateW(('{'+$previous.productCode+'}')) -eq -1) 'Old standalone BS1 MSI removed'
        }else{
            Assert ([CombinedTestState]::MsiQueryFeatureStateW(('{'+$previous.productCode+'}'),'Game_bs2') -eq 3) 'Old combined MSI still owns only BS2'
            Assert ([CombinedTestState]::MsiQueryFeatureStateW(('{'+$previous.productCode+'}'),'Game_bs1') -eq 2) 'Selected BS1 feature retired from old combined MSI'
        }
        $bs1Transactions=Count-Transactions 'bs1'
        if($legacy){
            Run-Msi 'bs2' '03a-beta-rollback-after-retire' 'BVR_TESTFAIL=after-retire BVR_TESTFAILGAME=bs2' 1603
            Assert ((Hash $legacyManifest) -eq $legacyHash -and (Hash $oldShortcut) -eq $oldShortcutHash) 'Failed beta migration restores exact manifest and old shortcut'
            foreach($path in $beforeBs2.Keys){if((Hash $path) -ne $beforeBs2[$path]){throw "Beta rollback mismatch: $path"}}
            Assert (-not(Test-Path -LiteralPath $data.bs2.original)) 'Failed beta migration removes only its new original snapshot'
            Run-Msi 'bs2' '03b-beta-rollback-after-files' 'BVR_TESTFAIL=1 BVR_TESTFAILGAME=bs2' 1603
            Assert ((Hash $legacyManifest) -eq $legacyHash -and (Hash $oldShortcut) -eq $oldShortcutHash) 'Late beta migration failure also restores manifest and shortcut'
        }
        Run-Msi 'bs2' '03-upgrade-bs2-only'
        Check-Payload 'bs1';Check-Payload 'bs2'
        Assert ((Count-Transactions 'bs1') -eq $bs1Transactions) 'Choosing BS2 does not repair or change BS1'
        Assert ([CombinedTestState]::MsiQueryProductStateW(('{'+$previous.productCode+'}')) -eq -1) 'No obsolete predecessor product remains'
        if($legacy){
            Assert (-not(Test-Path -LiteralPath $legacyManifest) -and -not(Test-Path -LiteralPath $oldShortcut)) 'Migrated beta manifest and obsolete shortcut retired to recovery copies'
            Assert (Test-Path -LiteralPath $data.bs2.shortcut) 'New versioned shortcut installed in its own destination'
            Run-Msi 'bs2' '03c-beta-uninstall-rollback' 'BVR_TESTFAIL=1 BVR_TESTFAILGAME=bs2' 1603 'remove'
            Check-Payload 'bs2'
            Assert (-not(Test-Path -LiteralPath $oldShortcut)) 'Failed uninstall does not leave a resurrected beta shortcut'
        }
        Run-Msi 'bs1' '04-remove-bs1' '' 0 'remove'
        Check-Removed 'bs1';Check-Payload 'bs2'
        Run-Msi 'bs2' '05-remove-bs2' '' 0 'remove'
        if($legacy){
            foreach($name in $legacyFiles.Keys){Assert ((Hash (Join-Path $data.bs2.game $name)) -eq $legacyFiles[$name]) "Restored pre-beta BS2 file: $name"}
            if($BetaShortcut -eq 'beta-new'){
                Assert (-not(Test-Path -LiteralPath $oldShortcut)) 'No pre-beta shortcut existed: none recreated on uninstall'
            }else{
                Assert ((Get-Content -LiteralPath $oldShortcut -Raw) -eq 'BEFORE BETA shortcut') 'Original shortcut restored under its original name'
            }
        }else{Check-Removed 'bs2'}
    } else {
    & (Join-Path (Split-Path $msi) 'work\SnapshotTests.exe')
    if($LASTEXITCODE -ne 0){throw 'Historical snapshot regression test failed.'}
    Assert $true 'Historical BS1 snapshots accepted without altering their source'
    & (Join-Path (Split-Path $msi) 'work\MigrationTests.exe')
    if($LASTEXITCODE -ne 0){throw 'Beta shortcut regression tests failed.'}
    Assert $true 'Legacy migration validation and exact Beta shortcut regression pass'
    Check-Selection
    Run-Msi 'bs1' '01-install-bs1'
    Check-Payload 'bs1';Check-Removed 'bs2'
    $bs1Transactions=Count-Transactions 'bs1'
    $bs1Original=Hash $data.bs1.original
    Write-Fixture (Join-Path $data.bs1.profile 'dlss.ini') "mode=DLAA\r\nuser=bs1"
    Run-Msi 'bs2' '02-bs2-failed-install' 'BVR_TESTFAIL=after-retire BVR_TESTFAILGAME=bs2' 1603
    Check-Payload 'bs1';Check-Removed 'bs2'
    Assert ((Count-Transactions 'bs1') -eq $bs1Transactions) 'Failed BS2 install does not touch BS1'
    Run-Msi 'bs2' '03-install-bs2'
    Check-Payload 'bs1';Check-Payload 'bs2'
    Assert ((Count-Transactions 'bs1') -eq $bs1Transactions -and (Hash $data.bs1.original) -eq $bs1Original) 'Installing BS2 leaves BS1 transactions/originals untouched'
    Check-Selection
    Write-Fixture (Join-Path $data.bs2.profile 'dlss.ini') "mode=DLSS\r\nuser=bs2"
    $bs2Original=Hash $data.bs2.original
    $bs2Prefs=Hash (Join-Path $data.bs2.profile 'dlss.ini')
    Write-Fixture (Join-Path $data.bs2.game 'bioshockvr.dll') 'corrupted-in-isolated-test'
    Run-Msi 'bs2' '04-repair-bs2' 'REINSTALL=ALL REINSTALLMODE=amus'
    Check-Payload 'bs2'
    Assert ((Count-Transactions 'bs1') -eq $bs1Transactions) 'Repairing BS2 does not touch BS1'
    Assert ((Hash $data.bs2.original) -eq $bs2Original -and (Hash (Join-Path $data.bs2.profile 'dlss.ini')) -eq $bs2Prefs) 'Repair preserves originals and preferences'
    Run-Msi 'bs2' '05-repair-bs2-rollback' 'REINSTALL=ALL REINSTALLMODE=amus BVR_TESTFAIL=after-retire BVR_TESTFAILGAME=bs2' 1603
    Check-Payload 'bs1';Check-Payload 'bs2'
    $bs2Transactions=Count-Transactions 'bs2'
    Run-Msi 'bs1' '06-bs1-uninstall-rollback' 'BVR_TESTFAIL=1 BVR_TESTFAILGAME=bs1' 1603 'remove'
    Check-Payload 'bs1';Check-Payload 'bs2'
    foreach($id in @('bs1','bs2')) {
        Assert ([CombinedTestState]::MsiQueryProductStateW(('{'+$manifest.games.$id.productCode+'}')) -eq 5) "Rollback leaves product $id installed"
    }
    Assert ((Count-Transactions 'bs2') -eq $bs2Transactions) 'BS1 uninstall rollback leaves BS2 untouched'
    Run-Msi 'bs1' '07-remove-bs1' '' 0 'remove'
    Check-Removed 'bs1';Check-Payload 'bs2'
    Run-Msi 'bs1' '08-reinstall-bs1'
    Check-Payload 'bs1';Check-Payload 'bs2'
    Assert ((Count-Transactions 'bs2') -eq $bs2Transactions) 'BS1 reinstall leaves BS2 untouched'
    Run-Msi 'bs2' '09-bs2-no-shortcut' 'REINSTALL=ALL REINSTALLMODE=amus BVR_BS2_DESKTOPSHORTCUT=0'
    Assert (-not(Test-Path -LiteralPath $data.bs2.shortcut) -and (Test-Path -LiteralPath $data.bs1.shortcut)) 'Independent desktop shortcuts'
    Run-Msi 'bs2' '10-bs2-shortcut-again' 'REINSTALL=Game_bs2 REINSTALLMODE=amus BVR_BS2_DESKTOPSHORTCUT=1'
    Assert (Test-Path -LiteralPath $data.bs2.shortcut) 'Shortcut can be restored independently'
    Run-Msi 'bs2' '11-remove-bs2' '' 0 'remove'
    Check-Removed 'bs2';Check-Payload 'bs1'
    Run-Msi 'bs1' '12-remove-bs1' '' 0 'remove'
    Check-Removed 'bs1'
    foreach($id in @('bs1','bs2')) {
        Assert ([CombinedTestState]::MsiQueryProductStateW(('{'+$manifest.games.$id.productCode+'}')) -eq -1) "Product $id cleanly unregistered"
    }
    }
} catch { $failure = $_ }
finally {
    foreach($id in @('bs1','bs2')) {
        if([CombinedTestState]::MsiQueryProductStateW(('{'+$manifest.games.$id.productCode+'}')) -eq 5) {
            try {Run-Msi $id ('cleanup-'+$id) '' 0 'remove'} catch {if(-not $failure){$failure=$_}}
        }
    }
    if($previous -and [CombinedTestState]::MsiQueryProductStateW(('{'+$previous.productCode+'}')) -eq 5){
        try{Run-Previous 'cleanup-previous' '' 'remove'}catch{if(-not $failure){$failure=$_}}
    }
    if($previousSingle){
        foreach($id in @('bs1','bs2')) {
            if([CombinedTestState]::MsiQueryProductStateW(('{'+$previousSingle.games.$id.productCode+'}')) -eq 5){
                try{Run-Msi $id ('cleanup-old-'+$id) '' 0 'remove' $previousSingle}catch{if(-not $failure){$failure=$_}}
            }
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
    $reportName=if($PreviousSingleGameManifest){'test-result-instance-upgrade.json'}elseif(-not $PredecessorManifest){'test-result.json'}elseif($previous.testPredecessor){'test-result-standalone-'+$BetaShortcut+'.json'}else{'test-result-combined-upgrade.json'}
    [IO.File]::WriteAllText((Join-Path (Split-Path $ManifestPath) $reportName), ($report | ConvertTo-Json -Depth 6), $utf8)
}
if ($failure) { throw $failure }
Write-Output "MSI por juego: $($checks.Count) comprobaciones, $($logs.Count) operaciones."
