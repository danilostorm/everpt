#!/bin/bash
# Builds Etervale's MonoGame native Vulkan runtime (libmgruntime) from the vendored source in src/ (MonoGame v3.8.6's
# native/monogame with the fork's changes, see README.md) and writes it to bin/<rid>/, where the Vulkan game picks it up.
#
#   monogame/runtime/build.sh            # Linux x64/arm64 or macOS; on Windows use build-windows.ps1
#
# SDL2 and FAudio are fetched at the commits of externals.txt into src/external (git-ignored), patched with
# patches/<name>/*.patch (the fork's fixes to them, one issue document each in ../issues) and built statically; the
# rest is committed. Low parallelism by default; set JOBS to change.
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"
JOBS="${JOBS:-2}"
case "$(uname -s)-$(uname -m)" in
  Linux-x86_64) rid=linux-x64; arch=x64; lib=libmgruntime.so ;;
  Linux-aarch64) rid=linux-arm64; arch=arm64; lib=libmgruntime.so ;;
  Darwin-*) rid=osx; arch=$( [ "$(uname -m)" = arm64 ] && echo arm64 || echo x64 ); lib=libmgruntime.dylib ;;
  *) echo "unsupported host $(uname -s)-$(uname -m); on Windows use build-windows.ps1" >&2; exit 2 ;;
esac
src="$here/src"
nice_() { if command -v nice > /dev/null; then nice -n 10 "$@"; else "$@"; fi; }

# Linux: SDL2 must be built with every desktop sound server, each loaded at run time (dlopen, as SDL does), so the game
# plays on PipeWire, PulseAudio or bare ALSA, whichever the machine runs. Without a backend's development headers SDL's
# configure silently leaves it out (the game then has no sound on that server): refuse to build instead.
if [ "${rid%%-*}" = linux ]; then
  missing=()
  for pc in alsa libpulse libpipewire-0.3; do
    pkg-config --exists "$pc" 2> /dev/null || missing+=("$pc")
  done
  if [ ${#missing[@]} -gt 0 ]; then
    echo "SDL2's audio backends need development headers that are missing: ${missing[*]}" >&2
    echo "  Fedora:        sudo dnf install alsa-lib-devel pulseaudio-libs-devel pipewire-devel" >&2
    echo "  Debian/Ubuntu: sudo apt install libasound2-dev libpulse-dev libpipewire-0.3-dev" >&2
    exit 3
  fi
fi

# Fetched dependencies, at the pinned commits, with the fork's patches.
grep -v '^#' "$here/externals.txt" | while read -r name mode path commit _licence url; do
  [ "$mode" = fetched ] || continue
  dir="$src/$path"
  if [ "$(git -C "$dir" rev-parse HEAD 2>/dev/null || true)" != "$commit" ]; then
    rm -rf "$dir"
    mkdir -p "$dir"
    git -C "$dir" init -q
    git -C "$dir" remote add origin "$url"
    git -C "$dir" fetch -q --depth 1 origin "$commit"
    git -C "$dir" checkout -q FETCH_HEAD
    echo "fetched $name $commit"
  fi
  for patch in "$here/patches/$name"/*.patch; do
    [ -f "$patch" ] || continue
    if git -C "$dir" apply --reverse --check "$patch" 2> /dev/null; then
      continue # already applied
    fi
    git -C "$dir" apply "$patch"
    echo "patched $name: $(basename "$patch")"
  done
done

# A dependency is rebuilt when its patches or its configuration change (the stamp records both).
stamp_of() { { cat "$here/patches/$1"/*.patch 2> /dev/null || true; echo "$2"; } | sha256sum | cut -d' ' -f1; }

S="$src/external/sdl2/sdl"
sdl_options="-DSDL_STATIC=ON -DSDL_SHARED=OFF -DSDL_TEST=OFF -DCMAKE_BUILD_TYPE=Release -DCMAKE_POSITION_INDEPENDENT_CODE=ON"
if [ "${rid%%-*}" = linux ]; then
  sdl_options="$sdl_options -DSDL_ALSA=ON -DSDL_ALSA_SHARED=ON -DSDL_PULSEAUDIO=ON -DSDL_PULSEAUDIO_SHARED=ON"
  sdl_options="$sdl_options -DSDL_PIPEWIRE=ON -DSDL_PIPEWIRE_SHARED=ON"
fi
sdl_stamp=$(stamp_of sdl2 "$sdl_options")
if [ ! -f "$S/build/libSDL2.a" ] || [ "$(cat "$S/build/etervale.stamp" 2> /dev/null)" != "$sdl_stamp" ]; then
  rm -rf "$S/build"
  # shellcheck disable=SC2086 # the options are words
  cmake -S "$S" -B "$S/build" $sdl_options > /dev/null
  nice_ cmake --build "$S/build" --config Release -j "$JOBS" > /dev/null
  echo "$sdl_stamp" > "$S/build/etervale.stamp"
  rm -rf "$src/external/faudio/build" # FAudio links this SDL2
fi
# The audio backends SDL2 compiled in (SDL_config.h); on Linux the three sound servers are required.
backends=$(find "$S/build" -name SDL_config.h -exec grep -ho 'define SDL_AUDIO_DRIVER_[A-Z0-9]* 1' {} + \
  | sed 's/define SDL_AUDIO_DRIVER_//; s/ 1//' | sort -u | tr '\n' ' ')
echo "SDL2 audio backends: ${backends:-none}"
if [ "${rid%%-*}" = linux ]; then
  for b in ALSA PULSEAUDIO PIPEWIRE; do
    case " $backends " in *" $b "*) ;; *) echo "SDL2 was configured without the $b backend" >&2; exit 3 ;; esac
  done
fi

F="$src/external/faudio"
faudio_stamp=$(stamp_of faudio "")
if [ ! -f "$F/build/libFAudio.a" ] || [ "$(cat "$F/build/etervale.stamp" 2> /dev/null)" != "$faudio_stamp" ]; then
  rm -rf "$F/build"
  cmake -S "$F" -B "$F/build" -DBUILD_SHARED_LIBS=OFF -DBUILD_SDL3=OFF \
    -DCMAKE_C_STANDARD_INCLUDE_DIRECTORIES="$S/include" -DCMAKE_BUILD_TYPE=Release \
    -DCMAKE_POSITION_INDEPENDENT_CODE=ON > /dev/null
  nice_ cmake --build "$F/build" --config Release -j "$JOBS" > /dev/null
  echo "$faudio_stamp" > "$F/build/etervale.stamp"
fi

# The runtime. The Vulkan headers are vendored (premake adds $VULKAN_SDK/include, so point it there: a system volk or
# Vulkan header must not shadow the pinned ones). On macOS VULKAN_SDK is the Vulkan SDK with MoltenVK.
if [ "$rid" != osx ]; then export VULKAN_SDK="$src/external/vulkan-headers"; fi
cd "$src"
premake5 --mg-arch="$arch" gmake > /dev/null
# The runtime links the static SDL2 and FAudio, which make does not track: always relink (the objects are kept).
find "$here/build/desktopvk" -name "$lib" -delete 2> /dev/null || true
nice_ make config=release desktopvk -j "$JOBS" > /dev/null

built=$(find "$here/build/desktopvk" -name "$lib" | head -1)
mkdir -p "$here/bin/$rid"
cp "$built" "$here/bin/$rid/$lib"
echo "built $here/bin/$rid/$lib"
