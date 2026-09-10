[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$ManifestPath,
    [Parameter(Mandatory=$true)][string]$BuildToolsDirectory
)
$ErrorActionPreference='Stop'
$manifest=Get-Content -LiteralPath $ManifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
$packageRoot=Split-Path ([IO.Path]::GetFullPath($ManifestPath))
$msi=Join-Path $packageRoot $manifest.installer
if ($manifest.kind -ne 'native-combined-msi' -or (Get-FileHash -LiteralPath $msi).Hash -ne $manifest.sha256) { throw 'Wrong package identity/hash.' }
$extracted=Join-Path $packageRoot ('verify-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $extracted | Out-Null
Add-Type -Path (Join-Path $BuildToolsDirectory 'wixtoolset.dtf.windowsinstaller.6.0.2\lib\net20\WixToolset.Dtf.WindowsInstaller.dll')
$database=New-Object WixToolset.Dtf.WindowsInstaller.Database($msi, [WixToolset.Dtf.WindowsInstaller.DatabaseOpenMode]::ReadOnly)
try {
    if (($database.SummaryInfo.WordCount -band 8) -ne 0) { throw 'UAC-capable package metadata missing.' }
    if (@($database.ExecuteStringQuery('SELECT `File` FROM `File`')).Count -ne 46) { throw 'Expected exactly 46 payload files.' }
    if (@($database.ExecuteStringQuery('SELECT `Feature` FROM `Feature`')).Count -ne 4) { throw 'Expected two mods and two optional shortcuts.' }
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
    foreach ($id in @('bs1','bs2')) {
        $index=0
        foreach ($file in $manifest.games.$id.files) {
            $source=Join-Path $extracted ("File_${id}_$index")
            if ((Get-FileHash -LiteralPath $source).Hash -ne $file.sha256) { throw "Embedded payload mismatch: $id $($file.path)" }
            $index++; $verified++
        }
    }
    $report=[ordered]@{passed=$true;sha256=$manifest.sha256;files=$verified;nativeSingleMsi=$true;perUser=$true;uacCapable=$true;extracted=$extracted}
    [IO.File]::WriteAllText((Join-Path $packageRoot 'package-verification.json'),($report | ConvertTo-Json), (New-Object Text.UTF8Encoding($false)))
    Write-Output ("Verified native MSI: $verified embedded files, 4 features, no nested installations.")
} finally { $database.Dispose() }
