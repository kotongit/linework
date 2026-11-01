import importlib.util
import json
import plistlib
import sqlite3
import tempfile
import unittest
import uuid
from pathlib import Path

spec = importlib.util.spec_from_file_location("importer", Path(__file__).with_name("import-gesturedrawing.py"))
importer = importlib.util.module_from_spec(spec)
spec.loader.exec_module(importer)

PHOTO_ID = uuid.UUID("0636D1BA-38C7-4A93-B85D-6996B3C6BCF5")
SESSION_ID = uuid.UUID("11111111-2222-3333-4444-555555555555")
EPOCH_2026 = 815_000_000.0


def keyed_archive(ids):
    objects = ["$null", {"NS.objects": [plistlib.UID(i + 2) for i in range(len(ids))], "$class": plistlib.UID(1)},
               ]
    objects[1:1] = []
    for item in ids:
        objects.append({"NS.uuidbytes": item.bytes, "$class": plistlib.UID(len(ids) + 2)})
    objects.append({"$classname": "NSUUID"})
    return plistlib.dumps({"$version": 100000, "$archiver": "NSKeyedArchiver", "$top": {"root": plistlib.UID(1)}, "$objects": objects}, fmt=plistlib.FMT_BINARY)


def make_source(root, checks=(), repeats=()):
    (root / "Images").mkdir(parents=True)
    (root / "Drawings").mkdir()
    (root / "Freehand/Images").mkdir(parents=True)
    (root / "Images" / f"{PHOTO_ID}.JPG").write_bytes(b"photo")
    (root / "Images" / f"{PHOTO_ID}_thumb.jpg").write_bytes(b"thumb")
    (root / "Drawings/d.png").write_bytes(b"drawing")
    (root / "Freehand/canvas.json").write_text('{"version":1,"items":[],"greyscale":false,"camera":{"zoom":1,"offset":[0,0]}}')
    db = sqlite3.connect(root / "Library.store")
    db.executescript("""
    CREATE TABLE ZREFIMAGE (Z_PK INTEGER PRIMARY KEY, ZID BLOB, ZFILENAME TEXT, ZIMPORTEDAT REAL, ZCROPX REAL, ZCROPY REAL, ZCROPW REAL, ZCROPH REAL,
        ZROTATION INT, ZFLIPPED INT, ZNOFIGUREFOUND INT, ZISBENCHMARK INT, ZBENCHMARKSECONDS INT, ZBENCHMARKINTERVAL INT, ZBENCHMARKMARKEDAT REAL);
    CREATE TABLE ZIMAGESET (Z_PK INTEGER PRIMARY KEY, ZID BLOB, ZNAME TEXT, ZCREATEDAT REAL);
    CREATE TABLE Z_3IMAGES (Z_3SETS INT, Z_6IMAGES INT);
    CREATE TABLE ZCLASSPRESET (Z_PK INTEGER PRIMARY KEY, ZID BLOB, ZNAME TEXT, ZSTEPS BLOB);
    CREATE TABLE ZSESSIONRECORD (Z_PK INTEGER PRIMARY KEY, ZID BLOB, ZSTARTEDAT REAL, ZDRAWINGSECONDS INT, ZPOSECOUNT INT, ZIMAGEIDS BLOB);
    CREATE TABLE ZBENCHMARKATTEMPT (Z_PK INTEGER PRIMARY KEY, ZID BLOB, ZIMAGE INT, ZDATE REAL, ZSESSIONID BLOB, ZDRAWINGFILENAME TEXT);
    CREATE TABLE ZPLANCHECK (Z_PK INTEGER PRIMARY KEY, ZDAYKEY TEXT, ZBLOCKKEY TEXT, ZCHECKEDAT REAL);
    CREATE TABLE ZPLANREPEATRECORD (Z_PK INTEGER PRIMARY KEY, ZAFTERSEQUENCEINDEX INT, ZWEEKCOUNT INT, ZCREATEDAT REAL);
    """)
    db.execute("INSERT INTO ZREFIMAGE VALUES (1,?,?,?,0.1,0.2,0.3,0.4,90,1,0,1,120,10,?)", (PHOTO_ID.bytes, f"{PHOTO_ID}.JPG", EPOCH_2026, EPOCH_2026))
    db.execute("INSERT INTO ZIMAGESET VALUES (1,?,?,?)", (uuid.uuid4().bytes, "Figures", EPOCH_2026))
    db.execute("INSERT INTO Z_3IMAGES VALUES (1,1)")
    db.execute("INSERT INTO ZCLASSPRESET VALUES (1,?,?,?)", (uuid.uuid4().bytes, "Default class", b'[{"count":10,"seconds":30}]'))
    db.execute("INSERT INTO ZSESSIONRECORD VALUES (1,?,?,60,2,?)", (SESSION_ID.bytes, EPOCH_2026, keyed_archive([PHOTO_ID])))
    db.execute("INSERT INTO ZBENCHMARKATTEMPT VALUES (1,?,1,?,?,?)", (uuid.uuid4().bytes, EPOCH_2026, SESSION_ID.bytes, "d.png"))
    for i, (day, block) in enumerate(checks):
        db.execute("INSERT INTO ZPLANCHECK VALUES (?,?,?,?)", (i + 1, day, block, EPOCH_2026))
    for i, (after, count) in enumerate(repeats):
        db.execute("INSERT INTO ZPLANREPEATRECORD VALUES (?,?,?,?)", (i + 1, after, count, EPOCH_2026 + i))
    db.commit()
    db.close()


