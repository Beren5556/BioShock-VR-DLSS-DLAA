[CmdletBinding()]
param(
    [ValidatePattern('^\d+\.\d+\.\d+$')][string]$Version = '0.2.13',
    [ValidateSet('bs1','bs2')][string]$GameId = 'bs2',
    [Parameter(Mandatory=$true)][string]$BasePayloadDirectory,
    [string]$BuildToolsDirectory = '',
    [switch]$SkipLauncherBuild,
    [ValidatePattern('^(|[a-f0-9]{32})$')][string]$TestFamily = ''
)
$ErrorActionPreference = 'Stop'
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
# A delivered ProductCode must never get a replacement package under the same
# version: normal double-click then fails with MSI 1638 before our UI can run.
# Keep the delivered artifact immutable; changed code/UI requires a new version.
if (-not $TestFamily -and (Test-Path -LiteralPath (Join-Path $repoRoot "release\SHA256SUMS-v$Version.txt"))) {
    throw "La versión $Version ya se ha entregado. Usa una versión nueva para que Windows Installer pueda actualizarla sin error 1638."
}
$releaseRoot = if ($GameId -eq 'bs2') { Join-Path $repoRoot ("artifacts\integration-" + $Version + "\msi\bs2") } else { Join-Path $repoRoot ('artifacts\stable-' + $Version) }
$buildRoot = if ($GameId -eq 'bs2') { Join-Path $repoRoot ("artifacts\integration-" + $Version + "\build") } else { Join-Path $releaseRoot 'build' }
$displayName = if ($GameId -eq 'bs2') { 'BioShock 2' } else { 'BioShock' }
$launcherName = 'Lanzador ' + $displayName + ' VR DLSS-DLAA.exe'
$shortcutBase = $displayName + ' VR DLSS-DLAA'
$outputRoot = if ($TestFamily) { Join-Path $repoRoot ("artifacts\msi-isolated\$TestFamily\$Version") } else { $releaseRoot }
$work = Join-Path $outputRoot ('msi-build-' + $Version)
$stage = Join-Path $work 'payload'
$toolsRoot = if ($BuildToolsDirectory) { [IO.Path]::GetFullPath($BuildToolsDirectory) } else { Join-Path $repoRoot 'artifacts\msi-tools' }
$utf8 = New-Object System.Text.UTF8Encoding($false)
New-Item -ItemType Directory -Path $stage -Force | Out-Null

if (-not $SkipLauncherBuild) { & (Join-Path $repoRoot 'apps\launcher\Build-Launcher.ps1') -Game $GameId }
if (-not (Test-Path -LiteralPath (Join-Path $toolsRoot 'wix.6.0.2\tools\net6.0\any\wix.dll')) -or
    -not (Test-Path -LiteralPath (Join-Path $toolsRoot 'WiX-6.0.2-source.zip'))) {
    throw 'Faltan las herramientas WiX verificadas. Ejecuta Acquire-BuildTools.ps1 o proporciona BuildToolsDirectory.'
}

