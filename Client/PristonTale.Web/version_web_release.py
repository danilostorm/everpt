"""Fingerprint public JS entrypoints so CDN cache cannot mix WebAssembly releases.

Usage: python version_web_release.py <published wwwroot> <git revision>
Only modifies the generated published directory, never checked-in source files.
"""
from pathlib import Path
import re
import shutil
import sys

root = Path(sys.argv[1]).resolve()
revision = sys.argv[2].strip().lower()
if not re.fullmatch(r"[0-9a-f]{7,40}", revision):
    raise SystemExit("Expected a 7-40 character hexadecimal git revision.")

index = root / "index.html"
main = root / "main.js"
loader = root / "_framework" / "dotnet.js"
if not all(p.is_file() for p in (index, main, loader)):
    raise SystemExit("WebAssembly publish directory is incomplete.")

html = index.read_text(encoding="utf-8")
entry = main.read_text(encoding="utf-8")
old_script = 'src="main.js"'
old_import = "from './_framework/dotnet.js'"
if html.count(old_script) != 1 or entry.count(old_import) != 1:
    raise SystemExit("Unexpected WebPT bootstrap tags: refusing partial fingerprinting.")

new_main = f"main.{revision}.js"
new_loader = f"dotnet.{revision}.js"
shutil.copy2(loader, loader.with_name(new_loader))
(root / new_main).write_text(
    entry.replace(old_import, f"from './_framework/{new_loader}'"),
    encoding="utf-8",
)
index.write_text(html.replace(old_script, f'src="{new_main}"'), encoding="utf-8")
print(f"EVERPT_VERSIONED_WEB_BOOTSTRAP: {new_main} => _framework/{new_loader}")
