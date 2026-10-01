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

foreach ($file in @("RetakeV4.dll", "RetakeV4.Domain.dll")) {
    $stream = [System.IO.File]::OpenRead((Join-Path $staging "addons/counterstrikesharp/plugins/RetakeV4/$file"))
    try {
        $headers = [System.Reflection.PortableExecutable.PEReader]::new($stream).PEHeaders
        if ($headers.CorHeader.ManagedNativeHeaderDirectory.Size -le 0) { throw "$file is not ReadyToRun compiled" }
    }
    finally { $stream.Dispose() }
}

$sqlite = Join-Path $staging "addons/counterstrikesharp/plugins/RetakeV4/runtimes/linux-x64/native/libe_sqlite3.so"
$glibc = [regex]::Matches([System.Text.Encoding]::ASCII.GetString([System.IO.File]::ReadAllBytes($sqlite)), "GLIBC_2\.(\d+)") |
    ForEach-Object { [int]$_.Groups[1].Value } | Measure-Object -Maximum
if ($glibc.Maximum -gt 31) { throw "libe_sqlite3.so needs GLIBC_2.$($glibc.Maximum): game hosts with glibc 2.31 could not load it" }

$checkDir = Join-Path $root "artifacts/configcheck"
if (Test-Path $checkDir) { Remove-Item $checkDir -Recurse -Force }
dotnet publish (Join-Path $root "tools/RetakeV4.ConfigCheck") -c Release -o $checkDir --nologo
if ($LASTEXITCODE -ne 0) { throw "ConfigCheck publish failed" }
if (-not (Test-Path (Join-Path $checkDir "lang/fr.json"))) { throw "ConfigCheck has no lang files" }
Compress-Archive -Path (Join-Path $checkDir "*") -DestinationPath (Join-Path $release "RetakeV4-$Version-configcheck.zip")

Write-Host "Release ready: $full, $noConfigs"
