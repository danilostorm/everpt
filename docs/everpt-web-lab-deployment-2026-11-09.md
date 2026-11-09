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

## 2026-11-09 opening-asset extension

- Added `Client/PristonTale.Web/startup-extras.json` with 206 verified assets captured from an initial WebGPU opening-scene trace on Nexus; about 7.94 MiB of additional unpacked assets.
- Added `extend_preload.py` before `pack_startup.py` in `build_web.ps1`. It validates each extra path against the published file manifest and extends the startup list without changing world assets.
- LAB web release `20261109-extras`, JS tag `dcfc09c6d372`.
- Startup set is now **2,228 files**, 262,439,614 uncompressed bytes and 146,440,196 gzip bytes.
- SHA-256 `c3b8ce84119b8981b75ec663b0a2d50c26e4794da28baf253ae009edd0249254`; package `startup-c3b8ce84119b8981.pack`.
- Local Nexus Chrome/WebGPU canary: package verified, rendered 4 frames / 1,212 draws after 20 seconds, zero JavaScript page errors; observed world background loading after first render.
- Remote public HTTPS: page, versioned scripts, `/healthz` and package manifest/headers all return 200. The first external browser cold-load remains bandwidth-bound; do not label the remote UI test complete until it renders.
- Same browser/test account login, selection of characters and gameplay persistence remain separate acceptance steps.
- Service config backup before this release: `10-release.pre-extras` in the WebHost systemd override directory. The `20261109-pack` release remains for rollback.
- Internal VM IP after reboot: `192.168.122.129` (libvirt reservation tied to VM MAC). LAB autostart enabled; game production VMs untouched.

## 2026-11-09 public browser and read-cache validation

- Version `b96a74748b4d` deployed to the **isolated EverPT LAB web service** at `ever.hoststorm.cloud`. The prior `20261109-extras` release remains available for rollback; override backup is `10-release.pre-lru`.
- Startup archive remains `startup-c3b8ce84119b8981.pack` (2,228 files, 146,440,196 compressed bytes, integrity checked).
- Web loader now fetches **18 HTTP byte ranges with four concurrent workers** plus a one-byte range probe, validates Content-Range and final SHA-256, with single-request and original per-file fallbacks.
- Added a bounded 384 MiB in-memory LRU for repeated file reads, preventing duplicate synchronous CDN downloads during opening. This is browser-only and does not change desktop or game server logic.
- Nexus direct-public Chrome/WebGPU test with **no local request interception**: first rendering within about 30 seconds; 4 frames, 1,212 draw calls, 0 on-demand file fetches, 11 read-cache hits, 0 JavaScript page errors. The browser automatically stopped testing at the first successful drawing threshold, not after a full gameplay session.
- CDN served the public bundle with `HIT`; public HTML, versioned JS, startup manifest and gateway health were all checked successfully.
- Remaining acceptance tests: authenticate the disposable QA account, load/create/select a character, enter a map, move/interact and confirm persistence and desktop interoperability. No claim of authenticated end-to-end gameplay until these complete.

## 2026-11-09 browser locale auto-selection

- Web release `20261109-locale` / `422a6b575448` deployed to the **isolated LAB** web host `ever.hoststorm.cloud`. Backup before promotion: `/etc/systemd/system/everpt-web-host.service.d/10-release.pre-locale`; previous `20261109-lru` release is retained.
- `i18n.js` auto-selects `pt-BR` for browser locales beginning with `pt`, and `en` for English browsers. It does **not** infer a person's language from their IP or physical country; players using an English browser can select Portuguese explicitly.
- `settings.js` writes the selected `language` to the game's WebAssembly `/client-settings.json` **before .NET starts**. This drives the existing translated `ClientText.pt-BR.resx` (942/942 English keys also translated), so opening/login, character selection and gameplay text can use pt-BR.
- New settings control: `Settings → General → Language` / `Configurações → Geral → Idioma`. Explicit choices survive reloads; older implicit `en` defaults migrate to the browser language. Reset restores browser auto-selection.
- Browser chrome translations cover loading status, graphics/audio settings, generic panel controls and GM admin form labels. In-game UI translations remain sourced from the existing game resources. Item names in assets, user chat and text baked into images are not automatically translated.
- Script fingerprinting now includes the new `i18n.js` module (8 modules total) to avoid CDN mixing.
- Playwright regression (`Client/PristonTale.Web/tests/locale_smoke.py`): all six locale/manual override scenarios passed, including pt-BR, en-US, pt-PT and migration of the previous implicit-English setting; live language selection updates the panel.
- Direct-public Chrome WebGPU pt-BR QA on Nexus, **no request interception**: at 30 s the game rendered 10 frames / 3,589 draw calls, no page JavaScript errors. `navigator.language`, document language, and the game's saved settings all confirmed `pt-BR`. Screenshot showed translated `Configurações` and `Preparando o mundo`.
- Account sign-in is not needed for these language tests. The user's separate game-login/character-map testing is not independently repeated with their account.
