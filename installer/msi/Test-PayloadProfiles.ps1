[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$BasePayloadDirectory,
    [string]$InstalledBs2Directory = ''
)
$ErrorActionPreference = 'Stop'
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$acceptedManifest = Get-Content -LiteralPath (Join-Path $repoRoot 'release\manifest-v0.2.11.json') -Raw | ConvertFrom-Json
$checks = 0
function Assert-Profile([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw $Message }
    $script:checks++
}

# Execute only the real source-selection loop, with Add-Payload replaced by a
# read-only recorder. No MSI, staging files or Windows registrations are made.
$tokens = $null
$parseErrors = $null
$ast = [Management.Automation.Language.Parser]::ParseFile(
    (Join-Path $PSScriptRoot 'Build-Msi.ps1'), [ref]$tokens, [ref]$parseErrors)
Assert-Profile ($parseErrors.Count -eq 0) 'Build-Msi.ps1 has syntax errors.'
$loops = @($ast.FindAll({ param($node)
    $node -is [Management.Automation.Language.ForEachStatementAst] -and
    $node.Condition.Extent.Text -eq '$baseManifest.files' -and
    $node.Body.Extent.Text.Contains('Add-Payload')
}, $false))
Assert-Profile ($loops.Count -eq 1) 'Cannot identify the payload-selection loop unambiguously.'
$selectionLoop = [scriptblock]::Create($loops[0].Extent.Text)

function Get-SelectedPayload([string]$GameId, [string]$Separator) {
    $Version = '0.2.13'
    $buildRoot = Join-Path $repoRoot 'artifacts\profile-selection-test-only'
    $launcherName = if ($GameId -eq 'bs2') { 'Lanzador BioShock 2 VR DLSS-DLAA.exe' } else { 'Lanzador BioShock VR DLSS-DLAA.exe' }
    $baseManifest = [pscustomobject]@{ files = @($acceptedManifest.files | ForEach-Object {
        [pscustomobject]@{ path = $_.path.Replace('\', '/').Replace('/', $Separator); sha256 = $_.sha256 }
    }) }
    $selected = New-Object 'System.Collections.Generic.List[object]'
    function Add-Payload([string]$Source, [string]$Destination, [string]$Expected = '') {
        $selected.Add([pscustomobject]@{ Source = $Source; Destination = $Destination; Expected = $Expected })
    }
    & $selectionLoop
    return $selected.ToArray()
}

$legacyPath = Join-Path $BasePayloadDirectory 'host64\dlss-capabilities.ini'
$bs2Path = Join-Path $repoRoot 'installer\profiles\bs2\dlss-capabilities.ini'
foreach ($gameId in @('bs1', 'bs2')) {
    foreach ($separator in @('/', '\')) {
        $plan = @(Get-SelectedPayload $gameId $separator)
        Assert-Profile ($plan.Count -eq $acceptedManifest.files.Count) "$gameId/${separator}: incomplete payload."
        Assert-Profile (@($plan | Where-Object { $_.Destination.Contains('/') }).Count -eq 0) "$gameId payload paths must be normalized."
        $cap = @($plan | Where-Object { $_.Destination -eq 'host64\dlss-capabilities.ini' })
        Assert-Profile ($cap.Count -eq 1) "$gameId must have exactly one host capabilities file."
        $expectedSource = if ($gameId -eq 'bs2') { $bs2Path } else { $legacyPath }
        Assert-Profile ($cap[0].Source -eq $expectedSource) "$gameId selected the wrong host profile."
        if ($gameId -eq 'bs1') {
            $acceptedCap = @($acceptedManifest.files | Where-Object { $_.path.Replace('/', '\') -eq 'host64\dlss-capabilities.ini' })
            Assert-Profile ($cap[0].Expected -eq $acceptedCap[0].sha256) 'The accepted BS1 profile hash must remain pinned.'
        }
    }
}

# Use the same Windows INI API and contract as dlss45_client.cpp. In particular,
# the old unlabelled BS1 profile must never pass the BS2 identity guard.
if (-not ('BioShockPayloadProfileIni' -as [type])) {
    Add-Type -TypeDefinition @'
using System.Runtime.InteropServices;
using System.Text;
public static class BioShockPayloadProfileIni {
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    public static extern uint GetPrivateProfileStringW(string section, string key,
        string fallback, StringBuilder value, uint size, string path);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    public static extern uint GetPrivateProfileIntW(string section, string key, int fallback, string path);
}
'@
}
function Test-Capabilities([string]$Path, [string]$GameId) {
    $fullPath = [IO.Path]::GetFullPath($Path)
    if (-not (Test-Path -LiteralPath $fullPath -PathType Leaf)) { throw "Missing profile: $fullPath" }
    $values = @{}
    foreach ($key in @('phase', 'runtime', 'game', 'adapter')) {
        $buffer = New-Object Text.StringBuilder 32
        [void][BioShockPayloadProfileIni]::GetPrivateProfileStringW('backend', $key, '', $buffer, 32, $fullPath)
        $values[$key] = $buffer.ToString()
    }
    $eyes = [BioShockPayloadProfileIni]::GetPrivateProfileIntW('backend', 'eyeHosts', 0, $fullPath)
    $protocol = [BioShockPayloadProfileIni]::GetPrivateProfileIntW('backend', 'protocol', 0, $fullPath)
    $identity = if ($GameId -eq 'bs2') {
        $values.game -ceq 'bs2' -and $values.adapter -ceq 'bioshock2r'
    } else {
        $GameId -eq 'bs1' -and ((-not $values.game -and -not $values.adapter) -or
            ($values.game -ceq 'bs1' -and $values.adapter -ceq 'bioshock1r'))
    }
    return $identity -and $values.phase -ieq 'DLSS45' -and $eyes -ge 2 -and
        $values.runtime -ceq '310.7.0' -and $protocol -eq 8
}

$manifestCap = @($acceptedManifest.files | Where-Object { $_.path.Replace('/', '\') -eq 'host64\dlss-capabilities.ini' })
Assert-Profile ((Get-FileHash -LiteralPath $legacyPath -Algorithm SHA256).Hash -eq $manifestCap[0].sha256) 'Legacy profile does not match the accepted BS1 payload.'
Assert-Profile (Test-Capabilities $legacyPath 'bs1') 'Legacy BS1 profile must remain valid for BS1.'
Assert-Profile (-not (Test-Capabilities $legacyPath 'bs2')) 'Regression: legacy BS1 profile incorrectly accepted for BS2.'
Assert-Profile (Test-Capabilities $bs2Path 'bs2') 'BS2 profile does not satisfy the runtime contract.'
Assert-Profile (-not (Test-Capabilities $bs2Path 'bs1')) 'BS2 profile must not be used for BS1.'
if ($InstalledBs2Directory) {
    $installedPath = Join-Path $InstalledBs2Directory 'host64\dlss-capabilities.ini'
    Assert-Profile (Test-Capabilities $installedPath 'bs2') 'Installed BS2 profile is rejected by the runtime contract.'
    Assert-Profile ((Get-FileHash -LiteralPath $installedPath -Algorithm SHA256).Hash -eq
        (Get-FileHash -LiteralPath $bs2Path -Algorithm SHA256).Hash) 'Installed BS2 profile differs from the authored profile.'
}
Write-Output "PASS: $checks payload/profile checks. No installer or game was launched."
