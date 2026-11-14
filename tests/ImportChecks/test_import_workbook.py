"""Synthetic fixtures only: no customer workbook, names, phones or tickets."""
import contextlib
import hashlib
import importlib.util
import io
import json
import subprocess
import sys
import tempfile
import unittest
from datetime import datetime, timedelta
from pathlib import Path

import openpyxl

TOOL = next(parent / "tools" / "import_workbook.py" for parent in Path(__file__).resolve().parents if (parent / "tools" / "import_workbook.py").exists())
spec = importlib.util.spec_from_file_location("import_workbook", TOOL)
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)


def event(category="3", count=1, end="НВ"):
    return ["NTT-QA-1", "СВН", "ЕКБ", category, "GSM LTE", "NODE-QA — Тестовая БС", count,
            "Test model", "ЭЛ", "Тестовое событие", datetime(2026, 8, 30, 10, 0), end,
            None, 0, timedelta(days=2), timedelta(hours=1), timedelta(hours=1), "Тестовая группа", "Тест"]


class WorkbookImportTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(dir=Path.cwd())
        self.path = Path(self.temp.name) / "synthetic.xlsx"
        book = openpyxl.Workbook()
        book.remove(book.active)
        overview = book.create_sheet("Основные события")
        overview.append(event("А0", None))
        cell = book.create_sheet("Аварии на сети СС")
        cell["K1"] = "текущее время"; cell["L1"] = 46265.3699782407
        # The numbered template header must not become an event on 1900-01-11.
        cell["K4"] = 11; cell["F4"] = 6; cell["J4"] = 10
        cell.append(event("1", 3, datetime(2026, 8, 30, 11, 0)))
        cell.append(event("2", "С"))
        fiber = book.create_sheet("Аварии на сети ФС")
        fiber["K1"] = "текущее время"; fiber["L1"] = datetime(2026, 8, 31, 8, 52, 46, 240000)
        fiber.append(event("В", None))
        fiber.append(event("A0", None))
        fiber.append(event("B", None))
        # hh:mm:ss is intentionally not [h]: the raw serial must recover 26h,
        # even if openpyxl exposes an epoch-anchored datetime.
        fiber["M2"] = timedelta(hours=26, minutes=25, seconds=46, milliseconds=192)
        fiber["M2"].number_format = "hh:mm:ss"
        fiber["P3"] = timedelta(days=1000, seconds=1)
        fiber["P3"].number_format = "[h]:mm:ss"
        for name in ("Недозвоны", "Остановленные БС", "Запуск-вывод БС"):
            sheet = book.create_sheet(name)
            sheet["A1"] = "Сохранить для следующего этапа"
            sheet["B2"] = "=1+2"
        reference = book.create_sheet("Справочная информация")
        reference.append(["Направления:", "InfoSystem", "в сводке"])
        reference.append(["Северо-восточное направление", "СВ", "СВН"])
        reference.append(["Тех.центр:"])
        reference.append(["Екатеринбургский центр", "ЕК", "ЕКБ"])
        book.save(self.path); book.close()
        self.original_hash = hashlib.sha256(self.path.read_bytes()).hexdigest()

    def tearDown(self):
        self.temp.cleanup()

    def test_unknown_timezone_is_not_utc(self):
        payload, stats = module.build_payload(self.path)
        self.assertIsNone(payload["timeZone"])
        events = [r for r in payload["rows"] if r["kind"] == "event"]
        self.assertEqual(5, len(events))
        for row in events:
            value = row["event"]
            self.assertEqual("source_local", value["timeBasis"])
            self.assertIsNone(value["beginTime"])
            self.assertTrue(value["beginTimeLocal"].startswith("2026-08-30T10:00:00"))
        self.assertEqual(self.original_hash, payload["fileHash"])
        self.assertEqual(self.original_hash, hashlib.sha256(self.path.read_bytes()).hexdigest())

    def test_categories_and_unknown_count_are_preserved(self):
        payload, _ = module.build_payload(self.path)
        actual = {r["event"]["categoryCode"] for r in payload["rows"] if r["event"]}
        self.assertTrue({"1", "2", "А0", "A0", "В", "B"}.issubset(actual))
        fiber = [r["event"] for r in payload["rows"] if r["kind"] == "event" and r["sheet"] == "Аварии на сети ФС"]
        self.assertTrue(all(x["stationCount"] is None and not x["isSector"] for x in fiber))
        sector = next(r["event"] for r in payload["rows"] if r["event"] and r["event"]["isSector"])
        self.assertIsNone(sector["stationCount"])

    def test_large_and_epoch_anchored_durations(self):
        payload, _ = module.build_payload(self.path)
        fiber = [r for r in payload["rows"] if r["kind"] == "event" and r["sheet"] == "Аварии на сети ФС"]
        self.assertAlmostEqual(26*3600 + 25*60 + 46.192, fiber[0]["event"]["batterySeconds"], places=3)
        self.assertEqual(1000*86400 + 1, fiber[1]["event"]["downtimeSeconds"])
        self.assertEqual(0, fiber[0]["event"]["generatorSeconds"])
        self.assertIsNone(fiber[0]["event"]["endTimeLocal"])

    def test_overview_deferred_and_formulas_are_archived(self):
        payload, stats = module.build_payload(self.path)
        self.assertEqual(1, stats["byKind"]["overview"])
        self.assertTrue(all(r["kind"] != "event" for r in payload["rows"] if r["sheet"] == "Основные события"))
        for name in ("Недозвоны", "Остановленные БС", "Запуск-вывод БС"):
            rows = [r for r in payload["rows"] if r["sheet"] == name]
            self.assertEqual(2, len(rows))
            self.assertTrue(all(r["kind"] == "deferred" and r["event"] is None for r in rows))
            formula = next(c for r in rows for c in r["rawValues"] if c["formula"])
            self.assertEqual("=1+2", formula["formula"])
            self.assertIsNone(formula["value"])
        header = next(r for r in payload["rows"] if r["sheet"] == "Аварии на сети СС" and r["row"] == 4)
        self.assertEqual("deferred", header["kind"])

    def test_snapshot_per_sheet_not_an_invented_global_time(self):
        payload, _ = module.build_payload(self.path)
        self.assertIsNone(payload["snapshotAtLocal"])
        sheets = {x["name"]: x for x in payload["sheets"]}
        self.assertTrue(sheets["Аварии на сети СС"]["snapshotAtLocal"].startswith("2026-08-31T08:52:46"))
        self.assertEqual("2026-08-31T08:52:46.240", sheets["Аварии на сети ФС"]["snapshotAtLocal"])

    def test_confirmed_offset_converts_without_using_machine_timezone(self):
        payload, _ = module.build_payload(self.path, "+05:00")
        event = next(r["event"] for r in payload["rows"] if r["kind"] == "event")
        self.assertEqual("utc", event["timeBasis"])
        self.assertEqual("2026-08-30T05:00:00.000Z", event["beginTime"])
        self.assertIsNone(event["beginTimeLocal"])

    def test_mismatches_flagged_without_replacing_source_values(self):
        payload, _ = module.build_payload(self.path)
        row = next(r for r in payload["rows"] if r["kind"] == "event" and r["event"]["stationCount"] == 3)
        self.assertIn("source_total_downtime_mismatch", row["issues"])
        self.assertIn("source_duration_differs_from_dates", row["issues"])
        self.assertEqual(3600, row["event"]["downtimeSeconds"])

    def test_reference_codes_and_raw_numeric_are_retained(self):
        payload, _ = module.build_payload(self.path)
        self.assertEqual("СВ", payload["references"]["directions"][0]["externalCode"])
        self.assertEqual("ЕКБ", payload["references"]["centers"][0]["code"])
        l1 = next(c for r in payload["rows"] if r["sheet"] == "Аварии на сети СС" and r["row"] == 1 for c in r["rawValues"] if c["coordinate"] == "L1")
        self.assertEqual("n", l1["rawType"])
        self.assertTrue(l1["rawValue"].startswith("46265."))

    def test_preview_cli_never_posts_by_default(self):
        output = Path(self.temp.name) / "preview.json"
        result = subprocess.run([sys.executable, str(TOOL), str(self.path), "--output", str(output), "--url", "http://127.0.0.1:1"], capture_output=True, text=True)
        self.assertEqual(0, result.returncode, result.stderr)
        self.assertFalse(json.loads(result.stdout)["applied"])
        self.assertTrue(output.exists())
        self.assertNotIn("NODE-QA", result.stdout)
        self.assertEqual(self.original_hash, hashlib.sha256(self.path.read_bytes()).hexdigest())

    def test_live_destination_and_offset_validation(self):
        with self.assertRaises(ValueError):
            module.apply_payload({}, "https://example.com", "admin", "secret")
        for invalid in ("+15:00", "+14:01", "local", "+05:60"):
            with self.assertRaises(ValueError):
                module.offset_timezone(invalid)


if __name__ == "__main__":
    unittest.main(verbosity=2)
