[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$sourceRoot = [IO.Path]::GetFullPath($PSScriptRoot)
$repoRoot = [IO.Path]::GetFullPath((Join-Path $sourceRoot '..'))
$payloadRoot = Join-Path $sourceRoot 'Payload'
$releaseDocsRoot = Join-Path $repoRoot 'docs\release'
$modSourceRoot = $repoRoot
$hostSourceRoot = Join-Path $repoRoot 'components\dlss-host'
$outputPath = Join-Path $repoRoot 'artifacts\release\Instalador BioShock VR DLSS-DLAA Beta 0.2.1.exe'
$temporaryDirectory = Join-Path $sourceRoot ('.installer-build-' + [Guid]::NewGuid().ToString('N'))
$temporaryPath = Join-Path $temporaryDirectory 'Instalador BioShock VR DLSS-DLAA Beta 0.2.1.exe'
$sourcePath = Join-Path $sourceRoot 'src\BioshockVrDlss45StandaloneInstaller.cs'
$iconPath = Join-Path $repoRoot 'apps\launcher\assets\BioshockVrLauncher.ico'

$resources = @(
    [pscustomobject]@{ Path = (Join-Path $payloadRoot 'xinput1_3.dll'); Name = 'xinput1_3.dll'; Hash = '441BF1728BB38A2EC2BA57605CF840D786122E862D47A6DFA642BFF484F8E191' },
    [pscustomobject]@{ Path = (Join-Path $payloadRoot 'bioshockvr.dll'); Name = 'bioshockvr.dll'; Hash = '44B0FB0946330AB7F471D230A3E27D686CDFD400B2CF251B70BE6A9364986E7F' },
    [pscustomobject]@{ Path = (Join-Path $payloadRoot 'bvr_steamvr32.dll'); Name = 'bvr_steamvr32.dll'; Hash = '56537A2EA8F88FCE6A2928EAECDE11EEEEED9C4D04F36B39E466B4D03330B972' },
    [pscustomobject]@{ Path = (Join-Path $payloadRoot 'openvr_api.dll'); Name = 'openvr_api.dll'; Hash = 'AB696E4F218A95B3E396BC310F9FE6485DF48C99C0969762083212B1E1F025A6' },
    [pscustomobject]@{ Path = (Join-Path $payloadRoot 'host64\BioShockVR-DLSS45-Host64.exe'); Name = 'BioShockVR-DLSS45-Host64.exe'; Hash = '480D4A931C0CA5669B11061EFB28239BE6A041D1452BA50EACC30E0B26291453' },
    [pscustomobject]@{ Path = (Join-Path $payloadRoot 'host64\nvngx_dlss.dll'); Name = 'nvngx_dlss.dll'; Hash = 'BE6E434A94CA32499515EB62CA0E6C274526055D568D0426E4C652DCDFB6EE6E' },
    [pscustomobject]@{ Path = (Join-Path $payloadRoot 'host64\dlss-capabilities.ini'); Name = 'dlss-capabilities.ini'; Hash = '7C52BD6F6F186C40CDA847F0E143BDCFF94F0CB9BAC355977C27C2E27B857D77' },
    [pscustomobject]@{ Path = (Join-Path $payloadRoot 'Lanzador BioShock VR DLSS-DLAA.exe'); Name = 'Lanzador BioShock VR DLSS-DLAA.exe'; Hash = '298E4E7E744DBD5EC11FF7A32083B1CA5EB787C7BD8A7F23C504E3062B57D0D4' },
    [pscustomobject]@{ Path = (Join-Path $releaseDocsRoot 'LEEME-DLSS45.md'); Name = 'LEEME-DLSS45.md'; Hash = '8AEE2FCA8E2B2AA053FC483417402C81EAD38BFBA6FA9A0CE0A3C00E00E2EB5F' },
    [pscustomobject]@{ Path = (Join-Path $repoRoot 'docs\licenses\NVIDIA-DLSS-LICENSE.txt'); Name = 'NVIDIA-DLSS-LICENSE.txt'; Hash = 'A3E28883672AB1B48187A0CC004EA468C76F6BEA15F33F0F38A970B7F7E04C64' },
    [pscustomobject]@{ Path = (Join-Path $releaseDocsRoot 'INFORMACION-DEL-PAQUETE.txt'); Name = 'INFORMACION-DEL-PAQUETE.txt'; Hash = 'C7D9799CC7D1E8CC4E4673B0BB913F8EC3A962BB6B6A030FB079602CC13AF449' },
    [pscustomobject]@{ Path = (Join-Path $releaseDocsRoot 'dlss.ini.example'); Name = 'dlss.ini.example'; Hash = '0C8D1260BC3A5782106D95E6D374CE87D03CA0F36D3C198527F3C3359B601329' },
    [pscustomobject]@{ Path = (Join-Path $modSourceRoot 'LICENSE'); Name = 'BioShockVR-MIT-LICENSE.txt'; Hash = '199384980B6925AA5DA072314C0C265BB097F41C7849A7AB0E6DE9294D3D8114' },
    [pscustomobject]@{ Path = (Join-Path $hostSourceRoot 'LICENSE'); Name = 'DLSS-Host-MIT-LICENSE.txt'; Hash = '1CE240E402901FB81EB82A60A6BAFD2FB913CD5746860B0A4EC52A5ACB49CED7' },
    [pscustomobject]@{ Path = (Join-Path $modSourceRoot 'THIRD_PARTY_NOTICES.md'); Name = 'THIRD_PARTY_NOTICES.md'; Hash = '56EB4D3AEF9087E47113609CE507856A0270A62B8C6E1734CDF0EE5A2B670C13' },
    [pscustomobject]@{ Path = (Join-Path $modSourceRoot 'third_party\minhook\LICENSE.txt'); Name = 'MinHook-LICENSE.txt'; Hash = '4F21F857550D7BE854DA6EA5F2DA4E6775CA4E3FBB535E4F3D961C47D0BF3335' },
    [pscustomobject]@{ Path = (Join-Path $modSourceRoot 'third_party\imgui\LICENSE.txt'); Name = 'Dear-ImGui-LICENSE.txt'; Hash = 'F20418B409E53C8C9F4E90917FF395554A60320D4DFBF833DA89B339CAD8628A' },
    [pscustomobject]@{ Path = (Join-Path $modSourceRoot 'third_party\openvr_headers\LICENSE'); Name = 'OpenVR-LICENSE.txt'; Hash = '9E6D1480FB68E86CEAFED312F7E67DADCDC2A99B350B710D624B8F0F0F1A2329' },
    [pscustomobject]@{ Path = (Join-Path $modSourceRoot 'third_party\OpenXR-SDK\LICENSE'); Name = 'OpenXR-LICENSE.txt'; Hash = '3DDF9BE5C28FE27DAD143A5DC76EEA25222AD1DD68934A047064E56ED2FA40C5' },
    [pscustomobject]@{ Path = (Join-Path $modSourceRoot 'third_party\OpenXR-SDK\COPYING.adoc'); Name = 'OpenXR-COPYING.adoc'; Hash = '1B0FF1CFEADAE317A54457E4407EA1AE026AB71555CAF9A20294C492F2514273' }
)

foreach ($path in @($sourcePath, $iconPath) + @($resources | ForEach-Object { $_.Path })) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Falta un archivo necesario: $path"
    }
}

