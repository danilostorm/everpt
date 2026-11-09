"""Fingerprint the entire WebPT ES-module graph for every published revision.

Usage: python version_web_release.py <published wwwroot> <git revision>
Generated JavaScript files are release artifacts, not checked-in code.
"""
from pathlib import Path
import re
import shutil
import sys

root = Path(sys.argv[1]).resolve()
revision = sys.argv[2].strip().lower()
if not re.fullmatch(r"[0-9a-f]{7,40}", revision):
    raise SystemExit("Expected a hexadecimal git revision of at least seven characters.")

index = root / "index.html"
loader = root / "_framework" / "dotnet.js"
modules = ("main", "mgweb", "mgfiles", "mgaudio", "settings", "admin", "panels", "i18n")
if not index.is_file() or not loader.is_file() or any(not (root / f"{name}.js").is_file() for name in modules):
    raise SystemExit("Incomplete WebPT publish: cannot fingerprint all bootstrap modules.")

shutil.copy2(loader, loader.with_name(f"dotnet.{revision}.js"))
for name in modules:
    source = (root / f"{name}.js").read_text(encoding="utf-8")
    for other in modules:
        source = source.replace(f"'./{other}.js'", f"'./{other}.{revision}.js'")
        source = source.replace(f'"./{other}.js"', f'"./{other}.{revision}.js"')
    source = source.replace(
        "'./_framework/dotnet.js'",
        f"'./_framework/dotnet.{revision}.js'",
    )
    (root / f"{name}.{revision}.js").write_text(source, encoding="utf-8")

html = index.read_text(encoding="utf-8")
html, count = re.subn(
    r'src="main(?:\.[A-Za-z0-9_-]{7,40})?\.js"',
    f'src="main.{revision}.js"',
    html,
)
if count != 1:
    raise SystemExit("Expected exactly one WebPT ES module entrypoint in index.html.")
index.write_text(html, encoding="utf-8")
print(f"EVERPT_VERSIONED_WEB_BOOTSTRAP {revision}: {len(modules)} modules + runtime loader")
