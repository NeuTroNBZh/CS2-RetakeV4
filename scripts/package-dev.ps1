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
Copy-Item (Join-Path $bin "lang") $pluginDir -Recurse
Copy-Item (Join-Path $bin "spawns") $pluginDir -Recurse
foreach ($dependency in @("Microsoft.Data.Sqlite.dll", "SQLitePCLRaw.core.dll", "SQLitePCLRaw.batteries_v2.dll", "SQLitePCLRaw.provider.e_sqlite3.dll", "MySqlConnector.dll")) {
    $source = Join-Path $bin $dependency
    if (-not (Test-Path $source)) { throw "Missing dependency: $dependency" }
    Copy-Item $source $pluginDir
}
Copy-Item (Join-Path $bin "runtimes") $pluginDir -Recurse

$cfgDir = Join-Path $out "cfg/RetakeV4"
New-Item -ItemType Directory -Force $cfgDir | Out-Null
Copy-Item (Join-Path $root "src/RetakeV4/cfg/RetakeV4/retake.cfg") $cfgDir

Write-Host "Package ready: $out"
