[CmdletBinding()]
param([string]$Version = '0.2.11')
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$release = Join-Path $repo ('artifacts\stable-' + $Version)
$manifest = Get-Content -LiteralPath (Join-Path $release "manifest-$Version.json") -Raw | ConvertFrom-Json
$msi = Join-Path $release $manifest.installer
$dtf = Join-Path $repo 'artifacts\msi-tools\wixtoolset.dtf.windowsinstaller.6.0.2\lib\net20\WixToolset.Dtf.WindowsInstaller.dll'
[void][Reflection.Assembly]::LoadFrom($dtf)
$checks = New-Object 'System.Collections.Generic.List[string]'
function Assert([bool]$Pass, [string]$Message) {
    if (-not $Pass) { throw $Message }
    $checks.Add($Message)
    Write-Output ('PASS: ' + $Message)
}
Assert ((Get-FileHash -LiteralPath $msi).Hash -eq $manifest.sha256) 'Hash del MSI coincide con su manifiesto'
$database = New-Object WixToolset.Dtf.WindowsInstaller.Database($msi, [WixToolset.Dtf.WindowsInstaller.DatabaseOpenMode]::ReadOnly)
try {
    $view = $database.OpenView('SELECT `Control`, `Type`, `Y`, `Height`, `Text` FROM `Control` WHERE `Dialog_` = ''BioShockProgressDlg''')
    $controls = @{}
    try {
        $view.Execute()
        while ($record = $view.Fetch()) {
            try { $controls[$record.GetString(1)] = @{Type=$record.GetString(2);Y=$record.GetInteger(3);Height=$record.GetInteger(4);Text=$record.GetString(5)} }
            finally { $record.Dispose() }
        }
    } finally { $view.Dispose() }
    $view = $database.OpenView('SELECT `Condition` FROM `InstallUISequence` WHERE `Action` = ''ProgressDlg''')
    try {
        $view.Execute(); $record = $view.Fetch()
        try { Assert ($record.GetString(1) -eq '0') 'El diálogo estándar ya no puede sobreescribir al nuevo' }
        finally { $record.Dispose() }
    } finally { $view.Dispose() }
    Assert ($controls.Count -gt 0) 'El MSI contiene el diálogo de progreso propio'
    Assert (-not $controls.ContainsKey('NoLaunch')) 'Progreso sin el texto sobre no abrir el lanzador'
    $view = $database.OpenView('SELECT `Control`, `Property`, `Text` FROM `Control` WHERE `Dialog_` = ''GameFolderDlg''')
    $folderControls = @{}
    try {
        $view.Execute()
        while ($record = $view.Fetch()) {
            try { $folderControls[$record.GetString(1)] = @{Property=$record.GetString(2);Text=$record.GetString(3)} }
            finally { $record.Dispose() }
        }
    } finally { $view.Dispose() }
    Assert (-not $folderControls.ContainsKey('NoLaunch')) 'Carpeta sin el texto sobre no abrir el lanzador'
    Assert ($folderControls.Runtime.Text -eq 'Incluye NVIDIA DLSS 310.7.0.0.') 'NVIDIA sin el comentario entre paréntesis'
    Assert ($folderControls.DesktopShortcut.Text -eq 'Crear acceso directo en tu escritorio' -and $folderControls.DesktopShortcut.Property -eq 'BVR_DESKTOPSHORTCUT') 'Casilla de acceso enlazada a la elección real'
    $view = $database.OpenView('SELECT `Control`, `Text` FROM `Control` WHERE `Dialog_` = ''BioShockExitDlg''')
    $exitControls = @{}
    try {
        $view.Execute()
        while ($record = $view.Fetch()) {
            try { $exitControls[$record.GetString(1)] = $record.GetString(2) }
            finally { $record.Dispose() }
        }
    } finally { $view.Dispose() }
    Assert ($exitControls.ShortcutHelp.Contains('[ProductVersion]') -and $exitControls.ShortcutHelp.Contains('doble clic')) 'Final explica cómo abrir el acceso versionado'
    Assert ($exitControls.FolderHelp.Contains('Lanzador BioShock VR DLSS-DLAA.exe') -and $exitControls.GamePath -eq '[GAMEDIR]') 'Final alternativo indica el ejecutable y la carpeta elegida'
    Assert ($exitControls.Removed.Contains('desinstalación')) 'Desinstalar tiene un mensaje final propio'
    $view = $database.OpenView('SELECT `Sequence`, `Condition` FROM `InstallUISequence` WHERE `Action` = ''BioShockExitDlg''')
    try {
        $view.Execute(); $record = $view.Fetch()
        try { Assert ($record.GetInteger(1) -eq -1) 'Las instrucciones solo aparecen al terminar con éxito' }
        finally { $record.Dispose() }
    } finally { $view.Dispose() }
    $view = $database.OpenView('SELECT `Condition` FROM `InstallUISequence` WHERE `Action` = ''ExitDialog''')
    try {
        $view.Execute(); $record = $view.Fetch()
        try { Assert ($record.GetString(1) -eq '0') 'El final estándar no tapa las instrucciones nuevas' }
        finally { $record.Dispose() }
    } finally { $view.Dispose() }
    $view = $database.OpenView('SELECT `Feature_` FROM `FeatureComponents` WHERE `Component_` = ''VersionedDesktopLauncher''')
    try {
        $view.Execute(); $record = $view.Fetch()
        try { Assert ($record.GetString(1) -eq 'DesktopShortcut') 'Acceso como característica MSI independiente del mod' }
        finally { $record.Dispose() }
    } finally { $view.Dispose() }
    Assert ($controls.ActionText.Height -ge 38) 'El texto de estado tiene espacio para varias líneas'
    Assert (($controls.ActionText.Y + $controls.ActionText.Height + 10) -le $controls.ProgressBar.Y) 'Texto de estado separado de la barra por al menos 10 unidades'
    foreach ($name in @('Title','Description','StatusLabel','ActionText')) {
        Assert (($controls[$name].Y + $controls[$name].Height) -lt $controls.ProgressBar.Y) ('No solapa con la barra: ' + $name)
    }
    $view = $database.OpenView('SELECT `Name`, `Target`, `Component_` FROM `Shortcut` WHERE `Shortcut` = ''DesktopLauncher''')
    try {
        $view.Execute(); $record = $view.Fetch()
        try {
            Assert ($record.GetString(1).Split('|')[-1] -eq ('BioShock VR DLSS-DLAA ' + $Version)) 'Acceso directo con la versión visible'
            Assert ($record.GetString(2) -eq '[GAMEDIR]Lanzador BioShock VR DLSS-DLAA.exe') 'El acceso mantiene el destino correcto'
            Assert ($record.GetString(3) -eq 'VersionedDesktopLauncher') 'El acceso tiene componente propio para las actualizaciones'
        } finally { $record.Dispose() }
    } finally { $view.Dispose() }
    $view = $database.OpenView('SELECT `FileName`, `DirProperty` FROM `RemoveFile` WHERE `FileKey` = ''RetireUnversionedShortcut''')
    try {
        $view.Execute(); $record = $view.Fetch()
        try {
            Assert ($record.GetString(1).Split('|')[-1] -eq 'BioShock VR DLSS-DLAA.lnk' -and $record.GetString(2) -eq 'DesktopFolder') 'Solo retira el acceso antiguo de nombre exacto'
        } finally { $record.Dispose() }
    } finally { $view.Dispose() }
} finally { $database.Dispose() }

