# EverPT Web LAB deployment and validation

Updated: 2026-11-09. Scope: **Etervale/EverPT LAB only**. Never update the separate production StormPT, Games VM or other projects from these steps.

## Current architecture

- Desktop: Windows client, GPU-native build.
- Web: WebAssembly + WebGPU, served at `https://ever.hoststorm.cloud`.
- Game and characters: both clients connect to the same **isolated EverPT LAB game server** and its existing SQLite database. WebSocket HTTPS bridge routes `/ws.ashx` to the authorized local game port, with TLS certificate pinning and no arbitrary port forwarding.
- LAB VM: `Etervale-Lab-20261108` on the Unraid Ziel host.
- Services: `everpt-lab`, `everpt-web-host`, `everpt-web-tunnel`.
- LAB VM autostart enabled after the 2026-11-09 Unraid reboot, and a persistent libvirt DHCP reservation was configured for the VM.
- Repository work branch: `feat/webpt-integration`. Do not merge or promote to another environment without validation.

## Changes validated

1. Full globalization support in browser .NET runtime (ICU; locales including pt-BR).
2. Static MonoGame shader/font extensions `.mgfxo`, `.xnb`, and ICU `.dat` served by WebHost.
3. Browser first-frame unblocked by avoiding eager opening VFX compilation **only in browser**; desktop cinematic unchanged.
4. The full preload manifest was restored after a truncated JSON was detected.
5. Each release fingerprints seven JavaScript modules and the `dotnet.js` bootstrap; HTML/scripts/manifests avoid stale CDN caches.
6. No login credentials accepted in URL parameters; public automated developer reports are disabled.
7. A content-addressed startup `.pack` is generated during build by `Client/PristonTale.Web/pack_startup.py`. Its metadata contains SHA-256; the web loader checks SHA-256 and archive contents before use, and falls back to per-file downloads on failure.
8. Public static pack delivery and versioned browser WebAssembly/ICU resources are eligible for dedicated Cloudflare cache rules, both limited to the exact EverPT subdomain.

## Current startup package

- Release: `20261109-pack`, Web module tag `71237a9e3216`.
- Original startup set: 2,022 files totaling 254,116,132 bytes (242.34 MiB).
- Gzip package: 143,365,697 bytes (136.72 MiB), about 43.58% smaller.
- Package filename: `startup-9bad725e05963f62.pack`.
- Full SHA-256: `9bad725e05963f62a0207fae467b4ea7316b11ad37492bd614a3fc523c7e2478`.
- WebHost publishes as `application/octet-stream` with immutable URL and HTTP byte range support.
- Two Cloudflare rules cache **only** `/startup-*.pack` and `/_framework/*.wasm` / `*.dat` on `ever.hoststorm.cloud`. Ruleset phase: `http_request_cache_settings`; edge TTL 1 day.
- The old `20261109-openingfix` release and its service config backup remain for rollback.

## Verification and outstanding QA

- WebHost canary and production LAB service checks passed; `/healthz`, versioned JS, ICU and startup bundle return HTTP 200.
- Full local Chrome/WebGPU test with the packaged loader: **693 frames / 194,796 draw calls / 0 JavaScript errors in 90 seconds**.
- External HTTPS bundle test: public file metadata and size match the original; HTTP range requests work; CDN reports HIT after warming. Full remote browser rendering and **successful authenticated login through the web UI** should still be checked, separately. WebSocket connectivity was tested, but it is not proof of successful account login.
- Do not claim production readiness until the login, character list, gameplay, persistence and remote full-browser smoke test all pass.

## Recovery

Only the LAB web service release should change during promotions. The service override file is:

`/etc/systemd/system/everpt-web-host.service.d/10-release.conf`

Prior release backup is retained alongside it as `10-release.pre-pack`. To roll back after a failed health check, restore that file, run `systemctl daemon-reload`, restart `everpt-web-host.service`, and purge the HTML entrypoint from the CDN. Do not restore a different game server's data or alter production.
