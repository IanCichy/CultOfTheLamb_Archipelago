#!/usr/bin/env python3
"""Package worlds/cult_of_the_lamb/ into cult_of_the_lamb.apworld.

An .apworld is just a zip whose single top-level entry is the world package folder.
Run from the repo root:  py -3.12 build_apworld.py
"""
import os
import sys
import zipfile

WORLD_NAME = "cult_of_the_lamb"
REPO_ROOT = os.path.dirname(os.path.abspath(__file__))
WORLDS_DIR = os.path.join(REPO_ROOT, "worlds")
OUTPUT = os.path.join(REPO_ROOT, f"{WORLD_NAME}.apworld")

SKIP_DIRS = {"__pycache__", ".pytest_cache"}
SKIP_SUFFIXES = (".pyc", ".pyo")


def main() -> int:
    world_dir = os.path.join(WORLDS_DIR, WORLD_NAME)
    if not os.path.isdir(world_dir):
        print(f"ERROR: {world_dir} not found", file=sys.stderr)
        return 1

    written = []
    with zipfile.ZipFile(OUTPUT, "w", zipfile.ZIP_DEFLATED) as zf:
        for root, dirs, files in os.walk(world_dir):
            dirs[:] = [d for d in dirs if d not in SKIP_DIRS]
            for name in files:
                if name.endswith(SKIP_SUFFIXES):
                    continue
                abs_path = os.path.join(root, name)
                # Paths inside the zip must be relative to worlds/, so the archive
                # contains "cult_of_the_lamb/__init__.py" etc.
                arc_path = os.path.relpath(abs_path, WORLDS_DIR).replace(os.sep, "/")
                zf.write(abs_path, arc_path)
                written.append(arc_path)

    required = f"{WORLD_NAME}/__init__.py"
    if required not in written:
        print(f"ERROR: {required} missing from archive", file=sys.stderr)
        return 1

    print(f"Wrote {OUTPUT} ({len(written)} files)")
    for path in sorted(written):
        print(f"  {path}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