# Exercise recovery path mapping without installing or opening a game/session.
$assembly = [Reflection.Assembly]::LoadFrom((Join-Path $release "msi-build-$Version\BioShockMsiActions.dll"))
$choice = $assembly.GetType('BioShockMsi.Actions').GetMethod('DesktopShortcutChoice', [Reflection.BindingFlags]'Static,NonPublic')
foreach ($case in @(@('','','1'), @('','0','0'), @('','1','1'), @('0','1','0'), @('1','0','1'))) {
    Assert ([string]$choice.Invoke($null, @($case[0],$case[1])) -eq $case[2]) ('Acceso predeterminado/preferencia: solicitado=' + $case[0] + ', guardado=' + $case[1] + ', resultado=' + $case[2])
}
$destination = $assembly.GetType('BioShockMsi.Actions').GetMethod('Destination', [Reflection.BindingFlags]'Static,NonPublic')
$data = New-Object WixToolset.Dtf.WindowsInstaller.CustomActionData
$data['Desktop'] = Join-Path $repo 'artifacts\presentation-test\Desktop'
$data['Shortcut'] = Join-Path $data['Desktop'] 'BioShock VR DLSS-DLAA.lnk'
Assert ($destination.Invoke($null, @($data.PSObject.BaseObject, '@shortcut')) -eq $data['Shortcut']) 'Las copias antiguas conservan el destino sin versión'
foreach ($oldVersion in @($Version, '0.2.8', '0.3.0')) {
    $expected = Join-Path $data['Desktop'] ('BioShock VR DLSS-DLAA ' + $oldVersion + '.lnk')
    Assert ($destination.Invoke($null, @($data.PSObject.BaseObject, ('@shortcut:' + $oldVersion))) -eq $expected) ('Recuperación conserva la versión del acceso: ' + $oldVersion)
}
foreach ($invalid in @('@shortcut:..\outside', '@shortcut:0.2.9\x', '@shortcut:0.2.9.exe')) {
    $rejected = $false
    try { [void]$destination.Invoke($null, @($data.PSObject.BaseObject, $invalid)) } catch { $rejected = $true }
    Assert $rejected ('Rechaza destino inválido: ' + $invalid)
}
# Pure transformation only: no file, profile, game or Installer registration writes.
$defaults = $assembly.GetType('BioShockMsi.Actions').GetMethod('ApplyGraphicsDefaults', [Reflection.BindingFlags]'Static,NonPublic')
$fixture = "[WinDrv.WindowsClient]`r`nWindowedViewportX=3072`r`n[Engine.RenderConfig]`r`nRealTimeReflection=True`r`nUseRippleSystem=True`r`n[Unrelated]`r`nRealTimeReflection=KeepThis`r`n"
$updated = [string]$defaults.Invoke($null, @($fixture))
$section = [regex]::Match($updated, '(?ms)^\[Engine\.RenderConfig\]\r?\n(.*?)(?=^\[|\z)').Groups[1].Value
foreach ($key in @('HighDetailShaders','Shadows','RealTimeReflection','PostProcessing','UseRippleSystem','UseHighDetailSoftParticles','UseDistortion','UseHighDetailPostProcEffects','FluidSurfaceDetail')) {
    $expected = if ($key -eq 'FluidSurfaceDetail') { 'High' } elseif ($key -in @('RealTimeReflection','UseRippleSystem')) { 'False' } else { 'True' }
    Assert ($section -match "(?m)^$key=$expected\r?$") ('Predeterminado: ' + $key + '=' + $expected)
}
Assert ($updated.StartsWith("[WinDrv.WindowsClient]`r`nWindowedViewportX=3072`r`n") -and $updated.EndsWith("[Unrelated]`r`nRealTimeReflection=KeepThis`r`n")) 'Predeterminados no alteran resolución ni otras secciones'
Assert ([string]$defaults.Invoke($null, @($updated)) -eq $updated) 'Aplicar predeterminados dos veces produce el mismo resultado'
$withoutSection = "[Unrelated]`r`nKey=Value`r`n"
Assert ([string]$defaults.Invoke($null, @($withoutSection)) -eq $withoutSection) 'No inventa una sección gráfica si falta el INI inicial del juego'
$launcherVersion = [Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $release "msi-build-$Version\payload\Lanzador BioShock VR DLSS-DLAA.exe")).FileVersion
Assert ($launcherVersion -eq ($Version + '.0')) 'La versión del ejecutable coincide con el acceso y el MSI'
$output = Join-Path $release "presentation-test-$Version.json"
[IO.File]::WriteAllText($output, ([ordered]@{version=$Version;passed=@($checks);installationRun=$false} | ConvertTo-Json -Depth 4), (New-Object Text.UTF8Encoding($false)))
Write-Output ('Informe: ' + $output)
