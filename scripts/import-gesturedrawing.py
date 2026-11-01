#!/usr/bin/env python3
"""Copies the old GestureDrawing app's data into a new Linework library. A one-time tool, not part of the app.

Usage: import-gesturedrawing.py --library <folder> [--source <folder>] [--replace] [--preferences]

Only reads the old data, and the database is opened read-only, so the old app keeps working.
--replace re-runs into a library this script made, so it can run again just before switching.
Exits non-zero if the counts don't match.
"""
import argparse
import datetime as dt
import hashlib
import json
import plistlib
import shutil
import sqlite3
import subprocess
import sys
import uuid
from pathlib import Path

DEFAULT_SOURCE = Path.home() / "Library/Application Support/GestureDrawing"
MARKER = ".imported-from-gesturedrawing"
WEEK_ONE = dt.date(2026, 11, 2)
PLAN_WEEKS = 38
WEEKDAYS = ["mon", "tue", "wed", "thu", "fri", "sat", "sun"]
EPOCH = dt.datetime(2001, 1, 1, tzinfo=dt.timezone.utc)
PREFERENCES = {
    "artist.name": "linework.name", "artist.welcomed": "linework.welcomed", "use24h": "linework.clock24h",
    "showSeconds": "linework.clockSeconds", "freehand.notes": "linework.notes", "glass.transparency": "linework.transparency",
    "setup.sound": "linework.sound", "setup.mode": "linework.setup.mode", "setup.seconds": "linework.setup.seconds",
    "setup.maxPoses": "linework.setup.maxPoses", "setup.shuffle": "linework.setup.shuffle", "setup.grayscale": "linework.setup.grayscale",
    "setup.today": "linework.setup.today", "setup.targetMinutes": "linework.setup.targetMinutes",
}


def iso(seconds):
    return (EPOCH + dt.timedelta(seconds=seconds)).strftime("%Y-%m-%dT%H:%M:%SZ")


def uid(blob):
    return str(uuid.UUID(bytes=bytes(blob))).upper()


def archived_ids(blob):
    """Session image IDs: an NSKeyedArchiver array of NSUUIDs."""
    archive = plistlib.loads(bytes(blob))
    objects = archive["$objects"]
    root = objects[archive["$top"]["root"].data]
    return [uid(objects[ref.data]["NS.uuidbytes"]) for ref in root["NS.objects"]]


def sequence(repeats):
    """Weeks in order as (plan week, repeat number), the same way the app builds them."""
    weeks = [(i + 1, 0) for i in range(PLAN_WEEKS)]
    for number, (after, count) in enumerate(repeats, start=1):
        if 0 <= after < len(weeks):
            first = max(after - count + 1, 0)
            weeks[after + 1:after + 1] = [(week, number) for week, _ in weeks[first:after + 1]]
    return weeks


def tick_key(day, block, weeks):
    offset = (dt.date.fromisoformat(day) - WEEK_ONE).days
    if offset < 0:
        return None
    index, weekday = offset // 7, offset % 7
    if index >= len(weeks):
        return f"f-{day}-{block}"
    week, number = weeks[index]
    pass_part = f"-r{number}" if number else ""
    return f"w{week:02d}-{WEEKDAYS[weekday]}-{block}{pass_part}"


def write_progress(library, name, value):
    text = json.dumps({"items": value, "version": 1}, indent=2, sort_keys=True, ensure_ascii=False)
    (library / "Progress" / name).write_text(text + "\n")


def prepare_library(library, replace):
    if library.exists() and any(p.name != ".DS_Store" for p in library.iterdir()):
        if not replace:
            sys.exit(f"{library} already has things in it. Use --replace to redo a library this script made.")
        if not (library / MARKER).exists():
            sys.exit(f"{library} wasn't made by this script, so it won't be replaced.")
        shutil.rmtree(library)
    for folder in ["Photos", "Drawings", "Canvases", "Progress"]:
        (library / folder).mkdir(parents=True, exist_ok=True)
    (library / MARKER).write_text("Made by import-gesturedrawing.py. Safe to delete after switching.\n")


