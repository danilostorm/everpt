"""Move distant-field BGM out of WebPT's first-login bundle.

The native client is unchanged. WebPT decodes those tracks when first used,
while the browser warms their HTTP cache after drawing the login screen.
"""
import json
import sys
from pathlib import Path

DEFERRED_MUSIC = (
    "/assets/wav/BGM/Field - Forest - DarkWood.bgm",
    "/assets/wav/BGM/Dungeon - Gloomy Heart.bgm",
    "/assets/wav/BGM/Town 2 - Morion_Philliy - Voyage above the Clouds.bgm",
    "/assets/wav/Sod/BGM/Stage_Play1.bgm",
    "/assets/wav/Sod/BGM/Stage_Play3.bgm",
    "/assets/wav/BGM/Field - Desert - Pilgrim.bgm",
    "/assets/wav/BGM/Ice 1.bgm",
)


def main(site: Path) -> None:
    manifest_path = site / "preload.json"
    preload = json.loads(manifest_path.read_text(encoding="utf-8"))
    startup = preload["startup"]
    world = preload["world"]
    original = set(startup)
    if len(original) != len(startup):
        raise ValueError("Duplicate initial preload asset")
    missing = set(DEFERRED_MUSIC) - original
    if missing:
        raise ValueError(f"Expected BGM assets absent: {sorted(missing)}")
    deferred = set(DEFERRED_MUSIC)
    preload["startup"] = [path for path in startup if path not in deferred]
    preload["world"] = list(DEFERRED_MUSIC) + [path for path in world if path not in deferred]
    if set(preload["startup"]) & set(preload["world"]):
        raise ValueError("Startup/world asset overlap")
    manifest_path.write_text(json.dumps(preload, separators=(",", ":")), encoding="utf-8")
    print(f"WEB_AUDIO_LAZY_READY deferred={len(deferred)} startup={len(preload['startup'])} world={len(preload['world'])}")


if __name__ == "__main__":
    if len(sys.argv) != 2:
        raise SystemExit("Usage: python defer_world_audio.py <published-wwwroot>")
    main(Path(sys.argv[1]))