foreach ($resource in $resources) {
    $actual = (Get-FileHash -Algorithm SHA256 -LiteralPath $resource.Path).Hash
    if ($actual -ne $resource.Hash) {
        throw "Un componente ha cambiado: $($resource.Name); esperado $($resource.Hash); actual $actual"
    }
}

New-Item -ItemType Directory -Path (Split-Path -Parent $outputPath) -Force | Out-Null
New-Item -ItemType Directory -Path $temporaryDirectory | Out-Null
$compilerSource = Join-Path $temporaryDirectory 'BioshockVrDlss45StandaloneInstaller.utf8.cs'
$strictUtf8 = New-Object System.Text.UTF8Encoding($false, $true)
$source = [System.IO.File]::ReadAllText($sourcePath, $strictUtf8)
$utf8WithBom = New-Object System.Text.UTF8Encoding($true)
[System.IO.File]::WriteAllText($compilerSource, $source, $utf8WithBom)

$compilerPath = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compilerPath)) {
    $compilerPath = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
}
if (-not (Test-Path -LiteralPath $compilerPath)) {
    throw 'No se encontró el compilador C# de .NET Framework.'
}

$compilerArguments = @(
    '/nologo',
    '/target:winexe',
    '/optimize+',
    '/platform:anycpu',
    ('/win32icon:' + $iconPath),
    ('/out:' + $temporaryPath),
    '/reference:System.dll',
    '/reference:System.Core.dll',
    '/reference:System.Drawing.dll',
    '/reference:System.Windows.Forms.dll',
    $compilerSource
)
foreach ($resource in $resources) {
    $compilerArguments += ('/resource:' + $resource.Path + ',' + $resource.Name)
}

try {
    & $compilerPath $compilerArguments
    if ($LASTEXITCODE -ne 0) {
        throw "La compilación del instalador terminó con código $LASTEXITCODE."
    }
    if (-not (Test-Path -LiteralPath $temporaryPath -PathType Leaf)) {
        throw 'El compilador no produjo el ejecutable temporal.'
    }
    Move-Item -LiteralPath $temporaryPath -Destination $outputPath -Force
}
finally {
    if (Test-Path -LiteralPath $temporaryPath) { Remove-Item -LiteralPath $temporaryPath -Force }
    if (Test-Path -LiteralPath $compilerSource) { Remove-Item -LiteralPath $compilerSource -Force }
    if (Test-Path -LiteralPath $temporaryDirectory) { Remove-Item -LiteralPath $temporaryDirectory -Force }
}

$item = Get-Item -LiteralPath $outputPath
$hash = Get-FileHash -Algorithm SHA256 -LiteralPath $outputPath
Write-Host "Creado: $($item.FullName)"
Write-Host "Tamano: $($item.Length) bytes"
Write-Host "SHA-256: $($hash.Hash)"
