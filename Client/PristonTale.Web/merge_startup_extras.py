"""Add opening assets verified by LAB runtime trace to the published preload manifest.

The base preload.json remains upstream-compatible. Only public assets already in
files.json can be added. This file is run *after* manifest creation and *before*
pack_startup.py, so bundle integrity covers the same expanded startup list.
"""
from pathlib import Path
import json
import sys

if len(sys.argv) != 3:
    raise SystemExit("Usage: merge_startup_extras.py <wwwroot> <extra-json>")

site=Path(sys.argv[1]).resolve()
extras=json.loads(Path(sys.argv[2]).read_text(encoding="utf-8"))["paths"]
preload_file=site/"preload.json"
lists=json.loads(preload_file.read_text(encoding="utf-8"))
manifest=json.loads((site/"files.json").read_text(encoding="utf-8"))
allowed={f"{mount}/{relative}" for mount,_url,entries in manifest["trees"]
         for relative,_size in entries}
if not isinstance(extras,list) or len(extras)!=len(set(extras)):
    raise ValueError("Invalid or duplicate opening asset paths")
for name in extras:
    if not isinstance(name,str) or not name.startswith(("/assets/","/Content/")) or name not in allowed:
        raise ValueError(f"Opening asset not present in public files manifest: {name}")

before=lists["startup"]
merged=list(dict.fromkeys([*before,*extras]))
lists["startup"]=merged
preload_file.write_text(json.dumps(lists,ensure_ascii=False,separators=(",",":")),encoding="utf-8")
print(f"EVERPT_OPENING_ASSETS_MERGED original={len(before)} extra={len(merged)-len(before)} total={len(merged)}")
