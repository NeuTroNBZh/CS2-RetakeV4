param([string]$Configuration = "Release")
$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
dotnet build "$root/src/RetakeV4/RetakeV4.csproj" -c $Configuration --nologo
if ($LASTEXITCODE -ne 0) { throw "Build failed" }

$bin = Join-Path $root "src/RetakeV4/bin/$Configuration/net10.0"
$out = Join-Path $root "artifacts/dev"
if (Test-Path $out) { Remove-Item $out -Recurse -Force }

$pluginDir = Join-Path $out "addons/counterstrikesharp/plugins/RetakeV4"
New-Item -ItemType Directory -Force $pluginDir | Out-Null
foreach ($file in @("RetakeV4.dll", "RetakeV4.Domain.dll", "RetakeV4.deps.json", "RetakeV4.runtimeconfig.json")) {
    $source = Join-Path $bin $file
    if (-not (Test-Path $source)) { throw "Missing build output: $file" }
    Copy-Item $source $pluginDir
}
# Ahead-of-time compiled for Linux game hosts: the first round after a (re)load no longer pays the JIT (measured 586 ms -> 75 ms
# on Dathost), which stalled the server long enough to kick players. Other platforms ignore the native code and JIT as before.
$r2r = Join-Path $root "artifacts/r2r"
if (Test-Path $r2r) { Remove-Item $r2r -Recurse -Force }
dotnet publish "$root/src/RetakeV4/RetakeV4.csproj" -c $Configuration -r linux-x64 --self-contained false -p:PublishReadyToRun=true -o $r2r --nologo
if ($LASTEXITCODE -ne 0) { throw "ReadyToRun publish failed" }
foreach ($file in @("RetakeV4.dll", "RetakeV4.Domain.dll")) {
    Copy-Item (Join-Path $r2r $file) $pluginDir -Force
}
Copy-Item (Join-Path $bin "lang") $pluginDir -Recurse
Copy-Item (Join-Path $bin "spawns") $pluginDir -Recurse
foreach ($dependency in @("Microsoft.Data.Sqlite.dll", "SQLitePCLRaw.core.dll", "SQLitePCLRaw.batteries_v2.dll", "SQLitePCLRaw.provider.e_sqlite3.dll", "MySqlConnector.dll")) {
    $source = Join-Path $bin $dependency
    if (-not (Test-Path $source)) { throw "Missing dependency: $dependency" }
    Copy-Item $source $pluginDir
}
Copy-Item (Join-Path $bin "runtimes") $pluginDir -Recurse

$sharedDir = Join-Path $out "addons/counterstrikesharp/shared/RetakeV4.Contracts"
New-Item -ItemType Directory -Force $sharedDir | Out-Null
$contracts = Join-Path $root "src/RetakeV4.Contracts/bin/$Configuration/net10.0/RetakeV4.Contracts.dll"
if (-not (Test-Path $contracts)) { throw "Missing build output: RetakeV4.Contracts.dll" }
Copy-Item $contracts $sharedDir
$cfgDir = Join-Path $out "cfg/RetakeV4"
New-Item -ItemType Directory -Force $cfgDir | Out-Null
Copy-Item (Join-Path $root "src/RetakeV4/cfg/RetakeV4/retake.cfg") $cfgDir

Write-Host "Package ready: $out"
