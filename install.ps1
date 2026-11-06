# Install a prebuilt sharp release for Windows x64.
#
#   irm https://raw.githubusercontent.com/martasp/sharp/main/install.ps1 | iex
#
# Environment:
#   SHARP_VERSION       release tag to install (default: latest), e.g. v0.1.0
#   SHARP_INSTALL_DIR   destination directory (default: %LOCALAPPDATA%\Sharp\bin)

$ErrorActionPreference = 'Stop'
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

$repo = 'martasp/sharp'

if ($env:PROCESSOR_ARCHITECTURE -ne 'AMD64') {
    throw "sharp is currently published for Windows x64 only (detected $env:PROCESSOR_ARCHITECTURE)."
}

$asset = 'sharp-windows-x64.zip'

if ($env:SHARP_VERSION) {
    $version = $env:SHARP_VERSION -replace '^v', ''
    $baseUrl = "https://github.com/$repo/releases/download/v$version"
} else {
    $baseUrl = "https://github.com/$repo/releases/latest/download"
}

$tmp = Join-Path $env:TEMP ("sharp-install-" + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $tmp | Out-Null

try {
    Write-Host "==> Downloading $asset"
    Invoke-WebRequest -UseBasicParsing -Uri "$baseUrl/$asset" -OutFile (Join-Path $tmp $asset)
    Invoke-WebRequest -UseBasicParsing -Uri "$baseUrl/SHA256SUMS" -OutFile (Join-Path $tmp 'SHA256SUMS')

    Write-Host "==> Verifying checksum"
    $line = Get-Content (Join-Path $tmp 'SHA256SUMS') |
        Where-Object { $_ -match "\s$([regex]::Escape($asset))$" } |
        Select-Object -First 1
    if (-not $line) { throw "$asset not found in SHA256SUMS" }
    $expected = ($line -split '\s+')[0].ToLowerInvariant()
    $actual = (Get-FileHash (Join-Path $tmp $asset) -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actual -ne $expected) { throw "checksum mismatch for $asset" }

    Expand-Archive -Path (Join-Path $tmp $asset) -DestinationPath $tmp -Force
    $src = Join-Path $tmp 'sharp-windows-x64\sharp.exe'
    if (-not (Test-Path $src)) { throw "$asset does not contain sharp-windows-x64\sharp.exe" }

    $installDir = if ($env:SHARP_INSTALL_DIR) {
        $env:SHARP_INSTALL_DIR
    } else {
        Join-Path $env:LOCALAPPDATA 'Sharp\bin'
    }
    New-Item -ItemType Directory -Force -Path $installDir | Out-Null
    $target = Join-Path $installDir 'sharp.exe'
    Write-Host "==> Installing to $target"
    Copy-Item $src $target -Force

    $userPath = [Environment]::GetEnvironmentVariable('Path', 'User')
    $parts = @()
    if ($userPath) { $parts = $userPath -split ';' }
    if ($parts -notcontains $installDir) {
        $newPath = if ($userPath) { "$userPath;$installDir" } else { $installDir }
        [Environment]::SetEnvironmentVariable('Path', $newPath, 'User')
        Write-Host "==> Added $installDir to your user PATH; restart your terminal."
    } else {
        Write-Host "==> $installDir is already on your PATH."
    }

    Write-Host ""
    Write-Host "==> Done. Try:"
    Write-Host "      sharp --version"
    Write-Host "      sharp build"
} finally {
    Remove-Item -Recurse -Force $tmp -ErrorAction SilentlyContinue
}
