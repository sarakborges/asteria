$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$wryVersion = "v1.0.2"
$wryUrl = "https://github.com/doceazedo/godot_wry/releases/download/$wryVersion/godot_wry.zip"
$tempRoot = Join-Path ([System.IO.Path]::GetTempPath()) "asteria-godot-wry-$wryVersion"
$zipPath = Join-Path $tempRoot "godot_wry.zip"
$extractPath = Join-Path $tempRoot "extracted"
$addonsRoot = Join-Path $repoRoot "addons"
$targetPath = Join-Path $addonsRoot "godot_wry"

Write-Host "[1/3] Installing Godot WRY $wryVersion..."
Remove-Item $tempRoot -Recurse -Force -ErrorAction SilentlyContinue
New-Item $extractPath -ItemType Directory -Force | Out-Null
Invoke-WebRequest -Uri $wryUrl -OutFile $zipPath
Expand-Archive -Path $zipPath -DestinationPath $extractPath -Force

$extension = Get-ChildItem $extractPath -Recurse -Filter "WRY.gdextension" | Select-Object -First 1
if ($null -eq $extension) {
    throw "WRY.gdextension was not found in the Godot WRY release archive."
}

New-Item $addonsRoot -ItemType Directory -Force | Out-Null
Remove-Item $targetPath -Recurse -Force -ErrorAction SilentlyContinue
Copy-Item $extension.Directory.FullName $targetPath -Recurse

Write-Host "[2/3] Installing WebUI dependencies..."
Push-Location (Join-Path $repoRoot "ui")
try {
    npm install
    if ($LASTEXITCODE -ne 0) { throw "npm install failed." }

    Write-Host "[3/3] Building WebUI..."
    npm run build
    if ($LASTEXITCODE -ne 0) { throw "npm run build failed." }
}
finally {
    Pop-Location
}

Remove-Item $tempRoot -Recurse -Force -ErrorAction SilentlyContinue
Write-Host "WebUI ready. Restart Godot if it was already open, then run the project."
