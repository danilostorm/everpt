"""Writes files.json, the file tree the page mounts (wwwroot/mgfiles.js).

Usage: python make_files_manifest.py <assets dir> <content dir> <output files.json>
The server-only data (GameServer) and the original client's saves are left out.
"""
import json
import os
import sys

SKIP = {'gameserver', 'savedata'}


def entries(root, skip=()):
    found = []
    for folder, folders, files in os.walk(root):
        if folder == root:
            folders[:] = [name for name in folders if name.lower() not in skip]
        folders.sort()
        for name in sorted(files):
            path = os.path.join(folder, name)
            found.append([os.path.relpath(path, root).replace(os.sep, '/'), os.path.getsize(path)])
    return found


def main():
    assets, content, output = sys.argv[1:4]
    trees = [['/assets', 'assets', entries(assets, SKIP)], ['/Content', 'Content', entries(content)]]
    with open(output, 'w', encoding='utf-8') as out:
        json.dump({'trees': trees}, out, separators=(',', ':'))
    total = sum(size for tree in trees for _, size in tree[2])
    print('%d files, %.1f GB -> %s (%d KB)' % (sum(len(tree[2]) for tree in trees), total / 2 ** 30, output, os.path.getsize(output) // 1024))


if __name__ == '__main__':
    main()