def items(library, name):
    return json.loads((library / "Progress" / name).read_text())["items"]


class ImportTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.root = Path(self.temp.name)
        self.source = self.root / "old"
        self.library = self.root / "Linework Library"

    def tearDown(self):
        self.temp.cleanup()

    def run_import(self, checks=(), repeats=(), replace=False, make=True):
        if make:
            make_source(self.source, checks, repeats)
        return importer.run(self.source, self.library, replace=replace)

    def test_ids_decode_from_blobs(self):
        self.run_import()
        self.assertEqual(items(self.library, "photos.json")[0]["id"], str(PHOTO_ID).upper())

    def test_session_image_ids_decode_from_keyed_archive(self):
        self.run_import()
        self.assertEqual(items(self.library, "sessions.json")[0]["photoIDs"], [str(PHOTO_ID).upper()])

    def test_preset_steps_decode_from_json(self):
        self.run_import()
        self.assertEqual(items(self.library, "presets.json")[0]["steps"], [{"count": 10, "seconds": 30}])

    def test_dates_convert_from_2001(self):
        self.run_import()
        self.assertTrue(items(self.library, "sessions.json")[0]["startedAt"].startswith("2026-10-29"))

    def test_tick_becomes_lesson_keeping_its_day(self):
        self.run_import(checks=[("2026-11-12", "focus")])
        tick = items(self.library, "checks.json")[0]
        self.assertEqual((tick["key"], tick["countsOn"]), ("w02-thu-focus", "2026-11-12"))

    def test_repeated_week_keys_carry_repeat_number(self):
        self.run_import(checks=[("2026-11-16", "focus"), ("2026-11-23", "focus")], repeats=[(1, 1)])
        keys = [t["key"] for t in items(self.library, "checks.json")]
        self.assertEqual(keys, ["w02-mon-focus-r1", "w03-mon-focus"])

    def test_forever_week_keys_use_the_date(self):
        self.run_import(checks=[("2027-08-02", "warmup")])
        self.assertEqual(items(self.library, "checks.json")[0]["key"], "f-2027-08-02-warmup")

    def test_week_one_is_the_plans_monday(self):
        self.run_import(repeats=[(2, 1)])
        plan = items(self.library, "plan.json")
        self.assertEqual(plan["weekOneStart"], "2026-11-02")
        self.assertEqual(plan["repeats"], [{"number": 1, "afterSequenceIndex": 2, "weekCount": 1}])

    def test_refuses_existing_library_without_replace(self):
        self.library.mkdir()
        (self.library / "library.json").write_text("{}")
        with self.assertRaises(SystemExit):
            self.run_import()

    def test_replace_only_touches_libraries_it_created(self):
        self.library.mkdir()
        (self.library / "library.json").write_text("{}")
        with self.assertRaises(SystemExit):
            self.run_import(replace=True)
        self.assertEqual((self.library / "library.json").read_text(), "{}")

    def test_rerun_with_replace_works(self):
        self.run_import()
        (self.library / "Photos/extra.jpg").write_bytes(b"x")
        self.run_import(replace=True, make=False)
        self.assertFalse((self.library / "Photos/extra.jpg").exists())
        self.assertEqual(len(items(self.library, "photos.json")), 1)

    def test_names_keep_extension_and_skip_thumbnails(self):
        self.run_import()
        self.assertEqual([p.name for p in (self.library / "Photos").iterdir()], ["photo-001.jpg"])

    def test_benchmark_attempt_and_set_come_across(self):
        self.run_import()
        data = items(self.library, "benchmarks.json")
        self.assertEqual(data["benchmarks"][0]["seconds"], 120)
        self.assertEqual(data["attempts"][0]["drawingFileName"], "d.png")
        self.assertTrue((self.library / "Drawings/d.png").exists())
        self.assertEqual(items(self.library, "sets.json")[0]["photoIDs"], [str(PHOTO_ID).upper()])

    def test_canvas_arrives_as_canvas(self):
        self.run_import()
        self.assertTrue((self.library / "Canvases/Canvas/canvas.json").exists())

    def test_source_is_untouched(self):
        make_source(self.source)
        before = {p: p.stat().st_mtime_ns for p in self.source.rglob("*") if p.is_file()}
        self.run_import(make=False)
        after = {p: p.stat().st_mtime_ns for p in self.source.rglob("*") if p.is_file() and "-shm" not in p.name and "-wal" not in p.name}
        self.assertEqual(before, after)

    def test_counts_compare_old_and_new(self):
        counts = self.run_import()
        self.assertEqual(counts["photos"], (1, 1))
        self.assertTrue(all(old == new for old, new in counts.values()))


if __name__ == "__main__":
    unittest.main()
