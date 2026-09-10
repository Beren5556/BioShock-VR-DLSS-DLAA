[CmdletBinding()]
param(
    [ValidatePattern('^\d+\.\d+\.\d+$')][string]$Version = '0.2.13',
    [Parameter(Mandatory=$true)][string]$Bs1PayloadDirectory,
    [Parameter(Mandatory=$true)][string]$Bs2PayloadDirectory,
    [Parameter(Mandatory=$true)][string]$Bs2ManifestPath,
    [Parameter(Mandatory=$true)][string]$BuildToolsDirectory,
    [ValidatePattern('^(|[a-f0-9]{32})$')][string]$TestFamily = '',
    [ValidateSet('','bs1','bs2')][string]$TestPredecessor = ''
)
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$tools = [IO.Path]::GetFullPath($BuildToolsDirectory)
if ($TestPredecessor -and -not $TestFamily) { throw 'Un predecesor simulado solo se construye en una familia de prueba.' }
if (-not $TestFamily -and (Test-Path -LiteralPath (Join-Path $repo "release\SHA256SUMS-v$Version.txt"))) {
    throw 'Versión entregada e inmutable: utiliza una versión nueva.'
}
$kind = if ($TestPredecessor) { 'predecessor-' + $TestPredecessor } else { 'combined' }
$outputRoot = if ($TestFamily) { Join-Path $repo "artifacts\combined-isolated\$TestFamily\$kind-$Version" } else { Join-Path $repo "artifacts\integration-$Version\msi\combined" }
$work = Join-Path $outputRoot 'work'
New-Item -ItemType Directory -Path $work -Force | Out-Null
$utf8 = New-Object Text.UTF8Encoding($false)
$dtf = Join-Path $tools 'wixtoolset.dtf.windowsinstaller.6.0.2\lib\net20\WixToolset.Dtf.WindowsInstaller.dll'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$make = Join-Path $tools 'wixtoolset.dtf.customaction.6.0.2\tools\WixToolset.Dtf.MakeSfxCA.exe'
$sfx = Join-Path $tools 'wixtoolset.dtf.customaction.6.0.2\tools\x64\SfxCA.dll'
$shared = Join-Path $repo 'installer\msi'
function Write-Generated([string]$Path, [string]$Content) { [IO.File]::WriteAllText($Path, $Content, $utf8) }
function Stable-Guid([string]$Identity, [string]$Value) {
    # Keep predecessor component GUIDs for the SAME key paths. This is required
    # by an afterInstallExecute major upgrade; both products briefly coexist.
    $prefix = 'BioShockVR-DLSS-DLAA-MSI:' + $(if ($Identity -eq 'bs1') { '' } else { $Identity + ':' })
    $inputText = $prefix + $TestFamily + $(if ($TestFamily) { ':' } else { '' }) + $Value
    $sha = [Security.Cryptography.SHA256]::Create()
    try { $bytes = $sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($inputText)) } finally { $sha.Dispose() }
    $guidBytes = New-Object byte[] 16
    [Array]::Copy($bytes, $guidBytes, 16)
    (New-Object Guid (,$guidBytes)).ToString('D').ToUpperInvariant()
}
function Compile-Actions([string]$Name, [string[]]$Sources, [string]$Defines) {
    $managed = Join-Path $work ($Name + '.dll')
    $args = @('/nologo','/target:library','/platform:anycpu','/optimize+','/codepage:65001',"/out:$managed",
        '/reference:System.dll','/reference:System.Core.dll','/reference:System.Xml.dll',"/reference:$dtf")
    if ($Defines) { $args += '/define:' + $Defines }
    & $compiler @args @Sources | Write-Host
    if ($LASTEXITCODE -ne 0) { throw "Error compilando $Name" }
    $actions = Join-Path $work ($Name + '.CA.dll')
    & $make $actions $sfx $managed $dtf (Join-Path $shared 'CustomAction.config') | Write-Host
    if ($LASTEXITCODE -ne 0) { throw "Error empaquetando $Name" }
    return $actions
}
$manifests = @{
    bs1 = (Get-Content -LiteralPath (Join-Path $repo 'release\manifest-v0.2.11.json') -Raw | ConvertFrom-Json)
    bs2 = (Get-Content -LiteralPath $Bs2ManifestPath -Raw | ConvertFrom-Json)
}
if ($manifests.bs2.gameId -ne 'bs2' -or $manifests.bs2.version -ne '0.2.13' -or $manifests.bs2.testFamily) { throw 'Se requiere el payload BS2 candidato 0.2.13, no uno aislado.' }
if ((Get-FileHash -LiteralPath (Join-Path (Split-Path $Bs2ManifestPath) $manifests.bs2.installer)).Hash -ne $manifests.bs2.sha256) { throw 'MSI fuente de BS2 distinto de su manifiesto.' }
$sources = @{ bs1 = [IO.Path]::GetFullPath($Bs1PayloadDirectory); bs2 = [IO.Path]::GetFullPath($Bs2PayloadDirectory) }
$games = [ordered]@{}
$inputs = New-Object 'System.Collections.Generic.List[string]'
foreach ($id in @('bs1','bs2')) {
    $upper = $id.ToUpperInvariant()
    $display = if ($id -eq 'bs1') { 'BioShock' } else { 'BioShock 2' }
    $launcher = 'Lanzador ' + $display + ' VR DLSS-DLAA.exe'
    $shortcut = $display + ' VR DLSS-DLAA'
    $registry = if ($id -eq 'bs1') { 'Software\Beren5556\BioShockVRDLSSDLAA' } else { 'Software\Beren5556\BioShock2VRDLSSDLAA' }
    if ($TestFamily) { $registry = "Software\Beren5556\BioShockVRInstallerTests\$TestFamily\$id" }
    $upgrade = if ($TestFamily) { Stable-Guid $id 'test-upgrade' } elseif ($id -eq 'bs1') { '4C28DEFC-3BB8-48C0-BB6E-4D5108119CE7' } else { Stable-Guid $id 'upgrade' }
    $regGuid = if ($TestFamily) { Stable-Guid $id 'test-registration' } elseif ($id -eq 'bs1') { '273CDA46-4A9B-4DD9-B2C8-A8E8D31E95F3' } else { Stable-Guid $id 'registration' }
    $items = New-Object 'System.Collections.Generic.List[object]'
    if (@($manifests[$id].files).Count -ne 23) { throw "$id no contiene el inventario de 23 archivos esperado." }
    $seen = @{}
    foreach ($file in $manifests[$id].files) {
        $relative = $file.path.Replace('/', '\')
        $source = [IO.Path]::GetFullPath((Join-Path $sources[$id] $relative))
        if ($seen.ContainsKey($relative) -or $relative.Contains(':') -or
            -not $source.StartsWith($sources[$id].TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Ruta de payload no válida.' }
        $seen[$relative] = $true
        if ((Get-FileHash -LiteralPath $source).Hash -ne $file.sha256) { throw "Payload $id distinto: $relative" }
        $staged = Join-Path $work ("payload\$id\" + $relative)
        New-Item -ItemType Directory -Path (Split-Path $staged) -Force | Out-Null
        Copy-Item -LiteralPath $source -Destination $staged -Force
        $items.Add([pscustomobject]@{ path = $relative; sha256 = $file.sha256; staged = $staged })
    }
    $plan = New-Object Text.StringBuilder
    [void]$plan.AppendLine('namespace BioShockMsi { internal static class PayloadPlan {')
    [void]$plan.AppendLine(('internal const string Version = "{0}"; internal const string RegistryPath = @"{1}"; internal const string TestFamily = "{2}";' -f $Version,$registry,$TestFamily))
    [void]$plan.AppendLine('internal static readonly string[][] Items = {')
    foreach ($item in $items) { [void]$plan.AppendLine(('new string[] {{ @"{0}", "{1}" }},' -f $item.path,$item.sha256)) }
    [void]$plan.AppendLine('}; } }')
    $planPath = Join-Path $work ("Payload_$id.g.cs")
    Write-Generated $planPath $plan.ToString()
    $caSources = @('MsiActions.cs','MsiStorage.cs','GamePackage.cs','LegacyMigration.cs') | ForEach-Object { Join-Path $shared $_ }
    $caSources += $planPath
    $define = if ($id -eq 'bs2') { 'BIOSHOCK2,COMBINED_MSI' } else { 'BIOSHOCK1,COMBINED_MSI' }
    $actions = Compile-Actions "Actions_$id" $caSources $define

    $xml = New-Object Xml.XmlDocument
    $ns = 'http://wixtoolset.org/schemas/v4/wxs'
    $wix = $xml.CreateElement('Wix', $ns); [void]$xml.AppendChild($wix)
    $fragment = $xml.CreateElement('Fragment', $ns); [void]$wix.AppendChild($fragment)
    $rootDir = $xml.CreateElement('DirectoryRef', $ns); $rootDir.SetAttribute('Id', $upper + 'DIR'); [void]$fragment.AppendChild($rootDir)
    $dirs = @{ '' = $rootDir }; $dirIds = @{ '' = $upper + 'DIR' }
    foreach ($item in $items) {
        $folder = [IO.Path]::GetDirectoryName($item.path)
        $current = ''
        if ($folder) { foreach ($segment in $folder.Split('\')) {
            $parent = $current
            $current = if ($current) { $current + '\' + $segment } else { $segment }
            if (-not $dirs.ContainsKey($current)) {
                $node = $xml.CreateElement('Directory', $ns)
                $dirId = 'Dir_' + $id + '_' + (Stable-Guid $id $current).Replace('-','')
                $node.SetAttribute('Id', $dirId); $node.SetAttribute('Name', $segment)
                [void]$dirs[$parent].AppendChild($node); $dirs[$current] = $node; $dirIds[$current] = $dirId
            }
        } }
    }
    $group = $xml.CreateElement('ComponentGroup', $ns); $group.SetAttribute('Id', 'Files_' + $id); [void]$fragment.AppendChild($group)
    $index = 0
    foreach ($item in $items) {
        $folder = [IO.Path]::GetDirectoryName($item.path); if (-not $folder) { $folder = '' }
        $component = $xml.CreateElement('Component', $ns)
        $component.SetAttribute('Id', "Cmp_${id}_$index"); $component.SetAttribute('Guid', (Stable-Guid $id $item.path)); $component.SetAttribute('Directory', $dirIds[$folder])
        $file = $xml.CreateElement('File', $ns)
        $file.SetAttribute('Id', "File_${id}_$index"); $file.SetAttribute('Source', $item.staged); $file.SetAttribute('Name', [IO.Path]::GetFileName($item.path)); $file.SetAttribute('KeyPath', 'yes')
        [void]$component.AppendChild($file)
        $remove = $xml.CreateElement('RemoveFile', $ns)
        $remove.SetAttribute('Id', "Replace_${id}_$index"); $remove.SetAttribute('Name', [IO.Path]::GetFileName($item.path)); $remove.SetAttribute('On','install')
        [void]$component.AppendChild($remove); [void]$group.AppendChild($component)
        $index++
    }
    $filesPath = Join-Path $work ("Files_$id.g.wxs"); $xml.Save($filesPath); $inputs.Add($filesPath)
    $tokens = @{
        Game=$id; Upper=$upper; DisplayName=$display; Launcher=$launcher; Shortcut=$shortcut
        Registry=$registry; RegistrationGuid=$regGuid; Upgrade=$upgrade; Actions=$actions
        ShortcutGuid=(Stable-Guid $id ("desktop-shortcut:" + $Version))
        DetectBefore=$(if ($id -eq 'bs1') { 'DetectGame_bs2' } else { 'CostFinalize' })
        PrepareBefore=$(if ($id -eq 'bs1') { 'Prepare_bs2' } else { 'InstallValidate' })
        BackupBefore=$(if ($id -eq 'bs1') { 'RollbackFiles_bs2' } else { 'RemoveShortcuts' })
        FinishAfter=$(if ($id -eq 'bs1') { 'CreateShortcuts' } else { 'FinishFiles_bs1' })
        CommitBefore=$(if ($id -eq 'bs1') { 'CommitFiles_bs2' } else { 'InstallFinalize' })
        Next=$(if ($id -eq 'bs1') {
            '<Publish Event="NewDialog" Value="Folder_bs2" Order="5" Condition="BVR_VALID = &quot;1&quot; AND BVR_BS2_SELECTED = &quot;1&quot;" /><Publish Event="NewDialog" Value="SuiteReadyDlg" Order="6" Condition="BVR_VALID = &quot;1&quot; AND BVR_BS2_SELECTED &lt;&gt; &quot;1&quot;" />'
        } else { '<Publish Event="NewDialog" Value="SuiteReadyDlg" Order="5" Condition="BVR_VALID = &quot;1&quot;" />' })
    }
    foreach ($template in @('Game','Folder')) {
        $content = Get-Content -LiteralPath (Join-Path $PSScriptRoot ($template + '.wxs.in')) -Raw
        foreach ($token in $tokens.Keys) { $content = $content.Replace('@' + $token + '@', [Security.SecurityElement]::Escape([string]$tokens[$token])) }
        # Next is an XML element, not an attribute.
        $escapedNext = [Security.SecurityElement]::Escape([string]$tokens.Next)
        $content = $content.Replace($escapedNext, [string]$tokens.Next)
        if ($TestPredecessor -eq $id -and $template -eq 'Game') {
            $content = [regex]::Replace($content, '(?s)    <Upgrade .*?</Upgrade>', '')
        }
        $path = Join-Path $work ("${template}_$id.g.wxs")
        Write-Generated $path $content; $inputs.Add($path)
    }
    $games[$id] = [ordered]@{
        registration = 'HKCU:\' + $registry; legacyUpgradeCode = $upgrade
        sourceVersion = $manifests[$id].version; launcher = $launcher
        files = @($items | Select-Object path,sha256)
    }
}
$suitePlan = 'namespace BioShockSuite { internal static class SuitePlan { internal const string TestFamily = "' + $TestFamily +
    '"; internal const string Bs1Registry = @"' + $games.bs1.registration.Substring(6) +
    '"; internal const string Bs2Registry = @"' + $games.bs2.registration.Substring(6) + '"; } }'
$suitePlanPath = Join-Path $work 'SuitePlan.g.cs'; Write-Generated $suitePlanPath $suitePlan
$suiteActions = Compile-Actions 'SuiteActions' @((Join-Path $PSScriptRoot 'SuiteActions.cs'), $suitePlanPath) ''
$upgradeCode = if ($TestPredecessor) { $games[$TestPredecessor].legacyUpgradeCode } else { Stable-Guid 'combined' 'upgrade' }
$productCode = Stable-Guid 'combined' ("product:${kind}:$Version")
$output = Join-Path $outputRoot ("BioShock-1-2-VR-DLSS-DLAA-$Version-CANDIDATO.msi")
$args = @((Join-Path $tools 'wix.6.0.2\tools\net6.0\any\wix.dll'), 'build',
    (Join-Path $PSScriptRoot 'Package.wxs'), (Join-Path $PSScriptRoot 'Interface.wxs')) + @($inputs) +
    @('-ext', (Join-Path $tools 'wixtoolset.ui.wixext.6.0.2\wixext6\WixToolset.UI.wixext.dll'),
      '-arch','x64','-culture','es-es','-d',"Version=$Version",'-d',"RepoRoot=$repo",'-d',"SuiteActionsDll=$suiteActions",
      '-d',"ProductCode=$productCode",'-d',"UpgradeCode=$upgradeCode",'-o',$output)
& dotnet @args
if ($LASTEXITCODE -ne 0) { throw 'No se ha generado el MSI conjunto.' }
# WiX 6 Scope=perUser also sets the SummaryInfo "no elevation required"
# bit. Keep per-user ownership/migration, but allow Windows Installer to
# request UAC: native registration rollback needs elevation on this host.
# This changes only the package metadata, never Windows security policy.
# Do this BEFORE hashing/signing the final package.
Add-Type -Path $dtf
$summary = New-Object WixToolset.Dtf.WindowsInstaller.SummaryInfo($output, $true)
try {
    $summary.WordCount = $summary.WordCount -band (-bnot 8)
    $summary.Persist()
} finally { $summary.Dispose() }
$manifest = [ordered]@{
    kind='native-combined-msi'; version=$Version; installer=[IO.Path]::GetFileName($output)
    sha256=(Get-FileHash -LiteralPath $output).Hash; productCode=$productCode; upgradeCode=$upgradeCode
    testFamily=$TestFamily; testPredecessor=$TestPredecessor; games=$games; launcherAutoStart=$false
    installScope='perUser'; elevation='required-capable'; summaryNoElevationBit=$false
}
Write-Generated (Join-Path $outputRoot 'manifest.json') ($manifest | ConvertTo-Json -Depth 9)
if ($TestFamily) {
    Copy-Item -LiteralPath $dtf -Destination $work -Force
    & $compiler /nologo /target:exe /codepage:65001 ("/out:" + (Join-Path $work 'Test-Selection.exe')) /reference:System.dll ("/reference:$dtf") (Join-Path $PSScriptRoot 'Test-Selection.cs')
    if ($LASTEXITCODE -ne 0) { throw 'No se ha compilado la prueba de selección nativa.' }
    $fixtureSources = @('MsiActions.cs','MsiStorage.cs','GamePackage.cs','LegacyMigration.cs','LegacyMigrationTests.cs') | ForEach-Object { Join-Path $shared $_ }
    & $compiler /nologo /target:library /define:BIOSHOCK2,COMBINED_MSI /codepage:65001 ("/out:" + (Join-Path $work 'LegacyFixtures.dll')) /reference:System.dll /reference:System.Core.dll /reference:System.Xml.dll ("/reference:$dtf") @fixtureSources (Join-Path $work 'Payload_bs2.g.cs')
    if ($LASTEXITCODE -ne 0) { throw 'No se ha compilado el fixture de migración de la beta.' }
}
Write-Output "Creado: $output"
Write-Output "SHA-256: $($manifest.sha256)"
