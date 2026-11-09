# EverPT Web — LAB, 2026-11-09

## Scope
- This report concerns the **Etervale-Lab-20261108** VM (Ziel) and the WebPT client served at `https://ever.hoststorm.cloud`. No StormPT production component was touched.
- Git branch: `feat/webpt-integration`, build revision: `185d329d38ffebb430381eb0dc8826fa9a6db996`.
- Gateway and game service continue sharing the LAB SQLite account/character database. Real account/character round-trip is a separate QA item.

## Changes
1. Added a `/cadastro` link in the WebPT loading overlay so users can register without first downloading the game. Browser Playwright click/navigation passed.
2. Deferred seven BGM tracks belonging to distant fields on the **browser only**. First two town BGMs remain eager; desktop behavior is unchanged. `defer_world_audio.py` updates both startup/world manifests during `build_web.ps1`.
3. Corrected WebAssembly heap maximum to 2 GiB (`EmccMaximumHeapSize=2147483648`), matching the 32,768-page maximum of the Jiterpreter module. An earlier 4-GiB build produced a `LinkError` on opening; it was immediately rolled back before this corrected build was promoted.

## Bundle metrics
| | Old stable LAB | Current 2-GiB browser LAB |
|---|---:|---:|
| Startup assets | 2,228 | 2,221 |
| Raw startup bytes | 262,439,614 | 208,131,156 |
| Compressed startup bytes | 146,440,196 | 103,318,178 |

The startup download is smaller by 43,122,018 bytes (~29.4%). The seven moved BGM files stay in the world preload / on-demand paths rather than disappearing.

## Staging and verification
- Nexus build: `E:\\Etervale-Lab\\web-publish-audio-lazy-2gb\\wwwroot`.
- LAB active release: `/home/etervale/everpt-web-releases/20261109-audio-lazy-2gb`.
- Startup pack: `startup-6497eb5b928cffae.pack`; SHA-256: `6497eb5b928cffae942f9ddff7a1c0a4e3a3a79dfd6c1c91d33fde9bf22b57f0`.
- New runtime `_framework/dotnet.native.xs5s06l14z.wasm`: import `env.memory`, initial 8,192 pages, maximum 32,768 pages.
- Staging ZIP integrity / file-path checks and private HTTP smoke tests passed. HTTP 206 range loading passed.
- The LAB WebHost, game server, and web tunnel services were all active after deployment; public homepage and cadastro returned HTTP 200.

## Chrome browser QA
- Executed from Nexus via Playwright (headless Chrome, `locale=pt-BR`, WebGPU).
- **Passed:** QA task exit code 0. On the 50-second sample, runtime draw calls >36,000; game opening scenery, NPCs, and cinematic reported ready. Zero page JavaScript exceptions and no memory-import `LinkError`. Language `pt-BR`.
- Startup pack downloaded in 13 parallel HTTP 206 ranges and verified (2,221 files).
- Screenshot: `E:\\Etervale-Lab\\logs\\everpt-audio-lazy-2gb-ptbr.png`.
- JSON report: `E:\\Etervale-Lab\\logs\\everpt-web-audio-lazy-2gb.json`.
- The timing is a browser test result, **not** a controlled speed benchmark against the former release (CDN cache conditions differed).

## Rollback
- The former working release `20261109-registration-canary` and original systemd drop-in are preserved.
- Root-enabled QEMU guest agent can invoke the tested `/home/etervale/everpt-web-releases/rollback-audio-lazy.sh` script, which restores the original web release and restarts `everpt-web-host.service`.
- No game-server data migration is part of this deployment.

## Open follow-ups
- Do a complete real account registration -> login -> character creation -> first map round-trip in browser **and** desktop against the shared DB; do not assume the above HTTP/visual checks prove it.
- Review repeated nonfatal 404 resource responses and missing opening-map textures (white fallback logged).
- Verify full in-game NPC dialogues, client language override, and sprite rendering interactively.
- Benchmark initial and repeat load under identical cache/network conditions; optimize further only after reproducible measurements.
