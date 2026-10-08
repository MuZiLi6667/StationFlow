# Packages StationFlow into a Thunderstore-format zip:
#   dist/StationFlow-<ver>.zip
#     manifest.json / README.md / CHANGELOG.md / icon.png / plugins/StationFlow.dll
# Usage (execution policy friendly, via stdin):
#   cat tools/pack.ps1 | powershell -NoProfile -Command -
$ErrorActionPreference = "Stop"

# Locate project dir without hardcoding non-ASCII path
$modBuild = "E:\Mod build"
$sfDir = $null
foreach ($d in (Get-ChildItem $modBuild -Directory)) {
    $cand = Join-Path $d.FullName "StationFlow"
    if (Test-Path (Join-Path $cand "StationFlow.csproj")) { $sfDir = $cand; break }
}
if (-not $sfDir) { throw "StationFlow.csproj not found under $modBuild" }
Set-Location $sfDir

# Version from csproj (single source of truth)
$csproj = Get-Content "StationFlow.csproj" -Raw
if ($csproj -notmatch '<Version>([^<]+)</Version>') { throw "Version not found in csproj" }
$ver = $Matches[1]
Write-Host "StationFlow version: $ver"

# Release build (does NOT deploy to game profile; Debug builds keep doing that)
& dotnet build -c Release
if ($LASTEXITCODE -ne 0) { throw "release build failed" }

$dll = Join-Path $sfDir "bin\Release\StationFlow.dll"
if (-not (Test-Path $dll)) { throw "dll not found: $dll" }

# Stage
$dist = Join-Path $sfDir "dist"
$stage = Join-Path $dist "StationFlow-$ver"
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
New-Item (Join-Path $stage "plugins") -ItemType Directory -Force | Out-Null

# Sync manifest version_number with csproj version (csproj is the single source of truth)
$mfSrc = Join-Path $sfDir "package\manifest.json"
$mfText = Get-Content $mfSrc -Raw
$mfSync = $mfText -replace '"version_number"\s*:\s*"[^"]+"', ('"version_number": "' + $ver + '"')
if ($mfSync -ne $mfText) {
    [System.IO.File]::WriteAllText($mfSrc, $mfSync, (New-Object System.Text.UTF8Encoding($false)))
    Write-Host "manifest.json version_number synced to $ver"
}

foreach ($f in @("manifest.json", "README.md", "CHANGELOG.md", "icon.png")) {
    $src = Join-Path $sfDir "package\$f"
    if (-not (Test-Path $src)) { throw "missing package file: $f" }
    Copy-Item $src $stage
}
Copy-Item $dll (Join-Path $stage "plugins")

# Manifest version must match csproj version
$mf = Get-Content (Join-Path $stage "manifest.json") -Raw
if ($mf -notmatch '"version_number"\s*:\s*"([^"]+)"') { throw "version_number not found in manifest.json" }
if ($Matches[1] -ne $ver) { throw "manifest version $($Matches[1]) != csproj version $ver" }

# Zip (flat root: manifest at zip root, as Thunderstore requires)
$zip = Join-Path $dist "StationFlow-$ver.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path (Join-Path $stage "*") -DestinationPath $zip

# Verify: list entries
Add-Type -AssemblyName System.IO.Compression.FileSystem
$z = [System.IO.Compression.ZipFile]::OpenRead($zip)
Write-Host "package contents ($zip):"
$z.Entries | ForEach-Object { Write-Host ("  " + $_.FullName + "  " + $_.Length + " bytes") }
$z.Dispose()
Write-Host "DONE"
