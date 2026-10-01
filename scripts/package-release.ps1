param([Parameter(Mandatory = $true)][string]$Version)
$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
& (Join-Path $PSScriptRoot "package-dev.ps1") -Configuration Release
if ($LASTEXITCODE -ne 0) { throw "Packaging failed" }

$staging = Join-Path $root "artifacts/dev"
$release = Join-Path $root "artifacts/release"
if (Test-Path $release) { Remove-Item $release -Recurse -Force }
New-Item -ItemType Directory -Force $release | Out-Null

$noConfigs = Join-Path $release "RetakeV4-$Version-no-configs.zip"
Compress-Archive -Path (Join-Path $staging "*") -DestinationPath $noConfigs

$configDir = Join-Path $staging "addons/counterstrikesharp/configs/plugins/RetakeV4"
dotnet run --project (Join-Path $root "tools/RetakeV4.ConfigExporter") -c Release -- $configDir
if ($LASTEXITCODE -ne 0) { throw "Config export failed" }
$full = Join-Path $release "RetakeV4-$Version.zip"
Compress-Archive -Path (Join-Path $staging "*") -DestinationPath $full

Add-Type -AssemblyName System.IO.Compression.FileSystem
$noConfigEntries = [System.IO.Compression.ZipFile]::OpenRead($noConfigs).Entries.FullName -replace '\\', '/'
$fullEntries = [System.IO.Compression.ZipFile]::OpenRead($full).Entries.FullName -replace '\\', '/'
if ($noConfigEntries | Where-Object { $_ -like "addons/counterstrikesharp/configs/*" }) { throw "The no-configs zip contains configs" }
if (-not ($fullEntries -contains "addons/counterstrikesharp/configs/plugins/RetakeV4/core.json")) { throw "The full zip has no configs" }
if (-not ($fullEntries -contains "addons/counterstrikesharp/shared/RetakeV4.Contracts/RetakeV4.Contracts.dll")) { throw "The zip has no RetakeV4.Contracts" }

$sqlite = Join-Path $staging "addons/counterstrikesharp/plugins/RetakeV4/runtimes/linux-x64/native/libe_sqlite3.so"
$glibc = [regex]::Matches([System.Text.Encoding]::ASCII.GetString([System.IO.File]::ReadAllBytes($sqlite)), "GLIBC_2\.(\d+)") |
    ForEach-Object { [int]$_.Groups[1].Value } | Measure-Object -Maximum
if ($glibc.Maximum -gt 31) { throw "libe_sqlite3.so needs GLIBC_2.$($glibc.Maximum): game hosts with glibc 2.31 could not load it" }

Write-Host "Release ready: $full, $noConfigs"
