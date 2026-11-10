[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$DestinationPath
)

$ErrorActionPreference = "Stop"

# Pin the redistributable runtime used by the packaged Windows x64 build.
# The URL is Microsoft's official Fixed Version CDN endpoint.
$runtimeVersion = "157.0.4341.42"
$runtimeUrl = "https://msedge.sf.dl.delivery.mp.microsoft.com/filestreamingservice/files/b7b22a57-b321-483e-9855-d260b076f1a7/Microsoft.WebView2.FixedVersionRuntime.157.0.4341.42.x64.cab"
$expectedSha256 = "cedaf8f3d12202c8803bdbae727b01c1be34540b78e8f0a10c6b401c2a8be97d"

$workRoot = Join-Path $env:RUNNER_TEMP "asteria-webview2-$runtimeVersion"
$cabPath = Join-Path $workRoot "webview2-fixed-x64.cab"
$extractPath = Join-Path $workRoot "extracted"

try {
    Remove-Item $workRoot -Recurse -Force -ErrorAction SilentlyContinue
    New-Item -ItemType Directory -Force $extractPath | Out-Null

    Write-Host "Downloading Microsoft's WebView2 Fixed Version $runtimeVersion (x64)..."
    Invoke-WebRequest -Uri $runtimeUrl -OutFile $cabPath

    $actualSha256 = (Get-FileHash $cabPath -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actualSha256 -ne $expectedSha256) {
        throw "WebView2 Fixed Version SHA-256 mismatch: $actualSha256"
    }

    Write-Host "Expanding WebView2 Fixed Version runtime..."
    $expand = Join-Path $env:WINDIR "System32/expand.exe"
    & $expand "-F:*" $cabPath $extractPath
    if ($LASTEXITCODE -ne 0) {
        throw "Windows expand.exe failed for the WebView2 Fixed Version cabinet."
    }

    $browserExecutable = Get-ChildItem -LiteralPath $extractPath -Filter "msedgewebview2.exe" -File -Recurse |
        Select-Object -First 1
    if ($null -eq $browserExecutable) {
        throw "The WebView2 Fixed Version package did not contain msedgewebview2.exe."
    }

    $sourceDirectory = Split-Path -Parent $browserExecutable.FullName
    if (Test-Path $DestinationPath) {
        Remove-Item $DestinationPath -Recurse -Force
    }
    New-Item -ItemType Directory -Force $DestinationPath | Out-Null

    Get-ChildItem -LiteralPath $sourceDirectory -Force | ForEach-Object {
        Copy-Item -LiteralPath $_.FullName -Destination $DestinationPath -Recurse -Force
    }

    $packagedBrowser = Join-Path $DestinationPath "msedgewebview2.exe"
    if (-not (Test-Path $packagedBrowser -PathType Leaf)) {
        throw "Failed to place msedgewebview2.exe at the packaged runtime root."
    }

    Set-Content -LiteralPath (Join-Path $DestinationPath "asteria-runtime-version.txt") -Value $runtimeVersion -Encoding ascii

    $runtimeBytes = (Get-ChildItem -LiteralPath $DestinationPath -File -Recurse |
        Measure-Object -Property Length -Sum).Sum
    if ($runtimeBytes -lt 250MB) {
        throw "Packaged WebView2 runtime is unexpectedly small: $runtimeBytes bytes."
    }

    Write-Host "Packaged WebView2 Fixed Version $runtimeVersion ($runtimeBytes bytes)."
}
finally {
    Remove-Item $workRoot -Recurse -Force -ErrorAction SilentlyContinue
}
