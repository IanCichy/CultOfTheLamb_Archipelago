#!/usr/bin/env python3
"""Package worlds/cult_of_the_lamb/ into cult_of_the_lamb.apworld.

An .apworld is just a zip whose single top-level entry is the world package folder.
Run from the repo root:  py -3.12 build_apworld.py
"""
import json
import os
import re
import sys
import zipfile

WORLD_NAME = "cult_of_the_lamb"
REPO_ROOT = os.path.dirname(os.path.abspath(__file__))
WORLDS_DIR = os.path.join(REPO_ROOT, "worlds")
OUTPUT = os.path.join(REPO_ROOT, f"{WORLD_NAME}.apworld")

SKIP_DIRS = {"__pycache__", ".pytest_cache"}
SKIP_SUFFIXES = (".pyc", ".pyo")

MANIFEST_NAME = "archipelago.json"

# Archipelago 0.7.0 refuses to load an apworld without a manifest; 0.6.x only warns. The two
# version numbers are the manifest *format*, not ours - copied from worlds that ship one today.
MANIFEST_FORMAT_VERSION = 7
GAME_NAME = "Cult of the Lamb"
AUTHORS = ["IanCichy"]
MINIMUM_AP_VERSION = "0.6.6"


def world_version() -> str:
    """MOD_VERSION from the world package, which is also what fill_slot_data sends as
    worldVersion. Read by regex rather than imported: importing the package would pull in
    Archipelago itself, which isn't on the path when this script runs."""
    init_path = os.path.join(WORLDS_DIR, WORLD_NAME, "__init__.py")
    with open(init_path, encoding="utf-8") as handle:
        match = re.search(r'^MOD_VERSION\s*=\s*"([^"]+)"', handle.read(), re.MULTILINE)

    if not match:
        raise SystemExit(f"ERROR: no MOD_VERSION found in {init_path}")

    return match.group(1)


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

        # Generated rather than checked in, so it can't drift from MOD_VERSION.
        manifest_path = f"{WORLD_NAME}/{MANIFEST_NAME}"
        zf.writestr(manifest_path, json.dumps({
            "version": MANIFEST_FORMAT_VERSION,
            "compatible_version": MANIFEST_FORMAT_VERSION,
            "game": GAME_NAME,
            "authors": AUTHORS,
            "world_version": world_version(),
            "minimum_ap_version": MINIMUM_AP_VERSION,
        }, indent=4))
        written.append(manifest_path)

    for required in (f"{WORLD_NAME}/__init__.py", f"{WORLD_NAME}/{MANIFEST_NAME}"):
        if required not in written:
            print(f"ERROR: {required} missing from archive", file=sys.stderr)
            return 1

    print(f"Wrote {OUTPUT} ({len(written)} files)")
    for path in sorted(written):
        print(f"  {path}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
