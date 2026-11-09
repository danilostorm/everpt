# Build the EverPT Windows client on the dedicated Nexus laboratory.
# Run in Windows PowerShell 5.1 or later, after the assets and the C++ Build Tools have been installed.
[CmdletBinding()]
param(
    [switch]$SkipNativeRuntime
)

$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$archiveRoot = Split-Path -Parent $repo
$logDir = Join-Path $archiveRoot 'logs'
New-Item -ItemType Directory -Path $logDir -Force | Out-Null

$assetRoot = Join-Path $repo 'assets'
$font = Join-Path $assetRoot 'Hud\Fonts\DejaVuSans.ttf'
$nativeRuntime = Join-Path $repo 'monogame\runtime\bin\win-x64\mgruntime.dll'

if (-not (Test-Path $font)) {
    throw "Game assets (including Hud/Fonts/DejaVuSans.ttf) must be extracted under $assetRoot before building."
}

$gitDir = 'C:\Program Files\Git\cmd'
if ((Test-Path (Join-Path $gitDir 'git.exe')) -and
    -not (Get-Command git -ErrorAction SilentlyContinue)) {
    $env:Path = "$gitDir;$env:Path"
}

if (-not (Get-Command git -ErrorAction SilentlyContinue)) {
    throw 'Git for Windows is required by monogame/runtime/build-windows.ps1.'
}

$dotnet = Get-Command dotnet -ErrorAction Stop
if (-not ((& $dotnet.Source --list-sdks) -match '^10\.')) {
    throw '.NET 10 SDK is required.'
}

Start-Transcript -Path (Join-Path $logDir 'build-windows-client.log') -Append | Out-Null
try {
    Push-Location $repo
    try {
        if (-not $SkipNativeRuntime -or -not (Test-Path $nativeRuntime)) {
            $script = Join-Path $repo 'monogame\runtime\build-windows.ps1'
            & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $script
            if ($LASTEXITCODE -ne 0) {
                throw "MonoGame native Vulkan runtime failed with code $LASTEXITCODE."
            }
            if (-not (Test-Path $nativeRuntime)) {
                throw "Missing expected native runtime: $nativeRuntime"
            }
        }

        & $dotnet.Source tool restore
        if ($LASTEXITCODE -ne 0) { throw "dotnet tool restore failed ($LASTEXITCODE)." }

        & $dotnet.Source restore PristonTale.sln -warnaserror
        if ($LASTEXITCODE -ne 0) { throw "dotnet restore failed ($LASTEXITCODE)." }

        & $dotnet.Source build PristonTale.sln -c Release -m:1 -warnaserror
        if ($LASTEXITCODE -ne 0) { throw "dotnet build failed ($LASTEXITCODE)." }

        Write-Host 'EVERPT_WINDOWS_CLIENT_BUILD_OK'
        Write-Host "Client binaries: $(Join-Path $repo 'Client\PristonTale\bin\Release\net10.0')"
    }
    finally {
        Pop-Location
    }
}
finally {
    Stop-Transcript | Out-Null
}
