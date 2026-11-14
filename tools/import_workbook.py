"""Lossless, reviewable Excel import for the accident journal.

Default: read-only preview, no HTTP calls.  The source workbook is never saved.
Examples:
  python tools/import_workbook.py source.xlsx --timezone unknown --output preview.json
  python tools/import_workbook.py source.xlsx --timezone=+05:00 --output preview.json
  CASE_IMPORT_PASSWORD=... python tools/import_workbook.py source.xlsx --apply --url http://127.0.0.1:5090

Requires openpyxl. Unknown source timezone is preserved as source_local wall time;
it is never replaced with the machine timezone or silently labelled UTC.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import os
import posixpath
import re
import sys
from collections import Counter
from datetime import date, datetime, time, timedelta, timezone
from decimal import Decimal, InvalidOperation
from pathlib import Path
from urllib.error import HTTPError
from urllib.parse import urlsplit
from urllib.request import Request, build_opener, ProxyHandler
from xml.etree import ElementTree as ET
from zipfile import ZipFile

import openpyxl
from openpyxl.utils import get_column_letter
from openpyxl.utils.datetime import from_excel

EVENT_SHEETS = {"Аварии на сети СС", "Аварии на сети ФС"}
OVERVIEW_SHEET = "Основные события"
REFERENCE_SHEET = "Справочная информация"
NS = {"s": "http://schemas.openxmlformats.org/spreadsheetml/2006/main"}
REL_NS = "http://schemas.openxmlformats.org/package/2006/relationships"
OFFICE_REL = "http://schemas.openxmlformats.org/officeDocument/2006/relationships"


def json_value(value):
    """Tag types which JSON cannot represent by itself; never stringify everything."""
    if isinstance(value, datetime):
        return {"type": "datetime", "value": value.isoformat()}
    if isinstance(value, date):
        return {"type": "date", "value": value.isoformat()}
    if isinstance(value, time):
        return {"type": "time", "value": value.isoformat()}
    if isinstance(value, timedelta):
        return {"type": "duration", "seconds": value.total_seconds()}
    if isinstance(value, Decimal):
        return float(value)
    return value


def raw_xml_values(path: Path):
    """Keep exact numeric serials, including dates disguised as duration cells."""
    result = {}
    with ZipFile(path) as archive:
        workbook = ET.fromstring(archive.read("xl/workbook.xml"))
        relations = ET.fromstring(archive.read("xl/_rels/workbook.xml.rels"))
        targets = {x.attrib["Id"]: x.attrib["Target"] for x in relations.findall("{" + REL_NS + "}Relationship")}
        for sheet in workbook.find("s:sheets", NS):
            relationship = sheet.attrib["{" + OFFICE_REL + "}id"]
            target = targets[relationship]
            filename = target.lstrip("/") if target.startswith("/") else posixpath.normpath(posixpath.join("xl", target))
            values = {}
            tree = ET.fromstring(archive.read(filename))
            for cell in tree.findall(".//s:sheetData/s:row/s:c", NS):
                value = cell.find("s:v", NS)
                if value is not None:
                    values[cell.attrib["r"]] = {"rawValue": value.text, "rawType": cell.attrib.get("t", "n")}
            result[sheet.attrib["name"]] = values
    return result


def local_datetime(value, epoch=None):
    if isinstance(value, datetime):
        # Excel dates have no timezone. A nonstandard aware value is retained as
        # its explicit instant, but normal Excel cells always take this naive path.
        return value.replace(tzinfo=None)
    if isinstance(value, date):
        return datetime.combine(value, time())
    if isinstance(value, (float, int, Decimal)) and not isinstance(value, bool) and epoch is not None:
        converted = from_excel(float(value), epoch)
        return converted if isinstance(converted, datetime) else None
    if isinstance(value, str):
        text = value.strip()
        for parser in (lambda: datetime.fromisoformat(text),
                       lambda: datetime.strptime(text, "%d.%m.%Y %H:%M:%S"),
                       lambda: datetime.strptime(text, "%d.%m.%Y %H:%M")):
            try:
                return parser().replace(tzinfo=None)
            except ValueError:
                pass
    return None


def offset_timezone(value: str):
    if value == "unknown":
        return None
    if value in ("Z", "UTC"):
        return timezone.utc
    if not re.fullmatch(r"[+-]\d{2}:\d{2}", value):
        raise ValueError("timezone must be unknown, Z, UTC, or an explicit offset such as +05:00")
    sign = 1 if value[0] == "+" else -1
    hours, minutes = map(int, value[1:].split(":"))
    if hours > 14 or minutes > 59 or (hours == 14 and minutes != 0):
        raise ValueError("timezone offset must be within -14:00 to +14:00")
    return timezone(sign * timedelta(hours=hours, minutes=minutes))


def wall_string(value: datetime | None):
    return value.isoformat(timespec="milliseconds") if value is not None else None


def instant(value: datetime | None, zone):
    if value is None:
        return None
    return value.replace(tzinfo=zone).astimezone(timezone.utc).isoformat(timespec="milliseconds").replace("+00:00", "Z")


def text(value):
    if value is None:
        return None
    if isinstance(value, float) and value.is_integer():
        return str(int(value))
    return str(value)


def duration_seconds(value, raw: dict | None, issues: list[str], column: str):
    """Durations are decimal seconds, not TIME-of-day, and have no 838h cap."""
    if value is None:
        return None
    seconds = None
    # Raw Excel serial is authoritative for a numeric duration.  openpyxl may
    # convert hh:mm:ss cells >24h into an epoch-anchored datetime instead.
    if raw and raw.get("rawType") == "n" and raw.get("rawValue") is not None:
        try:
            seconds = Decimal(raw["rawValue"]) * Decimal(86400)
        except InvalidOperation:
            pass
    if seconds is None and isinstance(value, timedelta):
        seconds = Decimal(str(value.total_seconds()))
    if seconds is None and isinstance(value, time):
        seconds = Decimal(value.hour * 3600 + value.minute * 60 + value.second) + Decimal(value.microsecond) / 1000000
    if seconds is None and isinstance(value, str):
        match = re.fullmatch(r"\s*(\d+):([0-5]\d):([0-5]\d)(?:\.(\d+))?\s*", value)
        if match:
            hours, minutes, secs, fraction = match.groups()
            seconds = Decimal(int(hours) * 3600 + int(minutes) * 60 + int(secs))
            if fraction:
                seconds += Decimal("0." + fraction)
    if seconds is None and isinstance(value, (float, int)) and not isinstance(value, bool):
        seconds = Decimal(str(value)) * Decimal(86400)
    if seconds is None:
        issues.append("unparsed_duration:" + column)
        return None
    if not seconds.is_finite() or seconds < 0:
        issues.append("invalid_duration:" + column)
        return None
    return float(seconds.quantize(Decimal("0.000001")))


def normalize_event(values, raw_values, epoch, zone):
    issues = []
    value = list(values) + [None] * max(0, 19 - len(values))
    begin = local_datetime(value[10])
    if begin is None:
        return None, ["missing_or_invalid_begin_time"]
    end_raw = value[11]
    open_marker = isinstance(end_raw, str) and end_raw.strip().upper() in ("НВ", "Н.В.", "Н/В")
    end = None if open_marker or end_raw is None else local_datetime(end_raw)
    if end_raw is None:
        issues.append("end_time_missing_not_explicit_NV")
    elif end is None and not open_marker:
        issues.append("unparsed_end_time")
    if end is not None and end < begin:
        issues.append("end_before_begin")
    count_raw = value[6]
    is_sector = isinstance(count_raw, str) and count_raw.strip().upper() in ("С", "C", "С(СЕКТОР)", "С (СЕКТОР)")
    count = None
    if not is_sector and count_raw is not None:
        try:
            number = Decimal(str(count_raw).strip())
            if number == number.to_integral_value() and 1 <= number <= 10000:
                count = int(number)
            else:
                issues.append("invalid_station_count")
        except InvalidOperation:
            issues.append("unparsed_station_count")
    if count_raw is None:
        issues.append("station_count_unknown")
    seconds = {}
    for index, column in ((12, "M"), (13, "N"), (14, "O"), (15, "P"), (16, "Q")):
        seconds[column] = duration_seconds(value[index], raw_values.get(column), issues, column)
    if end is not None and seconds["O"] is not None and abs((end - begin).total_seconds() - seconds["O"]) > 1:
        issues.append("source_duration_differs_from_dates")
    if count is not None and seconds["P"] is not None and seconds["Q"] is not None and abs(count * seconds["P"] - seconds["Q"]) > 1:
        issues.append("source_total_downtime_mismatch")
    for index, field in ((1,"direction"),(3,"category"),(4,"network_type"),(5,"service_element"),(8,"alarm_type"),(9,"description")):
        if value[index] is None or not str(value[index]).strip():
            issues.append("missing_" + field)
    event = {
        "pw": text(value[0]), "direction": (text(value[1]) or "").strip(), "center": (text(value[2]) or "").strip(),
        "categoryCode": (text(value[3]) or "").strip(), "networkType": (text(value[4]) or "").strip(),
        "serviceElement": text(value[5]) or "", "stationCount": count, "isSector": is_sector,
        "equipment": text(value[7]), "alarmType": (text(value[8]) or "").strip(), "description": text(value[9]) or "",
        "timeBasis": "source_local" if zone is None else "utc", "beginTime": instant(begin, zone) if zone else None,
        "endTime": instant(end, zone) if zone else None, "beginTimeLocal": wall_string(begin) if zone is None else None,
        "endTimeLocal": wall_string(end) if zone is None else None,
        "batterySeconds": seconds["M"], "generatorSeconds": seconds["N"], "downtimeSeconds": seconds["P"],
        "notified": text(value[17]), "notes": text(value[18]),
    }
    return event, issues


def references(sheet):
    """Only explicit code correspondences present on the reference sheet."""
    result = {"directions": [], "centers": []}
    section = None
    for row in sheet.iter_rows(values_only=True):
        cells = list(row) + [None] * 3
        label = text(cells[0]) or ""
        if "Направления" in label:
            section = "directions"; continue
        if "Тех.центр" in label:
            section = "centers"; continue
        if label.strip().endswith(":"):
            section = None; continue
        if section is not None and all(cells[i] is not None for i in (0, 1, 2)):
            result[section].append({"code": str(cells[2]).strip(), "label": label.strip(), "externalCode": str(cells[1]).strip()})
    return result


def build_payload(path: Path, timezone_name="unknown", snapshot_override=None):
    zone = offset_timezone(timezone_name)
    source = openpyxl.load_workbook(path, read_only=True, data_only=False)
    cached = openpyxl.load_workbook(path, read_only=True, data_only=True)
    xml = raw_xml_values(path)
    rows = []
    sheet_snapshots = {}
    stats = {"bySheet": {}, "byKind": {}, "issueCounts": {}}
    try:
        for sheet in source.worksheets:
            cache = cached[sheet.title]
            # Only an explicitly labelled workbook cell can establish snapshot time.
            label = str(cache["K1"].value or "").strip().lower()
            if label == "текущее время":
                snapshot = local_datetime(cache["L1"].value, cached.epoch)
                if snapshot is not None:
                    sheet_snapshots[sheet.title] = snapshot
            nonempty = 0
            for row_index, (original_row, cached_row) in enumerate(zip(sheet.iter_rows(), cache.iter_rows()), 1):
                if not any(c.value is not None for c in original_row):
                    continue
                nonempty += 1
                values = [c.value for c in cached_row]
                raw = []
                for col_index, (original_cell, cached_cell) in enumerate(zip(original_row, cached_row), 1):
                    coordinate = get_column_letter(col_index) + str(row_index)
                    original_value = original_cell.value
                    formula = original_value if isinstance(original_value, str) and original_value.startswith("=") else None
                    item = {"coordinate": coordinate, "value": json_value(cached_cell.value), "formula": formula,
                            "numberFormat": getattr(original_cell, "number_format", "General"), "dataType": getattr(original_cell, "data_type", "n")}
                    item.update(xml.get(sheet.title, {}).get(coordinate, {}))
                    raw.append(item)
                kind = "reference" if sheet.title == REFERENCE_SHEET else "deferred"
                event = None
                issues = ["formula_without_cached_value:" + cell["coordinate"]
                          for cell in raw if cell["formula"] is not None and cell["value"] is None]
                if sheet.title in EVENT_SHEETS or sheet.title == OVERVIEW_SHEET:
                    begin_value = values[10] if len(values) > 10 else None
                    if local_datetime(begin_value) is not None:
                        by_column = {get_column_letter(i): xml.get(sheet.title, {}).get(get_column_letter(i)+str(row_index)) for i in range(1, 20)}
                        event, event_issues = normalize_event(values, by_column, cached.epoch, zone)
                        issues.extend(event_issues)
                        kind = "event" if sheet.title in EVENT_SHEETS else "overview"
                    elif row_index > 5 and len(values) > 9 and values[5] and values[9]:
                        issues.append("unrecognized_event_row_preserved_as_deferred")
                rows.append({"sheet": sheet.title, "row": row_index, "kind": kind, "rawValues": raw,
                             "event": event, "issues": issues})
            stats["bySheet"][sheet.title] = nonempty
        snapshot = None
        if snapshot_override:
            snapshot = local_datetime(snapshot_override)
            if snapshot is None:
                raise ValueError("snapshot-at must be an ISO local date/time present in the source or explicitly confirmed")
        elif sheet_snapshots:
            distinct = set(sheet_snapshots.values())
            if len(distinct) == 1:
                snapshot = next(iter(distinct))
        # A snapshot is stored per sheet: cached formula timestamps may differ.
        # Do not substitute the current machine clock for an Excel snapshot.
        ref = references(cached[REFERENCE_SHEET]) if REFERENCE_SHEET in cached.sheetnames else {"directions": [], "centers": []}
        payload = {"fileHash": hashlib.sha256(path.read_bytes()).hexdigest(), "sourceName": path.name,
                   "timeZone": None if zone is None else timezone_name, "snapshotAt": instant(snapshot, zone) if zone else None,
                   "snapshotAtLocal": wall_string(snapshot) if zone is None else None,
                   "sheets": [{"name": s.title, "snapshotAtLocal": wall_string(sheet_snapshots.get(s.title)) if zone is None else None,
                               "snapshotAt": instant(sheet_snapshots.get(s.title), zone) if zone else None}
                              for s in source.worksheets],
                   "rows": rows, "references": ref}
        stats["byKind"] = dict(Counter(r["kind"] for r in rows))
        stats["issueCounts"] = dict(Counter(issue.split(":", 1)[0] for r in rows for issue in r["issues"]))
        stats["sheetSnapshots"] = {s: wall_string(v) for s, v in sheet_snapshots.items()}
        return payload, stats
    finally:
        source.close(); cached.close()


def apply_payload(payload, base_url, username, password):
    if urlsplit(base_url).hostname not in ("localhost", "127.0.0.1", "::1"):
        raise ValueError("Live import is restricted to an explicitly chosen loopback host")
    if not password:
        raise ValueError("Set CASE_IMPORT_PASSWORD; credentials are never taken from source cells")
    opener = build_opener(ProxyHandler({}))
    def post(route, body, token=None):
        headers = {"Content-Type": "application/json", "Accept": "application/json"}
        if token:
            headers["Authorization"] = "Bearer " + token
        req = Request(base_url.rstrip("/")+route, data=json.dumps(body, ensure_ascii=False).encode("utf-8"), headers=headers, method="POST")
        try:
            with opener.open(req, timeout=120) as response:
                return json.load(response)
        except HTTPError as error:
            # Never echo response content: it may include source values or credentials.
            raise RuntimeError(f"{route} returned HTTP {error.code}; inspect the server locally") from error
    auth = post("/api/auth/login", {"username": username, "password": password})
    return post("/api/accidents/import", payload, auth["token"])


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("workbook", type=Path)
    parser.add_argument("--timezone", default="unknown")
    parser.add_argument("--snapshot-at", help="Explicitly confirmed source-local snapshot; otherwise only labelled K1/L1 cells are used")
    parser.add_argument("--output", type=Path, help="Save reviewable import JSON locally; it can contain sensitive source data")
    parser.add_argument("--apply", action="store_true", help="Actually POST after local review; default is dry run")
    parser.add_argument("--url", default="http://127.0.0.1:5090")
    parser.add_argument("--username", default="admin")
    args = parser.parse_args()
    if args.workbook.suffix.lower() not in (".xlsx", ".xlsm"):
        parser.error("Only xlsx/xlsm workbooks are supported")
    if args.output and (args.output.resolve() == args.workbook.resolve() or args.output.suffix.lower() != ".json"):
        parser.error("Output must be a separate .json file; the original workbook is never modified")
    payload, stats = build_payload(args.workbook, args.timezone, args.snapshot_at)
    if args.output:
        args.output.parent.mkdir(parents=True, exist_ok=True)
        args.output.write_text(json.dumps(payload, ensure_ascii=False, indent=2), encoding="utf-8")
    # Count-only diagnostics; do not print names, descriptions, workbook path, or raw rows.
    print(json.dumps({"rows": len(payload["rows"]), "byKind": stats["byKind"], "bySheetCounts": list(stats["bySheet"].values()),
                      "issueCounts": stats["issueCounts"], "timezoneKnown": payload["timeZone"] is not None,
                      "snapshotDetected": any(s["snapshotAt"] is not None or s["snapshotAtLocal"] is not None for s in payload["sheets"]),
                      "applied": False}, ensure_ascii=True))
    if args.apply:
        result = apply_payload(payload, args.url, args.username, os.environ.get("CASE_IMPORT_PASSWORD"))
        print(json.dumps({"applied": True, "resultCounts": {k:v for k,v in result.items() if isinstance(v,(int,bool))}}, ensure_ascii=True))


if __name__ == "__main__":
    try:
        main()
    except (ValueError, OSError, RuntimeError) as error:
        print(str(error), file=sys.stderr)
        raise SystemExit(1)
