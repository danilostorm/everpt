"""Build an integrity-checked, content-addressed gzip startup bundle for EverPT Web.

Only files present in files.json and named by preload.json are packaged.
The archive must contain public game assets only; redistribution rights are
separate from the technical build process.
Usage: python pack_startup.py <published-wwwroot> <game-assets-directory>
"""
import gzip
import hashlib
import json
import os
import shutil
import struct
import sys
from pathlib import Path

if len(sys.argv) != 3:
    raise SystemExit("Usage: pack_startup.py <wwwroot> <assets>")
site = Path(sys.argv[1]).resolve()
assets = Path(sys.argv[2]).resolve()
preload = json.loads((site / "preload.json").read_text(encoding="utf-8"))["startup"]
manifest = json.loads((site / "files.json").read_text(encoding="utf-8"))
allowed = {
    f"{mount}/{relative}": int(size)
    for mount, _url, entries in manifest["trees"]
    for relative, size in entries
}
if not preload or len(preload) != len(set(preload)):
    raise SystemExit("Startup manifest is empty or contains duplicates")

temp = site / "startup-build.tmp"
total = 0
try:
    with temp.open("wb") as raw:
        with gzip.GzipFile(filename="", fileobj=raw, mode="wb", compresslevel=4, mtime=0) as output:
            output.write(b"EVPT1" + struct.pack("<I", len(preload)))
            for logical in preload:
                if logical not in allowed:
                    raise ValueError(f"Startup path missing from public file manifest: {logical}")
                if logical.startswith("/assets/"):
                    base, relative = assets, logical[len("/assets/"):]
                elif logical.startswith("/Content/"):
                    base, relative = site / "Content", logical[len("/Content/"):]
                else:
                    raise ValueError("Disallowed startup asset mount")
                path = (base / relative).resolve()
                if not path.is_file() or not path.is_relative_to(base.resolve()):
                    raise ValueError(f"Missing or escaping startup asset: {logical}")
                length = path.stat().st_size
                if length != allowed[logical] or length >= 2**32:
                    raise ValueError(f"Invalid published asset size: {logical}")
                encoded = logical.encode("utf-8")
                if not 0 < len(encoded) < 2**16:
                    raise ValueError("Invalid asset path length")
                output.write(struct.pack("<HI", len(encoded), length))
                output.write(encoded)
                with path.open("rb") as source:
                    shutil.copyfileobj(source, output, 1024 * 1024)
                total += length

    digest = hashlib.sha256()
    with temp.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    sha = digest.hexdigest()
    target = site / f"startup-{sha[:16]}.pack"
    os.replace(temp, target)
    metadata = {
        "format": "EVPT1-gzip",
        "url": target.name,
        "sha256": sha,
        "count": len(preload),
        "uncompressedBytes": total,
        "compressedBytes": target.stat().st_size,
    }
    (site / "startup-pack.json").write_text(
        json.dumps(metadata, separators=(",", ":")), encoding="utf-8")
    print(f"EVERPT_STARTUP_PACK_OK files={len(preload)} original={total} packed={metadata['compressedBytes']}")
finally:
    if temp.exists():
        temp.unlink()
