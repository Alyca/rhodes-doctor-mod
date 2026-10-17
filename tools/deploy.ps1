param(
    [string]$GamePath = "d:\steam\steamapps\common\Pathfinder Second Adventure"
)

$ErrorActionPreference = "Stop"
$root = Resolve-Path (Join-Path $PSScriptRoot "..")
$src = Join-Path $root "bin\Debug\net48"
$dst = Join-Path $GamePath "Mods\RhodesDoctor"
$ts = Get-Date -Format "yyyyMMdd-HHmmss"

if (-not (Test-Path (Join-Path $src "RhodesDoctor.dll"))) {
    Write-Error "Cannot find $src\RhodesDoctor.dll - please run dotnet build first."
}

if (-not (Test-Path $dst)) {
    Write-Error "Cannot find target mod directory $dst"
}

$oldDll = Join-Path $dst "RhodesDoctor.dll"
if (Test-Path $oldDll) {
    Copy-Item $oldDll (Join-Path $dst "RhodesDoctor.dll.bak-$ts")
    Write-Host "Backed up old DLL -> RhodesDoctor.dll.bak-$ts"
}

Copy-Item (Join-Path $src "RhodesDoctor.dll") (Join-Path $dst "RhodesDoctor.dll") -Force
$pdb = Join-Path $src "RhodesDoctor.pdb"
if (Test-Path $pdb) {
    Copy-Item $pdb (Join-Path $dst "RhodesDoctor.pdb") -Force
}

foreach ($f in @("Info.json", "LocalizedStrings.json")) {
    $runtimeFile = Join-Path $src $f
    if (Test-Path $runtimeFile) {
        Copy-Item $runtimeFile (Join-Path $dst $f) -Force
    }
}

Write-Host "Deployed successfully to $dst"
Write-Host "Please restart the game to load the new DLL."