def run(source, library, replace=False):
    source, library = Path(source), Path(library)
    prepare_library(library, replace)
    db = sqlite3.connect(f"file:{source / 'Library.store'}?mode=ro", uri=True)
    db.row_factory = sqlite3.Row
    rows = lambda table: db.execute(f"SELECT * FROM {table}").fetchall()

    photo_rows = rows("ZREFIMAGE")
    id_of_pk = {r["Z_PK"]: uid(r["ZID"]) for r in photo_rows}
    photos, benchmarks = [], []
    for number, r in enumerate(sorted(photo_rows, key=lambda r: r["ZIMPORTEDAT"]), start=1):
        original = source / "Images" / r["ZFILENAME"]
        name = f"photo-{number:03d}{original.suffix.lower()}"
        exists = original.exists()
        if exists:
            shutil.copy2(original, library / "Photos" / name)
        photos.append({
            "id": uid(r["ZID"]), "fileName": name, "sha256": hashlib.sha256(original.read_bytes()).hexdigest() if exists else "",
            "importedAt": iso(r["ZIMPORTEDAT"]),
            "crop": {"x": r["ZCROPX"], "y": r["ZCROPY"], "width": r["ZCROPW"], "height": r["ZCROPH"]},
            "rotation": r["ZROTATION"], "flipped": bool(r["ZFLIPPED"]), "noFigureFound": bool(r["ZNOFIGUREFOUND"]), "missing": not exists,
        })
        if r["ZISBENCHMARK"]:
            benchmarks.append({"photoID": uid(r["ZID"]), "seconds": r["ZBENCHMARKSECONDS"], "interval": r["ZBENCHMARKINTERVAL"],
                               "markedAt": iso(r["ZBENCHMARKMARKEDAT"] or r["ZIMPORTEDAT"])})
    write_progress(library, "photos.json", photos)

    attempts = []
    for r in rows("ZBENCHMARKATTEMPT"):
        name = r["ZDRAWINGFILENAME"]
        if name and (source / "Drawings" / name).exists():
            shutil.copy2(source / "Drawings" / name, library / "Drawings" / name)
        attempts.append({"id": uid(r["ZID"]), "photoID": id_of_pk.get(r["ZIMAGE"]), "date": iso(r["ZDATE"]), "sessionID": uid(r["ZSESSIONID"]),
                         **({"drawingFileName": name} if name else {})})
    write_progress(library, "benchmarks.json", {"benchmarks": benchmarks, "attempts": attempts})

    join = [t[0] for t in db.execute("SELECT name FROM sqlite_master WHERE type='table' AND name GLOB 'Z_*IMAGES'")]
    members = {}
    if join:
        columns = [c[1] for c in db.execute(f"PRAGMA table_info({join[0]})")]
        set_col = next(c for c in columns if c.endswith("SETS"))
        image_col = next(c for c in columns if c.endswith("IMAGES"))
        for r in rows(join[0]):
            members.setdefault(r[set_col], []).append(id_of_pk[r[image_col]])
    sets = [{"id": uid(r["ZID"]), "name": r["ZNAME"], "createdAt": iso(r["ZCREATEDAT"]), "photoIDs": members.get(r["Z_PK"], [])} for r in rows("ZIMAGESET")]
    write_progress(library, "sets.json", sets)

    presets = [{"id": uid(r["ZID"]), "name": r["ZNAME"], "steps": json.loads(bytes(r["ZSTEPS"]))} for r in rows("ZCLASSPRESET")]
    write_progress(library, "presets.json", presets)

    sessions = [{"id": uid(r["ZID"]), "startedAt": iso(r["ZSTARTEDAT"]), "drawingSeconds": r["ZDRAWINGSECONDS"], "poseCount": r["ZPOSECOUNT"],
                 "photoIDs": archived_ids(r["ZIMAGEIDS"])} for r in sorted(rows("ZSESSIONRECORD"), key=lambda r: r["ZSTARTEDAT"])]
    write_progress(library, "sessions.json", sessions)

    repeat_rows = sorted(rows("ZPLANREPEATRECORD"), key=lambda r: r["ZCREATEDAT"])
    repeats = [(r["ZAFTERSEQUENCEINDEX"], r["ZWEEKCOUNT"]) for r in repeat_rows]
    write_progress(library, "plan.json", {"weekOneStart": WEEK_ONE.isoformat(), "repeats": [
        {"number": i, "afterSequenceIndex": after, "weekCount": count} for i, (after, count) in enumerate(repeats, start=1)]})

    weeks = sequence(repeats)
    check_rows = sorted(rows("ZPLANCHECK"), key=lambda r: r["ZDAYKEY"])
    ticks = []
    for r in check_rows:
        key = tick_key(r["ZDAYKEY"], r["ZBLOCKKEY"], weeks)
        if key:
            ticks.append({"key": key, "countsOn": r["ZDAYKEY"], "checkedAt": iso(r["ZCHECKEDAT"])})
    write_progress(library, "checks.json", ticks)

    canvas = source / "Freehand"
    canvas_items = 0
    if (canvas / "canvas.json").exists():
        shutil.copytree(canvas, library / "Canvases/Canvas", ignore=shutil.ignore_patterns(".DS_Store"))
        canvas_items = len(json.loads((canvas / "canvas.json").read_text()).get("items", []))
    totals = {table: len(rows(table)) for table in ["ZIMAGESET", "ZCLASSPRESET", "ZSESSIONRECORD", "ZBENCHMARKATTEMPT"]}
    db.close()

    (library / "library.json").write_text(json.dumps({
        "createdAt": dt.datetime.now(dt.timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ"), "format": 1, "id": str(uuid.uuid4()).upper()}, indent=2, sort_keys=True) + "\n")

    copied = sum(1 for p in photos if not p["missing"])
    return {
        "photos": (len(photo_rows), len(photos)), "photo files": (len(photo_rows), copied), "sets": (totals["ZIMAGESET"], len(sets)),
        "presets": (totals["ZCLASSPRESET"], len(presets)), "sessions": (totals["ZSESSIONRECORD"], len(sessions)),
        "benchmarks": (sum(1 for r in photo_rows if r["ZISBENCHMARK"]), len(benchmarks)), "attempts": (totals["ZBENCHMARKATTEMPT"], len(attempts)),
        "ticks": (len(check_rows), len(ticks)), "repeats": (len(repeat_rows), len(repeats)),
        "canvas images": (canvas_items, len(json.loads((library / "Canvases/Canvas/canvas.json").read_text()).get("items", [])) if canvas_items else 0),
    }


def copy_preferences():
    old = subprocess.run(["defaults", "export", "com.hyprsea.gesturedrawing", "-"], capture_output=True).stdout
    values = plistlib.loads(old) if old else {}
    new = {PREFERENCES[k]: v for k, v in values.items() if k in PREFERENCES}
    subprocess.run(["defaults", "import", "com.hyprsea.linework", "-"], input=plistlib.dumps(new), check=True)
    return sorted(new)


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--library", required=True, type=Path)
    parser.add_argument("--source", type=Path, default=DEFAULT_SOURCE)
    parser.add_argument("--replace", action="store_true")
    parser.add_argument("--preferences", action="store_true")
    args = parser.parse_args()
    counts = run(args.source, args.library, replace=args.replace)
    print(f"{'':16}{'old':>6}{'new':>6}")
    for name, (old, new) in counts.items():
        print(f"{name:16}{old:>6}{new:>6}{'' if old == new else '  DIFFERENT'}")
    if args.preferences:
        print("preferences copied:", ", ".join(copy_preferences()))
    if any(old != new for old, new in counts.values()):
        sys.exit("The counts don't match. Don't switch yet.")
    print("All counts match.")


if __name__ == "__main__":
    main()
