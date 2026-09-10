[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$BioShock1Msi,
    [Parameter(Mandatory=$true)][string]$BioShock2Manifest,
    [Parameter(Mandatory=$true)][string]$BuildToolsDirectory
)
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$folder = Join-Path $repo 'artifacts\integration-0.2.13\bundle'
$bundleManifest = Get-Content -LiteralPath (Join-Path $folder 'manifest.json') -Raw | ConvertFrom-Json
$bs2 = Get-Content -LiteralPath $BioShock2Manifest -Raw | ConvertFrom-Json
$msi2 = Join-Path (Split-Path ([IO.Path]::GetFullPath($BioShock2Manifest))) $bs2.installer
$bundle = Join-Path $folder $bundleManifest.installer
$dtf = Join-Path $BuildToolsDirectory 'wixtoolset.dtf.windowsinstaller.6.0.2\lib\net20\WixToolset.Dtf.WindowsInstaller.dll'
[void][Reflection.Assembly]::LoadFrom($dtf)
$checks = New-Object 'System.Collections.Generic.List[string]'
function Assert([bool]$Pass,[string]$Message) {
    if (-not $Pass) { throw $Message }
    $checks.Add($Message); Write-Host ('PASS: '+$Message)
}
function Rows($Database,[string]$Sql,[int]$Columns) {
    $view = $Database.OpenView($Sql)
    try {
        $view.Execute()
        while ($record = $view.Fetch()) {
            try {
                $row = New-Object string[] $Columns
                for ($i=0; $i -lt $Columns; $i++) { $row[$i]=$record.GetString($i+1) }
                Write-Output -NoEnumerate $row
            } finally { $record.Dispose() }
        }
    } finally { $view.Dispose() }
}
function Inspect([string]$Path,[string]$Game,[string]$Version) {
    $db=New-Object WixToolset.Dtf.WindowsInstaller.Database($Path,[WixToolset.Dtf.WindowsInstaller.DatabaseOpenMode]::ReadOnly)
    try {
        $properties=@{}
        foreach($r in (Rows $db 'SELECT `Property`, `Value` FROM `Property`' 2)) { $properties[$r[0]]=$r[1] }
        Assert ($properties.ProductVersion -eq $Version) "$Game internal MSI version"
        $components=@(Rows $db 'SELECT `ComponentId` FROM `Component`' 1 | ForEach-Object { $_[0] })
        Assert ($components.Count -ge 23 -and @($components|Select-Object -Unique).Count -eq $components.Count) "$Game component identifiers are unique"
        $registry=@(Rows $db 'SELECT `Key` FROM `Registry`' 1 | ForEach-Object { $_[0] } | Select-Object -Unique)
        $expectedKey=if($Game -eq 'bs2'){'Software\Beren5556\BioShock2VRDLSSDLAA'}else{'Software\Beren5556\BioShockVRDLSSDLAA'}
        Assert ($registry -contains $expectedKey) "$Game independent registration key"
        $launcher=if($Game -eq 'bs2'){'Lanzador BioShock 2 VR DLSS-DLAA.exe'}else{'Lanzador BioShock VR DLSS-DLAA.exe'}
        $shortcutBase=if($Game -eq 'bs2'){'BioShock 2 VR DLSS-DLAA'}else{'BioShock VR DLSS-DLAA'}
        $shortcuts=@(Rows $db 'SELECT `Name`, `Target` FROM `Shortcut`' 2)
        Assert ($shortcuts.Count -eq 1 -and $shortcuts[0][0].Split('|')[-1] -eq ($shortcutBase+' '+$Version) -and $shortcuts[0][1] -eq ('[GAMEDIR]'+$launcher)) "$Game shortcut targets launcher inside selected game"
        $files=@(Rows $db 'SELECT `FileName` FROM `File`' 1 | ForEach-Object { $_[0].Split('|')[-1] })
        Assert ($files.Count -eq 23 -and -not @($files | Where-Object { $_ -match '^Bioshock(2)?HD\.exe$' }).Count) "$Game explicit 23-file payload, no game executable"
        $retired=@(Rows $db 'SELECT `FileName` FROM `RemoveFile`' 1 | ForEach-Object { $_[0].Split('|')[-1] })
        Assert ($retired -contains ($shortcutBase+'.lnk') -and -not @($retired | Where-Object { $_ -match '[*?]' }).Count) "$Game retirement has exact names and no wildcard"
        $help=@(Rows $db 'SELECT `Text` FROM `Control` WHERE `Dialog_` = ''BioShockExitDlg'' AND `Control` = ''FolderHelp''' 1)
        Assert ($help.Count -eq 1 -and $help[0][0].Contains($launcher)) "$Game success screen names the right launcher"
        [pscustomobject]@{ product=$properties.ProductCode; upgrade=$properties.UpgradeCode; components=$components; registry=$registry }
    } finally { $db.Dispose() }
}
Assert ((Get-FileHash -LiteralPath $bundle).Hash -eq $bundleManifest.sha256) 'EXE matches its manifest'
Assert ((Get-FileHash -LiteralPath $BioShock1Msi).Hash -eq '2A5365E332DC311C0E010AD0BECEEAB34E905F66C6F40AB5CF44824EDAD9F660') 'BS1 exact accepted 0.2.11 MSI'
Assert ($bs2.gameId -eq 'bs2' -and -not $bs2.testFamily -and (Get-FileHash -LiteralPath $msi2).Hash -eq $bs2.sha256) 'BS2 candidate matches its non-test manifest'
$one = Inspect $BioShock1Msi 'bs1' '0.2.11'
$two = Inspect $msi2 'bs2' '0.2.13'
Assert ($one.product -ne $two.product -and $one.upgrade -ne $two.upgrade) 'ProductCode and UpgradeCode differ between games'
Assert (-not @($two.components|Where-Object{$one.components -contains $_}).Count) 'No component GUID is shared between games'
Assert (-not @($two.registry|Where-Object{$one.registry -contains $_}).Count) 'Registration keys do not overlap'
$report=Join-Path $folder 'embedded-final-verification.txt'
$verify=Start-Process -FilePath $bundle -ArgumentList ('--verify "'+$report+'"') -WindowStyle Hidden -PassThru
Assert ($verify.WaitForExit(45000) -and $verify.ExitCode -eq 0) 'Both embedded MSI resources verify in final EXE'
$verify.Dispose()
$window=Start-Process -FilePath $bundle -ArgumentList '--window-test' -WindowStyle Hidden -PassThru
Start-Sleep -Milliseconds 700
Assert (-not $window.HasExited) 'Selector starts and remains running'
Assert ($window.WaitForExit(10000) -and $window.ExitCode -eq 0) 'Exact selector test instance closes itself safely'
$window.Dispose()
$preview=Join-Path $folder 'selector-final.png'
$draw=Start-Process -FilePath $bundle -ArgumentList ('--preview "'+$preview+'"') -WindowStyle Hidden -PassThru
Assert ($draw.WaitForExit(10000) -and $draw.ExitCode -eq 0 -and (Test-Path -LiteralPath $preview)) 'Selector preview generated without installation'
$draw.Dispose()
$output=Join-Path $folder 'verification.json'
[IO.File]::WriteAllText($output,([ordered]@{passed=@($checks);installed=$false;bs1=$one;bs2=$two;bundleSha256=$bundleManifest.sha256}|ConvertTo-Json -Depth 5),(New-Object Text.UTF8Encoding($false)))
Write-Output ('Informe: '+$output)