$cache = [IO.File]::ReadAllText((Join-Path $buildRoot 'CMakeCache.txt'))
foreach ($flag in @('BVR_DLSS_OVERLAP','BVR_DEPTH_COPY_REUSE','BVR_DLSS_TAIL_OVERLAP','BVR_DLSS_EARLY_DELIVERY')) {
    if ($cache -notmatch "(?m)^${flag}:BOOL=ON\r?$") { throw "No se empaqueta una DLL sin la optimización probada $flag." }
}
foreach ($flag in @('BVR_PERFORMANCE_PROBE','BVR_LATENCY_PROBE','BVR_CRITICAL_PATH_PROBE','BVR_BS2_TEST_ISOLATION')) {
    if ($cache -notmatch "(?m)^${flag}:BOOL=OFF\r?$") { throw "La distribución estable no debe activar $flag." }
}
$stamp = [IO.File]::ReadAllText((Join-Path $buildRoot 'generated\bvr_version.h'))
if (-not $stamp.Contains(('"' + $Version + '"'))) { throw "La DLL no procede de la compilación $Version." }
$buildId = [regex]::Match($stamp, '#define BVR_BUILD_ID\s+"([^"]+)"').Groups[1].Value
$registryPath = if ($GameId -eq 'bs2') { 'Software\Beren5556\BioShock2VRDLSSDLAA' } else { 'Software\Beren5556\BioShockVRDLSSDLAA' }
if ($TestFamily) { $registryPath = 'Software\Beren5556\BioShockVRInstallerTests\' + $TestFamily }

$items = New-Object 'System.Collections.Generic.List[object]'
function Add-Payload([string]$Source, [string]$Destination, [string]$Expected = '') {
    if (-not (Test-Path -LiteralPath $Source -PathType Leaf)) { throw "Falta $Source" }
    $hash = (Get-FileHash -LiteralPath $Source -Algorithm SHA256).Hash
    if ($Expected -and $hash -ne $Expected) { throw "Ha cambiado un componente base: $Destination" }
    $target = Join-Path $stage $Destination
    New-Item -ItemType Directory -Path (Split-Path -Parent $target) -Force | Out-Null
    Copy-Item -LiteralPath $Source -Destination $target -Force
    $items.Add([pscustomobject]@{ Path = $Destination; Sha256 = $hash; Staged = $target })
}
# The accepted 0.2.11 inventory is the only source for shared redistributables.
# Verify every source first, including files subsequently replaced by candidates.
$baseManifest = Get-Content -LiteralPath (Join-Path $repoRoot 'release\manifest-v0.2.11.json') -Raw | ConvertFrom-Json
foreach ($entry in $baseManifest.files) {
    $baseFile = Join-Path $BasePayloadDirectory $entry.path
    if (-not (Test-Path -LiteralPath $baseFile -PathType Leaf) -or
        (Get-FileHash -LiteralPath $baseFile -Algorithm SHA256).Hash -ne $entry.sha256) {
        throw "El payload base aceptado ha cambiado: $($entry.path)"
    }
}
foreach ($entry in $baseManifest.files) {
    # The accepted manifest uses '/', while game-specific overrides use '\'.
    # Normalize before dispatch so BS2 never inherits BS1 host capabilities.
    $destination = $entry.path.Replace('/', '\')
    if ($destination -eq 'bioshockvr.dll' -or ($GameId -eq 'bs2' -and $destination -eq 'xinput1_3.dll')) {
        Add-Payload (Join-Path $buildRoot ('src\Release\' + $destination)) $destination
    } elseif ($destination -eq 'Lanzador BioShock VR DLSS-DLAA.exe') {
        Add-Payload (Join-Path $repoRoot ("artifacts\integration-" + $Version + "\launcher\" + $GameId + "\" + $launcherName)) $launcherName
    } elseif ($GameId -eq 'bs2' -and $destination -eq 'host64\dlss-capabilities.ini') {
        Add-Payload (Join-Path $repoRoot 'installer\profiles\bs2\dlss-capabilities.ini') $destination
    } elseif ($destination -eq 'BioShockVR-DLSS45\LEEME-DLSS45.md') {
        Add-Payload (Join-Path $repoRoot ("docs\releases\v" + $Version + ".md")) $destination
    } elseif ($GameId -eq 'bs2' -and $destination -eq 'BioShockVR-DLSS45\INFORMACION-DEL-PAQUETE.txt') {
        Add-Payload (Join-Path $repoRoot 'installer\profiles\bs2\INFORMACION-DEL-PAQUETE.txt') $destination
    } else {
        Add-Payload (Join-Path $BasePayloadDirectory $destination) $destination $entry.sha256
    }
}

function Stable-Guid([string]$Value) {
    $sha = [Security.Cryptography.SHA256]::Create()
    try { $bytes = $sha.ComputeHash([Text.Encoding]::UTF8.GetBytes('BioShockVR-DLSS-DLAA-MSI:' + $(if ($GameId -eq 'bs2') { 'bs2:' } else { '' }) + $TestFamily + $(if ($TestFamily) { ':' } else { '' }) + $Value)) }
    finally { $sha.Dispose() }
    $guidBytes = New-Object byte[] 16
    [Array]::Copy($bytes, $guidBytes, 16)
    return (New-Object Guid (,$guidBytes)).ToString('D').ToUpperInvariant()
}

# Generated inputs derive only from the explicit verified payload, never the whole game folder.
$plan = New-Object Text.StringBuilder
[void]$plan.AppendLine('namespace BioShockMsi { internal static class PayloadPlan {')
[void]$plan.AppendLine(('internal const string Version = "{0}";' -f $Version))
[void]$plan.AppendLine(('internal const string RegistryPath = @"{0}";' -f $registryPath))
[void]$plan.AppendLine(('internal const string TestFamily = "{0}";' -f $TestFamily))
[void]$plan.AppendLine('internal static readonly string[][] Items = {')
foreach ($item in $items) {
    [void]$plan.AppendLine(('new string[] {{ @"{0}", "{1}" }},' -f $item.Path, $item.Sha256))
}
[void]$plan.AppendLine('}; } }')
$planPath = Join-Path $work 'PayloadPlan.g.cs'
[IO.File]::WriteAllText($planPath, $plan.ToString(), $utf8)

$ns = 'http://wixtoolset.org/schemas/v4/wxs'
$xml = New-Object Xml.XmlDocument
$wix = $xml.CreateElement('Wix', $ns)
[void]$xml.AppendChild($wix)
$fragment = $xml.CreateElement('Fragment', $ns)
[void]$wix.AppendChild($fragment)
$rootDirectory = $xml.CreateElement('DirectoryRef', $ns)
$rootDirectory.SetAttribute('Id', 'GAMEDIR')
[void]$fragment.AppendChild($rootDirectory)
$directories = @{ '' = $rootDirectory }
$directoryIds = @{ '' = 'GAMEDIR' }
foreach ($item in $items) {
    $relative = $item.Path.Replace('/', '\')
    $folder = [IO.Path]::GetDirectoryName($relative)
    if ($folder) {
        $current = ''
        foreach ($segment in $folder.Split('\')) {
            $parent = $current
            $current = if ($current) { $current + '\' + $segment } else { $segment }
            if (-not $directories.ContainsKey($current)) {
                $node = $xml.CreateElement('Directory', $ns)
                $id = 'Dir_' + (Stable-Guid $current).Replace('-', '')
                $node.SetAttribute('Id', $id)
                $node.SetAttribute('Name', $segment)
                [void]$directories[$parent].AppendChild($node)
                $directories[$current] = $node
                $directoryIds[$current] = $id
            }
        }
    }
}
$group = $xml.CreateElement('ComponentGroup', $ns)
$group.SetAttribute('Id', 'ModFiles')
[void]$fragment.AppendChild($group)
$index = 0
foreach ($item in $items) {
    $relative = $item.Path.Replace('/', '\')
    $folder = [IO.Path]::GetDirectoryName($relative)
    if (-not $folder) { $folder = '' }
    $component = $xml.CreateElement('Component', $ns)
    $component.SetAttribute('Id', 'Cmp' + $index)
    $component.SetAttribute('Guid', (Stable-Guid $relative))
    $component.SetAttribute('Directory', $directoryIds[$folder])
    $file = $xml.CreateElement('File', $ns)
    $file.SetAttribute('Id', 'File' + $index)
    $file.SetAttribute('Source', $item.Staged)
    $file.SetAttribute('Name', [IO.Path]::GetFileName($relative))
    $file.SetAttribute('KeyPath', 'yes')
    [void]$component.AppendChild($file)
    # Replace exactly this payload file even when an older mod shares its PE version.
    # BackupFiles runs before RemoveFiles; rollback restores the full prior snapshot.
    $remove = $xml.CreateElement('RemoveFile', $ns)
    $remove.SetAttribute('Id', 'Replace' + $index)
    $remove.SetAttribute('Name', [IO.Path]::GetFileName($relative))
    $remove.SetAttribute('On', 'install')
    [void]$component.AppendChild($remove)
    [void]$group.AppendChild($component)
    $index++
}
$filesPath = Join-Path $work 'Files.g.wxs'
$xml.Save($filesPath)

$dtf = Join-Path $toolsRoot 'wixtoolset.dtf.windowsinstaller.6.0.2\lib\net20\WixToolset.Dtf.WindowsInstaller.dll'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$managed = Join-Path $work 'BioShockMsiActions.dll'
$gameDefine = if ($GameId -eq 'bs2') { '/define:BIOSHOCK2' } else { '/define:BIOSHOCK1' }
& $compiler $gameDefine /nologo /target:library /platform:anycpu /optimize+ /codepage:65001 ("/out:$managed") /reference:System.dll /reference:System.Core.dll /reference:System.Xml.dll ("/reference:$dtf") (Join-Path $PSScriptRoot 'MsiActions.cs') (Join-Path $PSScriptRoot 'MsiStorage.cs') (Join-Path $PSScriptRoot 'GamePackage.cs') (Join-Path $PSScriptRoot 'LegacyMigration.cs') $planPath
if ($LASTEXITCODE -ne 0) { throw 'No se han compilado las acciones MSI.' }
$actions = Join-Path $work 'BioShockMsiActions.CA.dll'
$make = Join-Path $toolsRoot 'wixtoolset.dtf.customaction.6.0.2\tools\WixToolset.Dtf.MakeSfxCA.exe'
$sfx = Join-Path $toolsRoot 'wixtoolset.dtf.customaction.6.0.2\tools\x64\SfxCA.dll'
& $make $actions $sfx $managed $dtf (Join-Path $PSScriptRoot 'CustomAction.config')
if ($LASTEXITCODE -ne 0) { throw 'No se han empaquetado las acciones MSI.' }

$output = Join-Path $outputRoot (($displayName -replace ' ','-') + '-VR-DLSS-DLAA-' + $Version + '.msi')
$wixTool = Join-Path $toolsRoot 'wix.6.0.2\tools\net6.0\any\wix.dll'
$ui = Join-Path $toolsRoot 'wixtoolset.ui.wixext.6.0.2\wixext6\WixToolset.UI.wixext.dll'
$arguments = @($wixTool, 'build', (Join-Path $PSScriptRoot 'Package.wxs'), (Join-Path $PSScriptRoot 'Interface.wxs'), $filesPath,
    '-ext', $ui, '-arch', 'x64', '-culture', 'es-es', '-d', "Version=$Version", '-d', "RepoRoot=$repoRoot", '-d', "ActionsDll=$actions",
    '-d', ('ProductCode=' + (Stable-Guid ('product:' + $Version))),
    '-d', ('UpgradeCode=' + $(if ($TestFamily) { Stable-Guid 'test-upgrade' } elseif ($GameId -eq 'bs2') { Stable-Guid 'upgrade' } else { '4C28DEFC-3BB8-48C0-BB6E-4D5108119CE7' })),
    '-d', ('RegistrationGuid=' + $(if ($TestFamily) { Stable-Guid 'test-registration' } elseif ($GameId -eq 'bs2') { Stable-Guid 'registration' } else { '273CDA46-4A9B-4DD9-B2C8-A8E8D31E95F3' })),
    '-d', ('RegistryPath=' + $registryPath), '-d', ('DisplayName=' + $displayName),
    '-d', ('LauncherName=' + $launcherName), '-d', ('ShortcutBase=' + $shortcutBase),
    '-d', ('ShortcutComponentGuid=' + (Stable-Guid ('desktop-shortcut:' + $Version))), '-o', $output)
& dotnet @arguments
if ($LASTEXITCODE -ne 0) { throw 'No se ha generado el MSI.' }
$manifest = [ordered]@{
    gameId = $GameId
    version = $Version
    productCode = (Stable-Guid ('product:' + $Version))
    installer = [IO.Path]::GetFileName($output)
    sha256 = (Get-FileHash -LiteralPath $output -Algorithm SHA256).Hash
    launcherAutoStart = $false
    desktopShortcut = $shortcutBase + ' ' + $Version + '.lnk'
    baseMod = 'BioShock VR v0.8.2'
    modBuild = $buildId
    testFamily = $TestFamily
    registration = 'HKCU:\' + $registryPath
    runtime = '310.7.0.0'
    files = @($items | ForEach-Object { [ordered]@{ path = $_.Path; sha256 = $_.Sha256 } })
}
[IO.File]::WriteAllText((Join-Path $outputRoot ('manifest-' + $Version + '.json')), ($manifest | ConvertTo-Json -Depth 6), $utf8)
Write-Output "Creado: $output"
Write-Output "SHA-256: $($manifest.sha256)"
