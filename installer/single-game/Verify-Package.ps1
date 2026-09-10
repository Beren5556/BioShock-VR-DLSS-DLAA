[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$ManifestPath,
    [Parameter(Mandatory=$true)][string]$BuildToolsDirectory,
    [string]$Bs1SourceMsi = '',
    [string]$Bs2SourceMsi = ''
)
$ErrorActionPreference='Stop'
$manifest=Get-Content -LiteralPath $ManifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$validated=$null
if ([version]$manifest.version -ge [version]'0.2.16') {
    $validated=Get-Content -LiteralPath (Join-Path $repo ("release\validated-mods-v"+$manifest.version+".json")) -Raw | ConvertFrom-Json
    foreach($id in @('bs1','bs2')) {
        if($manifest.games.$id.modBuild -ne $validated.modBuild -or $manifest.games.$id.runtime -ne $validated.runtime){throw 'Wrong validated mod/runtime identity.'}
        foreach($file in $validated.games.$id.files) {
            $entry=@($manifest.games.$id.files | Where-Object {$_.path.Replace('/','\') -eq $file.path.Replace('/','\')})
            if($entry.Count -ne 1 -or $entry[0].sha256 -ne $file.sha256){throw "Validated fix missing from final MSI: $id $($file.path)"}
        }
    }
}
$packageRoot=Split-Path ([IO.Path]::GetFullPath($ManifestPath))
$msi=Join-Path $packageRoot $manifest.installer
if ($manifest.kind -ne 'native-single-game-msi' -or (Get-FileHash -LiteralPath $msi).Hash -ne $manifest.sha256) { throw 'Wrong package identity/hash.' }
$extracted=Join-Path $packageRoot ('verify-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $extracted | Out-Null
Add-Type -Path (Join-Path $BuildToolsDirectory 'wixtoolset.dtf.windowsinstaller.6.0.2\lib\net20\WixToolset.Dtf.WindowsInstaller.dll')
$database=New-Object WixToolset.Dtf.WindowsInstaller.Database($msi, [WixToolset.Dtf.WindowsInstaller.DatabaseOpenMode]::ReadOnly)
try {
    if (($database.SummaryInfo.WordCount -band 8) -ne 0) { throw 'UAC-capable package metadata missing.' }
    if (@($database.ExecuteStringQuery('SELECT `File` FROM `File`')).Count -ne 46) { throw 'Expected exactly 46 payload files.' }
    if (@($database.ExecuteStringQuery('SELECT `Feature` FROM `Feature`')).Count -ne 4) { throw 'Expected two mods and two optional shortcuts.' }
    if([version]$manifest.version -ge [version]'0.2.15'){
        $selectorText=$database.ExecuteStringQuery("SELECT ``Text`` FROM ``Control`` WHERE ``Dialog_``='GameSelectorDlg' AND ``Type``='Text'")
        if($selectorText.Count -ne 1 -or $selectorText[0] -notlike '*Selecciona el juego'){throw 'Selector should contain only its short title and game control.'}
        $bs1Label=$database.ExecuteStringQuery("SELECT ``Text`` FROM ``ComboBox`` WHERE ``Property``='BVR_GAME' AND ``Value``='bs1'")[0]
        if($bs1Label -ne 'BioShock Remastered'){throw 'Unwanted BS1 suffix.'}
        if($database.ExecuteStringQuery("SELECT ``Control`` FROM ``Control`` WHERE ``Dialog_``='SingleReadyDlg' AND ``Control``='Independent'").Count -ne 0){throw 'Removed confirmation paragraph is still present.'}
    }
    if ($database.ExecuteStringQuery("SELECT ``Value`` FROM ``Property`` WHERE ``Property``='BVR_INSTANCE'")[0] -ne 'selector') { throw 'First screen must be the game selector.' }
    if (@($database.ExecuteStringQuery('SELECT `Name` FROM `_Storages`') | Where-Object {$_ -in @('bs1','bs2')}).Count -ne 2) { throw 'Both native instance transforms required.' }
    if (@($database.ExecuteIntegerQuery('SELECT `Level` FROM `Feature`') | Where-Object {$_ -ne 0}).Count -ne 0) { throw 'Base selector must not select any payload.' }
    foreach($id in @('bs1','bs2')) {
        $view=$database.OpenView('SELECT `Name`, `Data` FROM `_Storages` WHERE `Name` = ?')
        $parameter=New-Object WixToolset.Dtf.WindowsInstaller.Record(1)
        $mst=Join-Path $extracted ($id+'.mst')
        try {
            $parameter.SetString(1,$id);$view.Execute($parameter);$row=$view.Fetch()
            if($null -eq $row){throw 'Missing instance transform.'}
            try{$row.GetStream(2,$mst)}finally{$row.Dispose()}
        }finally{$view.Dispose();$parameter.Dispose()}
        $copy=Join-Path $extracted ($id+'.msi');Copy-Item -LiteralPath $msi -Destination $copy
        $instance=New-Object WixToolset.Dtf.WindowsInstaller.Database($copy,[WixToolset.Dtf.WindowsInstaller.DatabaseOpenMode]::Transact)
        try {
            $instance.ApplyTransform($mst,[WixToolset.Dtf.WindowsInstaller.TransformErrors]::None)
            $product=$instance.ExecuteStringQuery("SELECT ``Value`` FROM ``Property`` WHERE ``Property``='ProductCode'")[0]
            $upgrade=$instance.ExecuteStringQuery("SELECT ``Value`` FROM ``Property`` WHERE ``Property``='UpgradeCode'")[0]
            $selected=$instance.ExecuteStringQuery("SELECT ``Value`` FROM ``Property`` WHERE ``Property``='BVR_INSTANCE'")[0]
            if($product -ne ('{'+$manifest.games.$id.productCode+'}') -or $upgrade -ne ('{'+$manifest.games.$id.legacyUpgradeCode+'}') -or $selected -ne $id){throw 'Wrong native instance identity.'}
        }finally{$instance.Dispose()}
    }
    if ($database.ExecuteStringQuery("SELECT ``Value`` FROM ``Property`` WHERE ``Property``='ALLUSERS'").Count -ne 0) { throw 'Do not change per-user scope.' }
    $actions=$database.ExecuteIntegerQuery('SELECT `Type` FROM `CustomAction`')
    foreach ($type in $actions) { if (($type -band 63) -in @(7,23,39)) { throw 'Nested MSI custom action forbidden.' } }
    $cabinets=$database.ExecuteStringQuery('SELECT `Cabinet` FROM `Media`')
    foreach ($cabinet in $cabinets) {
        if (-not $cabinet.StartsWith('#')) { throw 'All payloads must be embedded.' }
        $name=$cabinet.Substring(1)
        if ([IO.Path]::GetFileName($name) -ne $name) { throw 'Invalid cabinet name.' }
        $view=$database.OpenView('SELECT `Data` FROM `_Streams` WHERE `Name` = ?')
        $parameter=New-Object WixToolset.Dtf.WindowsInstaller.Record(1)
        try {
            $parameter.SetString(1,$name); $view.Execute($parameter)
            $row=$view.Fetch()
            if ($null -eq $row) { throw 'Embedded cabinet not found.' }
            try { $row.GetStream(1,(Join-Path $extracted $name)) } finally { $row.Dispose() }
        } finally { $parameter.Dispose(); $view.Dispose() }
        & (Join-Path $env:WINDIR 'System32\expand.exe') '-F:*' (Join-Path $extracted $name) $extracted | Out-Null
        if ($LASTEXITCODE -ne 0) { throw 'Cabinet extraction failed.' }
    }
    $verified=0
    $componentIdentities=0
    foreach ($id in @('bs1','bs2')) {
        $index=0
        foreach ($file in $manifest.games.$id.files) {
            $source=Join-Path $extracted ("File_${id}_$index")
            if ((Get-FileHash -LiteralPath $source).Hash -ne $file.sha256) { throw "Embedded payload mismatch: $id $($file.path)" }
            $index++; $verified++
        }
        $sourceMsi=if($id -eq 'bs1'){$Bs1SourceMsi}else{$Bs2SourceMsi}
        if($sourceMsi){
            if($manifest.testFamily){throw 'Production component identity comparison requires a production package.'}
            $sourceDb=New-Object WixToolset.Dtf.WindowsInstaller.Database($sourceMsi,[WixToolset.Dtf.WindowsInstaller.DatabaseOpenMode]::ReadOnly)
            try{
                $sourceIds=@{}
                $view=$sourceDb.OpenView('SELECT `File`.`FileName`, `Component`.`ComponentId` FROM `File`, `Component` WHERE `File`.`Component_` = `Component`.`Component`')
                try{
                    $view.Execute()
                    while($row=$view.Fetch()){
                        try{$sourceIds[$row.GetString(1).Split('|')[-1]]=$row.GetString(2)}finally{$row.Dispose()}
                    }
                }finally{$view.Dispose()}
                $index=0
                foreach($file in $manifest.games.$id.files){
                    $expected=$sourceIds[[IO.Path]::GetFileName($file.path)]
                    $actual=$database.ExecuteStringQuery("SELECT ``ComponentId`` FROM ``Component`` WHERE ``Component``='Cmp_${id}_$index'")[0]
                    if(-not $expected -or $actual -ne $expected){throw "Historical component identity mismatch: $id $($file.path)"}
                    $index++;$componentIdentities++
                }
            }finally{$sourceDb.Dispose()}
        }
    }
    $report=[ordered]@{passed=$true;sha256=$manifest.sha256;files=$verified;historicalComponentIdentities=$componentIdentities;nativeSingleMsi=$true;instances=@('bs1','bs2');selectorInstallsNothing=$true;perUser=$true;uacCapable=$true;validatedRuntimeFixes=($null -ne $validated);extracted=$extracted}
    [IO.File]::WriteAllText((Join-Path $packageRoot 'package-verification.json'),($report | ConvertTo-Json), (New-Object Text.UTF8Encoding($false)))
    Write-Output ("Verified native MSI: $verified embedded files, 4 features, no nested installations.")
} finally { $database.Dispose() }
