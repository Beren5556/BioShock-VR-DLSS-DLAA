param([Parameter(Mandatory=$true)][string]$BuildToolsDirectory)
$ErrorActionPreference='Stop'
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$output=Join-Path $repo 'artifacts\integration-0.2.13\migration-tests'
New-Item -ItemType Directory -Path $output -Force | Out-Null
$dtf=Join-Path $BuildToolsDirectory 'wixtoolset.dtf.windowsinstaller.6.0.2\lib\net20\WixToolset.Dtf.WindowsInstaller.dll'
Copy-Item -LiteralPath $dtf -Destination $output -Force
$plan=Join-Path $output 'PayloadPlan.g.cs'
[IO.File]::WriteAllText($plan,'namespace BioShockMsi { internal static class PayloadPlan { internal const string RegistryPath=""; internal const string TestFamily="migration-unit-test"; internal const string Version="0.2.13"; internal static readonly string[][] Items = new string[0][]; } }')
$compiler=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$exe=Join-Path $output 'LegacyMigrationTests.exe'
$sources=@('MsiActions.cs','MsiStorage.cs','GamePackage.cs','LegacyMigration.cs','LegacyMigrationTests.cs') | ForEach-Object { Join-Path $PSScriptRoot $_ }
& $compiler /nologo /target:exe /define:BIOSHOCK2 /codepage:65001 ("/out:"+$exe) /reference:System.dll /reference:System.Core.dll /reference:System.Xml.dll ("/reference:"+$dtf) @sources $plan
if($LASTEXITCODE -ne 0){throw 'No se han compilado las pruebas de migración.'}
& $exe | Tee-Object -FilePath (Join-Path $output 'results.txt')
if($LASTEXITCODE -ne 0){throw 'Ha fallado la migración aislada.'}
