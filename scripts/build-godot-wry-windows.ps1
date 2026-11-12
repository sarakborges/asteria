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

New-Item -ItemType Directory -Force (Split-Path -Parent $wryRoot) | Out-Null
Write-Host "Checking out WRY 0.50.5 source (the version resolved by the upstream Cargo.lock)..."
git clone --quiet --depth 1 --branch wry-v0.50.5 --single-branch https://github.com/tauri-apps/wry.git $wryRoot
if ($LASTEXITCODE -ne 0) { throw "Failed to retrieve WRY 0.50.5 source." }

$expectedWryCommit = "97fd5b1f5576ba4ac646cfb71830b7997f5f7018"
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
    let configured_browser_folder = std::env::var("WEBVIEW2_BROWSER_EXECUTABLE_FOLDER")
      .unwrap_or_default();
    // Godot returns normalized resource-style paths with forward slashes.
    // The WebView2 loader expects a native Windows filesystem path.
    let browser_executable_folder_text = configured_browser_folder.replace('/', "\\");
    let browser_executable_folder = if browser_executable_folder_text.is_empty() {
      None
    } else {
      Some(
        std::ffi::OsStr::new(&browser_executable_folder_text)
          .encode_wide()
          .chain(std::iter::once(0))
          .collect::<Vec<u16>>()
      )
    };
    let browser_executable_folder_ptr = browser_executable_folder
      .as_ref()
      .map(|path| PCWSTR::from_raw(path.as_ptr()))
      .unwrap_or(PCWSTR::null());

    let browser_executable_folder_path = PathBuf::from(&browser_executable_folder_text);
    let data_directory_display = data_directory
      .as_ref()
      .map(|path| path.to_string())
      .unwrap_or_else(|| "<default>".to_string());
    let mut available_browser_version = PWSTR::null();
    match unsafe {
      GetAvailableCoreWebView2BrowserVersionString(
        browser_executable_folder_ptr,
        &mut available_browser_version,
      )
    } {
      Ok(()) => {
        let version = unsafe {
          PCWSTR::from_raw(available_browser_version.0).to_string()
        }.unwrap_or_else(|_| "<decode-failed>".to_string());
        println!(
          "ASTERIA_WRY_DIAGNOSTICS runtime_probe=ok folder={} folder_exists={} browser_exe={} msedge_dll={} user_data={} user_data_exists={} detected_version={}",
          browser_executable_folder_text,
          browser_executable_folder_path.is_dir(),
          browser_executable_folder_path.join("msedgewebview2.exe").is_file(),
          browser_executable_folder_path.join("msedge.dll").is_file(),
          data_directory_display,
          PathBuf::from(&data_directory_display).is_dir(),
          version,
        );
        unsafe { CoTaskMemFree(Some(available_browser_version.0 as _)); }
      }
      Err(error) => println!(
        "ASTERIA_WRY_DIAGNOSTICS runtime_probe=failed folder={} folder_exists={} browser_exe={} msedge_dll={} user_data={} user_data_exists={} error={:?}",
        browser_executable_folder_text,
        browser_executable_folder_path.is_dir(),
        browser_executable_folder_path.join("msedgewebview2.exe").is_file(),
        browser_executable_folder_path.join("msedge.dll").is_file(),
        data_directory_display,
        PathBuf::from(&data_directory_display).is_dir(),
        error,
      ),
    }

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

# Report whether Godot successfully created the selected WebView2 user-data folder.
$godotModule = Join-Path $pluginRust "src/lib.rs"
$godotContent = Get-Content -LiteralPath $godotModule -Raw
$directoryCreationCall = "std::fs::create_dir_all(&absolute_path).ok();"
$directoryCreationDiagnostic = 'let directory_result = std::fs::create_dir_all(&absolute_path);' + $newline +
    '                println!("ASTERIA_WRY_DIAGNOSTICS user_data_directory={} exists={} create_result={:?}", absolute_path.display(), absolute_path.is_dir(), directory_result);'
if (-not $godotContent.Contains($directoryCreationCall)) {
    throw "Could not locate Godot WRY user-data directory creation for diagnostics."
}
$godotContent = $godotContent.Replace($directoryCreationCall, $directoryCreationDiagnostic)
Set-Content -LiteralPath $godotModule -Value $godotContent -Encoding utf8

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
Write-Host "Installed patched Godot WRY 1.0.2 / WRY 0.50.5 ($hash)."
Remove-Item $sourceRoot -Recurse -Force -ErrorAction SilentlyContinue
