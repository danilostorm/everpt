# Builds the MonoGame native Vulkan runtime (mgruntime.dll) for Windows x64 from the vendored source in src/ and writes it
# to bin/win-x64/, where the client and the test runner pick it up. It runs the steps of the
# fork's Windows CI build locally.
#
#   powershell -ExecutionPolicy Bypass -File monogame/runtime/build-windows.ps1
#
# Needs Visual Studio 2022 or 2026 with "Desktop development with C++" (MSVC, the Windows SDK and the CMake that ships
# with Visual Studio) and Git. The Vulkan SDK is not needed: volk and the Vulkan headers are vendored in src/external.
# SDL2 and FAudio are fetched at the commits of externals.txt into src/external (git-ignored), patched with
# patches/<name>/*.patch and built statically with the static C runtime (/MT); premake 5.0.0-beta8 is downloaded into
# work/ (git-ignored).
# Native tools report on stderr; failures are detected by their exit codes (Invoke-Checked), not by PowerShell.
$ErrorActionPreference = 'Continue'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$src = Join-Path $here 'src'
$work = Join-Path $here 'work'

function Invoke-Checked([string]$file, [string[]]$arguments)
{
    & $file @arguments
    if ($LASTEXITCODE -ne 0)
    {
        throw "$file $($arguments -join ' ') failed with exit code $LASTEXITCODE"
    }
}

$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
if (-not (Test-Path $vswhere))
{
    throw 'Visual Studio Installer (vswhere.exe) not found: install Visual Studio 2022 or 2026 with the C++ workload.'
}

$vs = & $vswhere -latest -prerelease -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
$vsVersion = & $vswhere -latest -prerelease -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property catalog_productLineVersion
if (-not $vs)
{
    throw 'No Visual Studio with the MSVC x64 tools was found.'
}

$cmake = Join-Path $vs 'Common7\IDE\CommonExtensions\Microsoft\CMake\CMake\bin\cmake.exe'
if (-not (Test-Path $cmake))
{
    $cmake = (Get-Command cmake -ErrorAction Stop).Source
}

$msbuild = Join-Path $vs 'MSBuild\Current\Bin\amd64\MSBuild.exe'
switch ($vsVersion)
{
    { $_ -in '2026', '18' } { $generator = 'Visual Studio 18 2026'; $premakeAction = 'vs2022'; $toolset = 'v145' }
    { $_ -in '2022', '17' } { $generator = 'Visual Studio 17 2022'; $premakeAction = 'vs2022'; $toolset = 'v143' }
    default { throw "Unsupported Visual Studio $vsVersion at $vs (2022 or 2026 required)." }
}

Write-Host "Visual Studio $vsVersion at $vs; CMake $cmake"

# Fetched dependencies at the pinned commits, with the fork's patches (the same rules as build.sh).
foreach ($line in Get-Content (Join-Path $here 'externals.txt'))
{
    if ($line.StartsWith('#') -or $line.Trim().Length -eq 0)
    {
        continue
    }

    $fields = $line -split '\s+'
    $name = $fields[0]
    $mode = $fields[1]
    $path = $fields[2]
    $commit = $fields[3]
    $url = $fields[$fields.Length - 1]
    if ($mode -ne 'fetched')
    {
        continue
    }

    $dir = Join-Path $src $path
    $head = ''
    if (Test-Path (Join-Path $dir '.git'))
    {
        $head = (& git -C $dir rev-parse HEAD 2>$null)
    }

    if ($head -ne $commit)
    {
        if (Test-Path $dir)
        {
            Remove-Item -Recurse -Force $dir -ErrorAction Stop
        }

        New-Item -ItemType Directory -Force $dir | Out-Null
        Invoke-Checked git @('-C', $dir, 'init', '-q')
        # The patches are LF; a checkout converted to CRLF (core.autocrlf) would not take them.
        Invoke-Checked git @('-C', $dir, 'config', 'core.autocrlf', 'false')
        Invoke-Checked git @('-C', $dir, 'remote', 'add', 'origin', $url)
        Invoke-Checked git @('-C', $dir, 'fetch', '-q', '--depth', '1', 'origin', $commit)
        Invoke-Checked git @('-C', $dir, 'checkout', '-q', 'FETCH_HEAD')
        Write-Host "fetched $name $commit"
    }

    $patchDir = Join-Path $here "patches\$name"
    if (Test-Path $patchDir)
    {
        foreach ($patch in Get-ChildItem $patchDir -Filter *.patch)
        {
            & git -C $dir apply --reverse --check $patch.FullName 2>$null
            if ($LASTEXITCODE -eq 0)
            {
                continue
            }

            Invoke-Checked git @('-C', $dir, 'apply', $patch.FullName)
            Write-Host "patched ${name}: $($patch.Name)"
        }
    }
}

