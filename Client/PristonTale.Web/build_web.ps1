# Builds the browser game into -Output: the page, the program, the converted content and files.json. The game data itself is
# served from the site's assets folder. Needs the .NET 10 SDK with the wasm-tools and wasm-experimental workloads, the .NET 8
# runtime (the content builder), naga (cargo install naga-cli) and Python; see monogame/web/README.md.
param(
    [Parameter(Mandatory)][string]$Output,
    [Parameter(Mandatory)][string]$Work,
    [Parameter(Mandatory)][string]$Naga,
    [string]$Assets = (Join-Path $PSScriptRoot '..\..\Assets'),
    [switch]$SkipContent
)
$ErrorActionPreference = 'Stop'
$repo = Resolve-Path (Join-Path $PSScriptRoot '..\..')
$contentOut = Join-Path $Work 'content'
$contentWeb = Join-Path $Work 'content-web'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'

python (Join-Path $repo 'monogame\web\generate_interop.py') | Out-Null
if (-not $SkipContent) {
    dotnet build (Join-Path $repo 'monogame\pipeline\MonoGame.Effect.Processor\MonoGame.Effect.Processor.csproj') -c Release -v:q -nologo
    if ($LASTEXITCODE) { throw 'effect processor build failed' }
    Push-Location (Join-Path $repo 'Client\PristonTale\Content')
    dotnet mgcb "/@:Content.mgcb" "/outputDir:$contentOut" "/intermediateDir:$(Join-Path $Work 'content-obj')"
    $mgcb = $LASTEXITCODE
    Pop-Location
    if ($mgcb) { throw 'content build failed' }
    $dxc = Join-Path $env:USERPROFILE '.nuget\packages\dotnet-mgcb\3.8.6\tools\net8.0\any\windows-x64\dxc.exe'
    python (Join-Path $repo 'monogame\web\convert_effects.py') $Naga $contentOut $contentWeb (Join-Path $repo 'monogame\runtime\src\vulkan') $dxc (Join-Path $repo 'Client\PristonTale\Content\Shaders')
    if ($LASTEXITCODE) { throw 'effect conversion failed' }
}

dotnet publish (Join-Path $PSScriptRoot 'PristonTale.Web.csproj') -c Release -o $Output --artifacts-path (Join-Path $Work 'artifacts') -p:EtervaleWeb=true -v:q -nologo
if ($LASTEXITCODE) { throw 'publish failed' }

$site = Join-Path $Output 'wwwroot'
$siteContent = Join-Path $site 'Content'
if (Test-Path $siteContent) { Remove-Item $siteContent -Recurse -Force }
Copy-Item $contentWeb $siteContent -Recurse
python (Join-Path $PSScriptRoot 'make_files_manifest.py') $Assets $siteContent (Join-Path $site 'files.json')
if ($LASTEXITCODE) { throw 'manifest failed' }
# Include opening resources measured in the LAB browser trace before packing.
python (Join-Path $PSScriptRoot 'extend_preload.py') $site (Join-Path $PSScriptRoot 'startup-extras.json')
if ($LASTEXITCODE) { throw 'startup preload extension failed' }
# The startup data is also published as a versioned, integrity-checked gzip
# archive; clients without browser gzip streaming still use individual files.
python (Join-Path $PSScriptRoot 'pack_startup.py') $site $Assets
if ($LASTEXITCODE) { throw 'startup pack failed' }
# The compiled loader has embedded SHA-256 hashes of its WebAssembly modules.
# Give the two unversioned bootstrap scripts unique URLs for every release.
$revision = (& git -C $repo rev-parse --short=12 HEAD).Trim()
if ($LASTEXITCODE -ne 0) { throw 'could not determine web release revision' }
python (Join-Path $PSScriptRoot 'version_web_release.py') $site $revision
if ($LASTEXITCODE -ne 0) { throw 'web bootstrap fingerprinting failed' }
Write-Output "Built -> $site"
