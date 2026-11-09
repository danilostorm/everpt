# Build both EverPT clients from exactly the same checked-out revision.
# Run on the dedicated Nexus LAB (Windows x64 with .NET 10, native runtime, Python and naga).
# This prepares verified artifacts only; it never updates a live game server automatically.
[CmdletBinding()]
param(
    [string]$OutputRoot = 'E:\Etervale-Lab\releases',
    [string]$Assets = '',
    [string]$Naga = '',
    [switch]$SkipNativeRuntime,
    [switch]$SkipWebContent
)

$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$git = 'C:\Program Files\Git\cmd\git.exe'
if (-not (Test-Path $git)) { throw 'Git for Windows is required.' }
$revision = (& $git -C $repo rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0 -or $revision.Length -ne 40) { throw 'Cannot determine the build revision.' }
$changes = @(& $git -C $repo status --porcelain --untracked-files=normal)
if ($changes.Count -gt 0) { throw 'Refusing to build mixed versions from a dirty worktree.' }

if (-not $Assets) { $Assets = Join-Path $repo 'assets' }
if (-not $Naga) { $Naga = Join-Path $env:USERPROFILE '.cargo\bin\naga.exe' }
if (-not (Test-Path (Join-Path $Assets 'Hud\Fonts\DejaVuSans.ttf'))) { throw 'Missing private game assets.' }
if (-not (Test-Path $Naga)) { throw 'Missing naga shader converter.' }

$release = Join-Path $OutputRoot $revision.Substring(0, 12)
New-Item -ItemType Directory -Force -Path $release | Out-Null

$desktopBuilder = Join-Path $repo 'tools\everpt-lab\build-windows-client.ps1'
$desktopArgs = @('-NoProfile', '-NonInteractive', '-ExecutionPolicy', 'Bypass', '-File', $desktopBuilder)
if ($SkipNativeRuntime) { $desktopArgs += '-SkipNativeRuntime' }
& powershell.exe @desktopArgs
if ($LASTEXITCODE -ne 0) { throw 'Desktop build failed; web will not be published.' }

$desktop = Join-Path $repo 'Client\PristonTale\bin\Release\net10.0\PristonTale.exe'
if (-not (Test-Path $desktop)) { throw 'Desktop executable was not generated.' }

$webScript = Join-Path $repo 'Client\PristonTale.Web\build_web.ps1'
$webOutput = Join-Path $release 'web'
$webWork = Join-Path $OutputRoot 'web-build'
$webArgs = @('-NoProfile', '-NonInteractive', '-ExecutionPolicy', 'Bypass', '-File', $webScript, '-Output', $webOutput, '-Work', $webWork, '-Naga', $Naga, '-Assets', $Assets)
if ($SkipWebContent) { $webArgs += '-SkipContent' }
& powershell.exe @webArgs
if ($LASTEXITCODE -ne 0) { throw 'Web build failed; neither client should be published.' }

$webRoot = Join-Path $webOutput 'wwwroot'
$loader = Join-Path $webRoot '_framework\dotnet.js'
$manifest = Join-Path $webRoot 'files.json'
if (-not (Test-Path $loader) -or -not (Test-Path $manifest)) { throw 'WebAssembly bundle is incomplete.' }
$metadata = [ordered]@{
    revision = $revision
    builtAtUtc = [DateTime]::UtcNow.ToString('o')
    clients = @('windows-vulkan', 'web-webgpu')
    desktopExe = $desktop
    desktopSha256 = (Get-FileHash $desktop -Algorithm SHA256).Hash.ToLowerInvariant()
    webRoot = $webRoot
    webManifestSha256 = (Get-FileHash $manifest -Algorithm SHA256).Hash.ToLowerInvariant()
    database = 'shared existing EverPT server, not bundled'
    deploy = 'LAB validation and controlled promotion required'
}
$metadata | ConvertTo-Json -Depth 5 | Set-Content -Path (Join-Path $release 'release.json') -Encoding UTF8
Write-Host "EVERPT_DUAL_BUILD_OK $revision"
Write-Host "Manifest: $(Join-Path $release 'release.json')"
