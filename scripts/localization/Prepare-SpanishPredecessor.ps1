[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$SpanishSource,[Parameter(Mandatory=$true)][string]$BasePayloadRoot)
$ErrorActionPreference='Stop'
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$output=Join-Path $repo 'artifacts\integration-0.2.17\spanish-predecessor-inputs'
New-Item -ItemType Directory -Path $output -Force | Out-Null
$pinsPath=Join-Path $SpanishSource 'release\validated-mods-v0.2.16.json'
$pins=Get-Content -LiteralPath $pinsPath -Raw | ConvertFrom-Json
$published=Get-Content -LiteralPath (Join-Path $repo 'release\manifest-v0.2.16.json') -Raw | ConvertFrom-Json
foreach($game in @('bs1','bs2')){
    $manifest=Get-Content -LiteralPath (Join-Path $BasePayloadRoot "manifest-$game.json") -Raw | ConvertFrom-Json
    if($manifest.version -ne '0.2.16' -or $manifest.gameId -ne $game -or $manifest.modBuild -ne $pins.modBuild -or $manifest.runtime -ne $pins.runtime -or $manifest.testFamily){throw 'Wrong predecessor identity.'}
    if(@($manifest.files).Count -ne 23){throw 'Wrong predecessor inventory.'}
    foreach($entry in $manifest.files){
        $pin=@($published.games.$game.files | Where-Object {$_.path.Replace('/','\') -eq $entry.path.Replace('/','\')})
        if($pin.Count -ne 1 -or $pin[0].sha256 -ne $entry.sha256 -or (Get-FileHash -LiteralPath (Join-Path (Join-Path $BasePayloadRoot $game) $entry.path)).Hash -ne $entry.sha256){throw 'Predecessor payload differs from published bytes.'}
    }
    foreach($pin in $pins.games.$game.files){
        $entry=@($manifest.files | Where-Object {$_.path.Replace('/','\') -eq $pin.path.Replace('/','\')})
        if($entry.Count -ne 1 -or $entry[0].sha256 -ne $pin.sha256){throw 'Predecessor release pins differ.'}
    }
    # Git checkout line endings change the JSON-file hash, not its payload pins.
    # Bind a generated input manifest to this exact unmodified Spanish checkout.
    $manifest.validatedManifestSha256=(Get-FileHash -LiteralPath $pinsPath).Hash
    [IO.File]::WriteAllText((Join-Path $output "manifest-$game.json"),($manifest | ConvertTo-Json -Depth 7),(New-Object Text.UTF8Encoding($false)))
}
Write-Output "PASS: Spanish 0.2.16 inputs rebound to checkout pin-file hash; all 46 payload hashes unchanged. $output"
