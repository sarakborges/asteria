[CmdletBinding()]
param(
    [string]$RepositoryRoot = (Split-Path -Parent $PSScriptRoot)
)

$ErrorActionPreference = "Stop"
$newline = [Environment]::NewLine

$addonRoot = Join-Path $RepositoryRoot "addons/godot_wry"
$sourceRoot = Join-Path $env:RUNNER_TEMP "asteria-godot-wry-v1.0.2"
$pluginSource = Join-Path $sourceRoot "godot_wry"
$pluginRust = Join-Path $pluginSource "rust"
$wryRoot = Join-Path $pluginSource "vendor/wry"
$wryModule = Join-Path $wryRoot "src/webview2/mod.rs"
$cargoManifest = Join-Path $pluginRust "Cargo.toml"
$target = "x86_64-pc-windows-msvc"

if (-not (Get-Command cargo -ErrorAction SilentlyContinue)) {
    throw "Rust Cargo is required to build the patched Windows WebView extension."
}
if (-not (Get-Command git -ErrorAction SilentlyContinue)) {
    throw "Git is required to retrieve the pinned Godot WRY and WRY sources."
}

Remove-Item $sourceRoot -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force $sourceRoot | Out-Null

Write-Host "Checking out Godot WRY v1.0.2 source..."
git clone --quiet --depth 1 --branch v1.0.2 --single-branch https://github.com/doceazedo/godot_wry.git $pluginSource
if ($LASTEXITCODE -ne 0) { throw "Failed to retrieve Godot WRY v1.0.2 source." }

Write-Host "Checking out WRY 0.50.4 source..."
git clone --quiet --depth 1 --branch wry-v0.50.4 --single-branch https://github.com/tauri-apps/wry.git $wryRoot
if ($LASTEXITCODE -ne 0) { throw "Failed to retrieve WRY 0.50.4 source." }

$expectedWryCommit = "8008ee18cfb6cb26bacea932b628363287221cad"
$actualWryCommit = (git -C $wryRoot rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0 -or $actualWryCommit -ne $expectedWryCommit) {
    throw "Unexpected WRY source revision: $actualWryCommit"
}

$wryContent = Get-Content -LiteralPath $wryModule -Raw
if ($wryContent -notmatch 'CreateCoreWebView2EnvironmentWithOptions\(\s*PCWSTR::null\(\),') {
    throw "The expected WRY 0.50.4 WebView2 environment call was not found."
}

if ($wryContent -notmatch 'use std::os::windows::ffi::OsStrExt;') {
    $wryContent = [regex]::Replace(
        $wryContent,
        '(?m)^use dpi::',
        ("use std::os::windows::ffi::OsStrExt;" + $newline + "use dpi::"),
        1
    )
}

$runtimeSelection = @'
    // Pass the packaged Fixed Version folder directly to WebView2.
    // Relying only on loader environment overrides is not sufficient in this host.
    let browser_executable_folder = std::env::var_os("WEBVIEW2_BROWSER_EXECUTABLE_FOLDER")
      .filter(|path| !path.is_empty())
      .map(|path| path.encode_wide().chain(std::iter::once(0)).collect::<Vec<u16>>());
    let browser_executable_folder_ptr = browser_executable_folder
      .as_ref()
      .map(|path| PCWSTR::from_raw(path.as_ptr()))
      .unwrap_or(PCWSTR::null());

'@

$anchor = "    // additional browser args"
if (-not $wryContent.Contains($anchor)) {
    throw "The expected WRY environment-setup insertion point was not found."
}
$wryContent = $wryContent.Replace($anchor, $runtimeSelection + $anchor)
$wryContent = [regex]::Replace(
    $wryContent,
    'CreateCoreWebView2EnvironmentWithOptions\(\s*PCWSTR::null\(\),',
    ("CreateCoreWebView2EnvironmentWithOptions(" + $newline + "        browser_executable_folder_ptr,"),
    1
)
if ($wryContent -notmatch 'browser_executable_folder_ptr,') {
    throw "Failed to patch WRY to pass browserExecutableFolder explicitly."
}
Set-Content -LiteralPath $wryModule -Value $wryContent -Encoding utf8

$manifestContent = Get-Content -LiteralPath $cargoManifest -Raw
if ($manifestContent -notmatch '(?m)^\[patch\.crates-io\]') {
    Add-Content -LiteralPath $cargoManifest -Value ($newline + "[patch.crates-io]" + $newline + 'wry = { path = "../vendor/wry" }' + $newline) -Encoding utf8
}

Write-Host "Building patched Godot WRY for $target..."
Push-Location $pluginRust
try {
    cargo build --release --target $target
    if ($LASTEXITCODE -ne 0) { throw "Patched Godot WRY build failed." }
}
finally {
    Pop-Location
}

$builtLibrary = Join-Path $pluginRust "target/$target/release/godot_wry.dll"
$destinationLibrary = Join-Path $addonRoot "bin/$target/godot_wry.dll"
if (-not (Test-Path $builtLibrary -PathType Leaf)) {
    throw "Patched Godot WRY build output was not created: $builtLibrary"
}
if (-not (Test-Path (Split-Path -Parent $destinationLibrary))) {
    throw "The installed Godot WRY Windows binary directory is missing."
}
Copy-Item -LiteralPath $builtLibrary -Destination $destinationLibrary -Force

$hash = (Get-FileHash $destinationLibrary -Algorithm SHA256).Hash.ToLowerInvariant()
Write-Host "Installed patched Godot WRY 1.0.2 / WRY 0.50.4 ($hash)."
Remove-Item $sourceRoot -Recurse -Force -ErrorAction SilentlyContinue
