[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$ManifestPath,
    [Parameter(Mandatory=$true)][string]$SpanishMsi,
    [Parameter(Mandatory=$true)][string]$BuildToolsDirectory
)
$ErrorActionPreference='Stop'
$manifest=Get-Content -LiteralPath $ManifestPath -Raw | ConvertFrom-Json
$msi=Join-Path (Split-Path ([IO.Path]::GetFullPath($ManifestPath))) $manifest.installer
if((Get-FileHash -LiteralPath $msi).Hash -ne $manifest.sha256){throw 'English MSI hash mismatch.'}
if((Get-FileHash -LiteralPath $SpanishMsi).Hash -ne '1C881F0A27416198FC25A068A79044BEED7FCC37160F38E7AFE2AE09CE9A2371'){throw 'Expected the unchanged published Spanish 0.2.16 MSI.'}
[void][Reflection.Assembly]::LoadFrom((Join-Path $BuildToolsDirectory 'wixtoolset.dtf.windowsinstaller.6.0.2\lib\net20\WixToolset.Dtf.WindowsInstaller.dll'))
$db=New-Object WixToolset.Dtf.WindowsInstaller.Database($msi,[WixToolset.Dtf.WindowsInstaller.DatabaseOpenMode]::ReadOnly)
$old=New-Object WixToolset.Dtf.WindowsInstaller.Database($SpanishMsi,[WixToolset.Dtf.WindowsInstaller.DatabaseOpenMode]::ReadOnly)
$checks=New-Object 'System.Collections.Generic.List[string]'
function Assert([bool]$value,[string]$label){if(-not $value){throw $label};$checks.Add($label)}
function Rows($database,[string]$query,[int]$columns){
    $view=$database.OpenView($query)
    try{$view.Execute();while($record=$view.Fetch()){
        try{$values=@();foreach($i in 1..$columns){$values+=$record.GetString($i)};Write-Output (,$values)}finally{$record.Dispose()}
    }}finally{$view.Dispose()}
}
try{
    $dialogs=@{}
    foreach($row in (Rows $db 'SELECT `Dialog`, `Width`, `Height` FROM `Dialog`' 3)){$dialogs[$row[0]]=@{w=[int]$row[1];h=[int]$row[2]}}
    $controls=@{}
    $count=0
    foreach($row in (Rows $db 'SELECT `Dialog_`, `Control`, `Type`, `X`, `Y`, `Width`, `Height`, `Text` FROM `Control`' 8)){
        if($row[0] -notmatch '^(GameSelectorDlg|Single.*Dlg|Folder_bs[12]|SuiteErrorDlg)$'){continue}
        $bounds=$dialogs[$row[0]];$x=[int]$row[3];$y=[int]$row[4];$w=[int]$row[5];$h=[int]$row[6]
        Assert ($x -ge 0 -and $y -ge 0 -and $x+$w -le $bounds.w -and $y+$h -le $bounds.h) ('Control in bounds: '+$row[0]+'/'+$row[1])
        $controls[$row[0]+'/'+$row[1]]=@{x=$x;y=$y;w=$w;h=$h;text=$row[7];type=$row[2]};$count++
    }
    Assert ($controls['GameSelectorDlg/Title'].text -eq '{\WixUI_Font_Title}Select a game') 'Compact English game selector title'
    Assert (-not $controls.ContainsKey('GameSelectorDlg/Description') -and -not $controls.ContainsKey('GameSelectorDlg/Info')) 'No explanatory paragraphs returned to selector'
    $choices=Rows $db 'SELECT `Text` FROM `ComboBox` WHERE `Property` = ''BVR_GAME''' 1
    Assert (@($choices | Where-Object {$_[0] -eq 'BioShock Remastered'}).Count -eq 1) 'BS1 name has no parenthesized suffix'
    Assert ($controls['SingleProgressDlg/ActionText'].y+$controls['SingleProgressDlg/ActionText'].h+10 -le $controls['SingleProgressDlg/Progress'].y) 'Progress text keeps a ten-unit gap above its bar'
    Assert ($controls['SingleExitDlg/Info'].y+$controls['SingleExitDlg/Info'].h -lt $controls['SingleExitDlg/Preserved'].y) 'Completion paragraphs do not overlap'
    $oldComponents=@{};foreach($row in (Rows $old 'SELECT `Component`, `ComponentId`, `Directory_`, `KeyPath` FROM `Component`' 4)){$oldComponents[$row[0]]=($row[1..3] -join '|')}
    $same=0;$versioned=0
    foreach($row in (Rows $db 'SELECT `Component`, `ComponentId`, `Directory_`, `KeyPath` FROM `Component`' 4)){
        if($row[0] -match '^ShortcutComponent_bs[12]$'){
            $previous=$oldComponents[$row[0]].Split('|')
            Assert ($row[1] -ne $previous[0] -and $row[2] -eq $previous[1]) ('Separate identity for the new versioned shortcut: '+$row[0])
            $keyRows=@(Rows $db ('SELECT `Name` FROM `Registry` WHERE `Registry` = '''+$row[3]+'''') 1)
            Assert ($keyRows[0][0] -eq '0.2.17') ('Shortcut registration belongs to version 0.2.17: '+$row[0])
            $versioned++;continue
        }
        Assert ($oldComponents.ContainsKey($row[0]) -and $oldComponents[$row[0]] -ceq ($row[1..3] -join '|')) ('Unchanged Spanish upgrade component: '+$row[0]);$same++
    }
    Assert ($same+$versioned -eq $oldComponents.Count -and $versioned -eq 2) 'Only the two versioned shortcut identities change'
    $language=@(Rows $db 'SELECT `Value` FROM `Property` WHERE `Property` = ''ProductLanguage''' 1)
    Assert ($language[0][0] -eq '1033') 'English MSI ProductLanguage 1033'
    $report=[ordered]@{passed=$true;installerSha256=$manifest.sha256;controlsChecked=$count;unchangedComponents=$same;versionedShortcuts=$versioned;installationRun=$false;checks=$checks.ToArray()}
    $output=Join-Path (Split-Path ([IO.Path]::GetFullPath($ManifestPath))) 'english-presentation.json'
    [IO.File]::WriteAllText($output,($report | ConvertTo-Json -Depth 5),(New-Object Text.UTF8Encoding($false)))
    Write-Output ('PASS: '+$count+' controls in bounds, '+$same+' unchanged upgrade components; no installation. '+$output)
}finally{$db.Dispose();$old.Dispose()}
