"""Synthetic normalized-import checks against a disposable local case host."""
import csv
import hashlib
import io
import json
import os
import sys
import uuid
from urllib.error import HTTPError
from urllib.parse import urlencode, urlsplit
from urllib.request import Request, build_opener, ProxyHandler

sys.stdout.reconfigure(errors="backslashreplace")
BASE = os.environ.get("CASE_BASE_URL", "http://127.0.0.1:5090").rstrip("/")
if os.environ.get("ALLOW_CASE_TEST_WRITES") != "1" or urlsplit(BASE).hostname not in ("localhost", "127.0.0.1", "::1"):
    raise SystemExit("Disposable loopback database and ALLOW_CASE_TEST_WRITES=1 required")
PASSWORD = os.environ.get("DEMO_PASSWORD", "Demo12345!")
MARKER = "NORMQA_" + uuid.uuid4().hex[:12]
opener = build_opener(ProxyHandler({}))
checks = 0
ids = set()


def check(value, label):
    global checks
    if not value:
        raise AssertionError(label)
    checks += 1
    print("PASS " + label, flush=True)


def request(method, path, body=None, token=None, status=200):
    headers = {"Accept": "application/json"}
    if token:
        headers["Authorization"] = "Bearer " + token
    data = None
    if body is not None:
        headers["Content-Type"] = "application/json"
        data = json.dumps(body, ensure_ascii=False).encode("utf-8")
    try:
        response = opener.open(Request(BASE + path, data=data, headers=headers, method=method), timeout=45)
    except HTTPError as error:
        response = error
    with response:
        raw = response.read()
        content = json.loads(raw) if raw and "json" in response.headers.get("Content-Type", "") else raw.decode("utf-8-sig")
        if response.code != status:
            raise AssertionError(f"{method} {path}: {response.code}, expected {status}; {str(content)[:700]}")
        return content


def login(name):
    return request("POST", "/api/auth/login", {"username": name, "password": PASSWORD})["token"]


def event(**changes):
    result = {"pw": "NTT-SYNTHETIC", "direction": "СВН", "center": "ЕКБ", "categoryCode": "А0",
              "networkType": "GSM LTE", "serviceElement": MARKER, "stationCount": None, "isSector": False,
              "equipment": "SYNTHETIC MODEL A\nSYNTHETIC MODEL B", "alarmType": "ЭЛ", "description": MARKER,
              "timeBasis": "source_local", "beginTime": None, "endTime": None,
              "beginTimeLocal": "2026-08-30T10:00:00.000", "endTimeLocal": None,
              "batterySeconds": None, "generatorSeconds": 0, "downtimeSeconds": 4000000.125,
              "notified": "Synthetic team", "notes": "Synthetic data only"}
    result.update(changes)
    return result


def batch(known=False):
    rows = [event(), event(categoryCode="A0", stationCount=2, downtimeSeconds=0), event(categoryCode="В", isSector=True)]
    if known:
        rows = [event(categoryCode="3", timeBasis="utc", beginTime="2026-08-30T05:00:00Z", beginTimeLocal=None,
                      batterySeconds=0, generatorSeconds=None, downtimeSeconds=0)]
    source_rows = [{"sheet": "Аварии на сети СС", "row": i+5, "kind": "event",
                    "rawValues": [{"value": MARKER, "formula": None, "numberFormat": "@"}], "event": value, "issues": []}
                   for i, value in enumerate(rows)]
    if not known:
        source_rows.extend([
            {"sheet": "Аварии на сети СС", "row": 20, "kind": "event", "rawValues": ["invalid retained"],
             "event": event(beginTimeLocal="not-a-date"), "issues": ["synthetic invalid row"]},
            {"sheet": "Основные события", "row": 5, "kind": "overview", "rawValues": ["overview retained"], "event": event(), "issues": []},
            {"sheet": "Недозвоны", "row": 2, "kind": "deferred", "rawValues": [{"formula": "=1+2", "value": 3}], "event": None, "issues": []}])
    return {"fileHash": hashlib.sha256((MARKER+str(known)).encode()).hexdigest(), "sourceName": MARKER+".xlsx",
            "timeZone": "+05:00" if known else None, "snapshotAtLocal": None,
            "snapshotAt": None, "references": {"directions": [], "centers": []},
            "sheets": [{"name": name, "snapshotAtLocal": None if known else "2026-08-31T10:00:00.000",
                        "snapshotAt": "2026-08-31T05:00:00Z" if known else None}
                       for name in ("Аварии на сети СС", "Основные события", "Недозвоны")], "rows": source_rows}


admin = login("admin")
duty = login("duty")
engineer = login("engineer")
query = {"from": "2026-08-01T00:00:00Z", "to": "2026-09-01T00:00:00Z", "search": MARKER,
         "sourceScope": "historical", "pageSize": 100}


def listed(**changes):
    return request("GET", "/api/accidents?"+urlencode(dict(query, **changes)), token=admin)