# SDL2 and FAudio as static libraries with the static C runtime. CMP0091 NEW: FAudio's cmake_minimum_required predates it
# and would otherwise ignore CMAKE_MSVC_RUNTIME_LIBRARY (/MD FAudio against the /MT runtime: LNK4098/LNK4217). CMake 4
# refuses projects that declare a minimum below 3.5 unless CMAKE_POLICY_VERSION_MINIMUM is given.
$common = @('-A', 'x64', '-G', $generator, '-DCMAKE_POLICY_DEFAULT_CMP0091=NEW', '-DCMAKE_MSVC_RUNTIME_LIBRARY=MultiThreaded',
    '-DCMAKE_POLICY_VERSION_MINIMUM=3.5')
$sdl = Join-Path $src 'external\sdl2\sdl'
Invoke-Checked $cmake (@('-S', $sdl, '-B', "$sdl\build\x64") + $common + @('-DSDL_STATIC=ON', '-DSDL_SHARED=OFF', '-DSDL_TEST=OFF'))
Invoke-Checked $cmake @('--build', "$sdl\build\x64", '--config', 'Release', '--parallel', '2')
$faudio = Join-Path $src 'external\faudio'
Invoke-Checked $cmake (@('-S', $faudio, '-B', "$faudio\build\x64") + $common + @('-DBUILD_SHARED_LIBS=OFF', '-DBUILD_SDL3=OFF',
    "-DCMAKE_C_STANDARD_INCLUDE_DIRECTORIES=$sdl\include"))
Invoke-Checked $cmake @('--build', "$faudio\build\x64", '--config', 'Release', '--parallel', '2')

# premake 5.0.0-beta8 (the version the fork's CI build uses). It writes Visual Studio 2022 projects; with Visual Studio 2026
# MSBuild builds them with its own toolset (PlatformToolset below).
$premake = Join-Path $work 'premake\premake5.exe'
if (-not (Test-Path $premake))
{
    New-Item -ItemType Directory -Force (Join-Path $work 'premake') | Out-Null
    $zip = Join-Path $work 'premake.zip'
    Invoke-WebRequest -UseBasicParsing -ErrorAction Stop -OutFile $zip `
        -Uri 'https://github.com/premake/premake-core/releases/download/v5.0.0-beta8/premake-5.0.0-beta8-windows.zip'
    Expand-Archive -Force -ErrorAction Stop $zip (Join-Path $work 'premake')
    Remove-Item $zip
}

$env:VULKAN_SDK = Join-Path $src 'external\vulkan-headers'
Push-Location $src
try
{
    Invoke-Checked $premake @($premakeAction)
    Invoke-Checked $msbuild @('monogame.sln', '/t:desktopvk', '/p:Configuration=Release', '/p:Platform=x64', "/p:PlatformToolset=$toolset",
        '/m:2', '/nologo', '/v:minimal')
}
finally
{
    Pop-Location
}

$built = Join-Path $here 'build\desktopvk\windows\x64\Release\mgruntime.dll'
$bin = Join-Path $here 'bin\win-x64'
New-Item -ItemType Directory -Force $bin | Out-Null
Copy-Item -Force $built $bin -ErrorAction Stop
Write-Host "built $bin\mgruntime.dll"
