"""Merge opening-scene assets observed in WebGPU QA into the startup manifest.

The extra paths are a curated trace in startup-extras.json. Validate each
against files.json to avoid sending missing or private files to the browser.
Usage: python extend_preload.py <published-wwwroot> <startup-extras.json>
"""
import json
import sys
from pathlib import Path

if len(sys.argv) != 3:
    raise SystemExit("Usage: extend_preload.py <wwwroot> <startup-extras.json>")

site = Path(sys.argv[1]).resolve()
extras_file = Path(sys.argv[2]).resolve()
preload_file = site / "preload.json"
data = json.loads(preload_file.read_text(encoding="utf-8"))
extra_data = json.loads(extras_file.read_text(encoding="utf-8"))
manifest = json.loads((site / "files.json").read_text(encoding="utf-8"))

allowed = {
    f"{mount}/{name}"
    for mount, _folder, entries in manifest["trees"]
    for name, _size in entries
}
startup = data["startup"]
world = data["world"]
extras = extra_data["paths"]

if not isinstance(extras, list) or not 0 < len(extras) <= 512:
    raise ValueError("Unexpected count of traced opening assets")
if len(extras) != len(set(extras)):
    raise ValueError("Duplicate traced opening assets")
if len(startup) != len(set(startup)):
    raise ValueError("Duplicate original startup files")
if any(not isinstance(p, str) or not p.startswith(("/assets/", "/Content/")) for p in extras):
    raise ValueError("Invalid extra path")
missing = set(extras) - allowed
if missing:
    raise ValueError(f"Opening assets are missing from published manifest: {sorted(missing)[:8]}")

old = set(startup)
added = [p for p in extras if p not in old]
data["startup"] = startup + added
data["world"] = [p for p in world if p not in set(added)]
preload_file.write_text(json.dumps(data, separators=(",", ":")), encoding="utf-8")
print(f"EVERPT_PRELOAD_EXTENDED base={len(startup)} extra={len(added)} total={len(data['startup'])} world={len(data['world'])}")