try:
    unknown = batch()
    request("POST", "/api/accidents/import", unknown, duty, 403)
    check(True, "duty cannot import")
    result = request("POST", "/api/accidents/import", unknown, admin)
    check(result["eventRows"] == 3 and result["rejectedRows"] == 1, "valid rows promoted; malformed row rejected separately")
    check(result["sourceRows"] == 6 and result["overviewRows"] == 1 and result["deferredRows"] == 1, "overview and deferred raw rows retained")
    page = listed()
    ids.update(row["id"] for row in page["items"])
    check(page["total"] == 3, "overview and invalid rows do not inflate incident totals")
    again = request("POST", "/api/accidents/import", unknown, admin)
    check(again["alreadyImported"] and again["batchId"] == result["batchId"] and listed()["total"] == 3, "same SHA256 import is idempotent")
    by_code = {r["categoryCode"]: r for r in page["items"]}
    check(set(by_code) == {"А0", "A0", "В"}, "Latin and Cyrillic category codes remain distinct")
    check(all(r["category"] is None and r["needsCategoryMapping"] for r in page["items"]), "no invented mapping to categories 1-3")
    unknown_row = by_code["А0"]
    check(unknown_row["timeBasis"] == "source_local" and unknown_row["beginTime"] is None and unknown_row["beginTimeLocal"].startswith("2026-08-30T10:00:00"), "unknown timezone is preserved as local source time")
    check(unknown_row["durationSeconds"] == 86400, "historical open duration uses sheet snapshot, not today")
    check(unknown_row["downtimeSeconds"] == 4000000.125, "decimal duration exceeds legacy TIME cap without truncation")
    check(unknown_row["batterySeconds"] is None and unknown_row["generatorSeconds"] == 0, "null duration differs from zero")
    check(unknown_row["stationCount"] is None and not unknown_row["isSector"] and unknown_row["totalDowntimeSeconds"] is None, "unknown station count is not one station or sector")
    check(by_code["В"]["isSector"] and by_code["В"]["stationCount"] is None, "sector remains distinct from unknown station count")
    check(by_code["A0"]["totalDowntimeSeconds"] == 0, "known zero downtime remains zero")
    check("SYNTHETIC MODEL A" in unknown_row["equipment"] and "SYNTHETIC MODEL B" in unknown_row["equipment"], "both linked equipment models survive roundtrip")
    gsm = listed(networkType="GSM"); lte = listed(networkType="LTE")
    check(gsm["total"] == 3 and lte["total"] == 3, "each member of a multi-network event can filter the event")
    check(listed(categoryCode="А0")["total"] == 1 and listed(categoryCode="A0")["total"] == 1, "category-code filter respects distinct codes")
    known = request("POST", "/api/accidents/import", batch(True), admin)
    check(known["eventRows"] == 1, "known timezone import accepted")
    page = listed(); ids.update(r["id"] for r in page["items"])
    utc = next(r for r in page["items"] if r["timeBasis"] == "utc")
    check(utc["beginTime"].startswith("2026-08-30T05:00:00") and utc["beginTimeLocal"] is None and utc["durationSeconds"] == 86400, "known offset retains UTC instant and snapshot duration")
    dashboard = request("GET", "/api/accidents/dashboard?"+urlencode(query), token=engineer)
    check(dashboard["total"] == page["total"] == 4, "dashboard and list share filters")
    check(sum(r["count"] for r in dashboard["byCategory"]) == 4, "category aggregation does not duplicate joined networks/equipment")
    filtered = request("GET", "/api/accidents/dashboard?"+urlencode(dict(query,categoryCode="А0")), token=engineer)
    check(filtered["total"] == listed(categoryCode="А0")["total"] == 1, "dashboard category filter matches collection")
    check(listed(sourceScope="live")["total"] == 0, "historical source scope is distinct from live data")
    csv_text = request("GET", "/api/accidents/export.csv?"+urlencode(query), token=engineer)
    exported = list(csv.reader(io.StringIO(csv_text), delimiter=";"))
    check(len(exported)-1 == page["total"] and len(exported[0]) >= 20, "CSV matches normalized rows and retains original field columns")
    check({r[4] for r in exported[1:]} == {"А0","A0","В","3"}, "CSV preserves unmapped category codes")
    changed = dict(unknown_row, description=MARKER+" edited")
    edited = request("PUT", f"/api/accidents/{unknown_row['id']}", changed, duty)
    check(edited["version"] > unknown_row["version"], "editing imported normalized record updates version")
    request("PUT", f"/api/accidents/{unknown_row['id']}", changed, duty,409)
    check(True, "stale imported-record update rejected")
    request("POST", "/api/accidents/import", unknown, admin)
    check(listed()["total"] == 4, "reimport does not duplicate rows after edits")
    print(f"SUCCESS: {checks} normalized checks passed",flush=True)
finally:
    # Immutable batches/raw/audit stay available for provenance; only test facts
    # created with our unique marker are removed.
    remaining = listed()
    ids.update(r["id"] for r in remaining["items"])
    for accident_id in ids:
        row = request("GET", f"/api/accidents/{accident_id}", token=admin)
        if MARKER not in row["serviceElement"]:
            raise RuntimeError("Refusing cleanup without synthetic marker")
        request("DELETE", f"/api/accidents/{accident_id}?version={row['version']}", token=admin,status=204)
    check(listed()["total"] == 0, "only synthetic test facts removed; immutable source batches retained")
