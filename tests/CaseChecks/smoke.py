"""Integration checks against the real C# REST host, not a replacement test server.

Run only on a disposable case database:
  ALLOW_CASE_TEST_WRITES=1 DEMO_PASSWORD=Demo12345! python smoke.py
  CASE_BASE_URL defaults to http://127.0.0.1:5090

The test signs in through /api/auth/login. It never invents JWTs or disables auth.
It deletes only accident IDs returned by its own successful POST requests.
The temporary user is disabled, not deleted; the audit trail is retained.
Only Python's standard library is needed.
"""
import csv
import io
import json
import os
import sys
import uuid
from datetime import datetime, timedelta, timezone
from urllib.error import HTTPError
from urllib.parse import urlencode, urlsplit
from urllib.request import Request, build_opener, ProxyHandler

# Windows PowerShell may expose a legacy console encoding; diagnostics must not
# interrupt an otherwise successful test or its cleanup.
sys.stdout.reconfigure(errors="backslashreplace")
sys.stderr.reconfigure(errors="backslashreplace")

BASE = os.environ.get("CASE_BASE_URL", "http://127.0.0.1:5090").rstrip("/")
PASSWORD = os.environ.get("DEMO_PASSWORD", "Demo12345!")
if urlsplit(BASE).hostname not in ("localhost", "127.0.0.1", "::1"):
    raise SystemExit("Refusing to write to a non-loopback host. Use a disposable local case database.")
if os.environ.get("ALLOW_CASE_TEST_WRITES") != "1":
    raise SystemExit("Set ALLOW_CASE_TEST_WRITES=1 only after verifying this host uses a disposable test database.")

MAX_TIME = 3020399
MARKER = "RESTQA_" + uuid.uuid4().hex[:12]
TEMP_USERNAME = "qa_" + uuid.uuid4().hex[:12]
TEMP_PASSWORD = "RestQA12345!"
checks = 0
created = set()
temp_user_id = None


def check(condition, description):
    global checks
    if not condition:
        raise AssertionError(description)
    checks += 1
    print("PASS " + description, flush=True)


class Client:
    def __init__(self):
        self.token = None
        self.opener = build_opener(ProxyHandler({}))

    def request(self, method, path, body=None, expected=None):
        data = None if body is None else json.dumps(body, ensure_ascii=False).encode("utf-8")
        headers = {"Accept": "application/json"}
        if data is not None:
            headers["Content-Type"] = "application/json"
        if self.token:
            headers["Authorization"] = "Bearer " + self.token
        req = Request(BASE + path, data=data, headers=headers, method=method)
        try:
            response = self.opener.open(req, timeout=25)
        except HTTPError as error:
            response = error
        with response:
            status = response.code
            raw = response.read()
            content_type = response.headers.get("Content-Type", "")
            result = json.loads(raw) if raw and "json" in content_type else raw.decode("utf-8-sig")
            response_headers = dict(response.headers)
        if expected is not None:
            allowed = (expected,) if isinstance(expected, int) else expected
            if status not in allowed:
                raise AssertionError(f"{method} {path}: expected {allowed}, got {status}: {str(result)[:1200]}")
        return status, result, response_headers

    def login(self, username, password=PASSWORD):
        _, result, _ = self.request("POST", "/api/auth/login", {"username": username, "password": password}, 200)
        check(isinstance(result.get("token"), str) and len(result["token"]) > 30, "real login: " + username)
        self.token = result["token"]
        return result


def identifier(row):
    return row.get("id", row.get("userId"))


def items(result):
    return result if isinstance(result, list) else result["items"]


def iso(value):
    return value.astimezone(timezone.utc).isoformat(timespec="seconds").replace("+00:00", "Z")


NOW = datetime.now(timezone.utc).replace(microsecond=0)
FROM = NOW - timedelta(hours=12)
TO = NOW - timedelta(hours=4)


def accident(**overrides):
    value = {
        "pw": "https://example.invalid/ntt/" + MARKER,
        "direction": "СВН", "center": "ЕКБ", "category": 1,
        "networkType": "LTE", "serviceElement": MARKER,
        "stationCount": 3, "isSector": False, "equipment": "Тестовое оборудование",
        "alarmType": "ЭЛ", "description": "Проверка полного контракта " + MARKER,
        "beginTime": iso(NOW - timedelta(hours=10)), "endTime": None,
        "batterySeconds": 3600, "generatorSeconds": 1800, "downtimeSeconds": 1200,
        "notified": "Инженер; дежурный", "notes": "Строка 1\nСтрока 2, с кавычками \"тест\"",
        "version": 0,
    }
    value.update(overrides)
    return value


def create(client, data):
    _, row, headers = client.request("POST", "/api/accidents", data, 201)
    check(isinstance(row, dict) and isinstance(row.get("id"), int), "POST returns persisted accident and ID")
    created.add(row["id"])
    return row


def get(client, accident_id):
    return client.request("GET", "/api/accidents/" + str(accident_id), expected=200)[1]


def put(client, row, **changes):
    body = accident()
    for key in body:
        if key in row:
            body[key] = row[key]
    body.update(changes)
    return client.request("PUT", "/api/accidents/" + str(row["id"]), body)


def user_list(admin):
    return items(admin.request("GET", "/api/users", expected=200)[1])


def user_by_id(admin, user_id):
    return next(u for u in user_list(admin) if identifier(u) == user_id)


def csv_rows(client, query):
    status, text, headers = client.request("GET", "/api/accidents/export.csv?" + urlencode(query), expected=200)
    check("text/csv" in headers.get("Content-Type", ""), "CSV content type")
    check("attachment" in headers.get("Content-Disposition", ""), "CSV download header")
    rows = list(csv.reader(io.StringIO(text), delimiter=";"))
    if rows and len(rows[0]) == 1:
        rows = list(csv.reader(io.StringIO(text), delimiter=","))
    return rows


def matching_csv(rows):
    return [row for row in rows[1:] if any(MARKER in cell for cell in row)]


anonymous = Client()
admin = Client()
duty = Client()
engineer = Client()
temporary = Client()
try:
    for route in ("/api/accidents", "/api/accidents/options", "/api/accidents/1", "/api/accidents/export.csv", "/api/users", "/api/auth/me"):
        check(anonymous.request("GET", route)[0] == 401, "anonymous denied: " + route)
    check(anonymous.request("POST", "/api/auth/login", {"username": "admin", "password": "wrong-password"})[0] == 401, "wrong password rejected")
    admin.login("admin"); duty.login("duty"); engineer.login("engineer")
    for client, role in ((admin, "admin"), (duty, "duty"), (engineer, "engineer")):
        me = client.request("GET", "/api/auth/me", expected=200)[1]
        check(me["role"] == role, "auth/me role: " + role)
    check(engineer.request("GET", "/api/users")[0] == 403, "engineer cannot list users")
    check(duty.request("GET", "/api/users")[0] == 403, "duty cannot list users")
    check(engineer.request("GET", "/api/accidents/audit")[0] == 403, "engineer cannot read audit")
    check(engineer.request("POST", "/api/accidents", accident())[0] == 403, "engineer cannot create via direct REST")
    _, options, _ = admin.request("GET", "/api/accidents/options", expected=200)
    check(isinstance(options, dict), "dropdown options are returned by API")

    # A complete DTO round trip, server-side formulas, and fields from the PDF.
    body = accident(durationSeconds=999999, totalDowntimeSeconds=999999)
    row = create(duty, body)
    saved = get(engineer, row["id"])
    for key in ("pw", "direction", "center", "category", "networkType", "serviceElement", "stationCount", "isSector", "equipment", "alarmType", "description", "batterySeconds", "generatorSeconds", "downtimeSeconds", "notified", "notes"):
        check(saved[key] == body[key], "round trip: " + key)
    check(saved["endTime"] is None, "ongoing accident is returned as null")
    check(saved["totalDowntimeSeconds"] == 3600, "server calculates 3 × 1200 = 3600 seconds")
    check(saved["durationSeconds"] >= 36000 and saved["durationSeconds"] < 36180, "server calculates duration and ignores client computed values")
    check(saved["createdBy"] is not None and saved["version"] >= 1, "server assigns author and version")
    check("serverTimeUtc" in saved, "API returns authoritative server time")
    check(engineer.request("PUT", "/api/accidents/" + str(row["id"]), accident(version=saved["version"]))[0] == 403, "engineer cannot update via direct REST")
    check(duty.request("DELETE", f"/api/accidents/{row['id']}?version={saved['version']}")[0] == 403, "duty cannot delete via direct REST")

    invalid = (
        {"direction": "made-up"}, {"center": "made-up"}, {"category": 0}, {"category": 4},
        {"networkType": "5G"}, {"alarmType": "made-up"}, {"serviceElement": ""},
        {"stationCount": 0}, {"stationCount": 10001}, {"stationCount": None, "isSector": False},
        {"stationCount": 3, "isSector": True}, {"batterySeconds": -1},
        {"generatorSeconds": MAX_TIME + 1}, {"downtimeSeconds": MAX_TIME + 1},
        {"stationCount": 10000, "downtimeSeconds": 3600}, {"description": "x" * 451},
        {"pw": "javascript:alert(1)"}, {"beginTime": iso(NOW + timedelta(days=1))},
        {"endTime": iso(NOW - timedelta(days=2))}, {"endTime": iso(NOW + timedelta(days=1))},
        {"beginTime": "not-a-date"}, {"beginTime": 123}, {"beginTime": {}},
    )
    for changed in invalid:
        check(duty.request("POST", "/api/accidents", accident(**changed))[0] == 400, "server validates: " + ",".join(changed))

    # Editing and optimistic concurrency: stale PUT and DELETE must not lose data.
    status, edited, _ = put(duty, saved, endTime=iso(NOW - timedelta(hours=6)), description="Closed " + MARKER)
    check(status == 200 and edited["version"] > saved["version"], "duty closes accident; version increases")
    check(edited["durationSeconds"] == 14400, "closed duration is exactly four hours")
    check(put(duty, saved, description="stale " + MARKER)[0] == 409, "stale PUT rejected")
    check(admin.request("DELETE", f"/api/accidents/{row['id']}?version={saved['version']}")[0] == 409, "stale DELETE rejected")
    check(get(admin, row["id"])["description"] == "Closed " + MARKER, "conflict did not overwrite saved data")
    sector = create(duty, accident(stationCount=None, isSector=True, serviceElement=MARKER + "_sector"))
    check(sector["isSector"] and sector["stationCount"] is None and sector["totalDowntimeSeconds"] is None, "sector is distinct from a full station")
    check(get(engineer, sector["id"])["totalDowntimeSeconds"] is None, "sector round trip")

    # Period overlap and the exclusive end boundary. All rows use a unique marker.
    spanning = create(duty, accident(beginTime=iso(FROM-timedelta(hours=2)), endTime=iso(TO+timedelta(hours=1)), serviceElement=MARKER+"_spanning"))
    ending_at_start = create(duty, accident(beginTime=iso(FROM-timedelta(hours=2)), endTime=iso(FROM), serviceElement=MARKER+"_ending_start"))
    starting_at_end = create(duty, accident(beginTime=iso(TO), endTime=iso(TO+timedelta(hours=1)), serviceElement=MARKER+"_starting_end"))
    query = {"from": iso(FROM), "to": iso(TO), "search": MARKER, "pageSize": 100}
    period = admin.request("GET", "/api/accidents?"+urlencode(query), expected=200)[1]
    period_ids = {i["id"] for i in period["items"]}
    check(spanning["id"] in period_ids, "period includes events that started before the window")
    check(ending_at_start["id"] not in period_ids and starting_at_end["id"] not in period_ids, "half-open period excludes both touching boundaries")
    check(row["id"] in period_ids, "period includes closed event inside window")
    for suffix in ("?from="+iso(FROM), "?from="+iso(TO)+"&to="+iso(FROM), "?from=bad&to=bad", "?from=2026-01-01&to=2026-01-02", "?page=0", "?pageSize=0", "?pageSize=10001"):
        check(admin.request("GET", "/api/accidents"+suffix)[0] == 400, "invalid collection parameters: "+suffix)
    only_open = dict(query, status="open")
    open_rows = admin.request("GET", "/api/accidents?"+urlencode(only_open), expected=200)[1]
    check(all(x["endTime"] is None for x in open_rows["items"]), "open status filter")

    # More than one page: stable ordering, no repeated IDs, export ignores pagination.
    for i in range(27):
        create(duty, accident(serviceElement=MARKER+f"_page_{i:02d}", category=2, direction="ХМЯН", center="СРГ"))
    page_query = {"search": MARKER+"_page_", "page": 1, "pageSize": 25, "direction":"ХМЯН", "category":2}
    page1 = admin.request("GET", "/api/accidents?"+urlencode(page_query), expected=200)[1]
    page2 = admin.request("GET", "/api/accidents?"+urlencode(dict(page_query,page=2)), expected=200)[1]
    check(page1["total"] == 27 and len(page1["items"]) == 25 and len(page2["items"]) == 2, "pagination: 27 records split 25+2")
    check(not ({x["id"] for x in page1["items"]} & {x["id"] for x in page2["items"]}), "stable pagination does not repeat rows")
    check("summary" in page1, "collection includes summary")
    exported = matching_csv(csv_rows(engineer, page_query))
    check(len(exported)==27, "CSV exports all filtered records, not just first page")
    exported_period = matching_csv(csv_rows(engineer,query))
    check(len(exported_period)==period["total"]+27, "CSV and JSON apply the same period/search filters")
    check(all(len(r)>=19 for r in exported_period), "CSV includes all PDF fields")

    # User administration and revocation are tested with a disposable account only.
    _, new_user, _ = admin.request("POST", "/api/users", {"username":TEMP_USERNAME,"password":TEMP_PASSWORD,"role":"engineer"}, 201)
    temp_user_id = identifier(new_user)
    check(temp_user_id is not None, "admin creates test user")
    temporary.login(TEMP_USERNAME,TEMP_PASSWORD)
    check(temporary.request("POST", "/api/accidents", accident())[0]==403, "new engineer is read-only")
    test_user = user_by_id(admin,temp_user_id)
    status, access, _ = admin.request("PUT", f"/api/users/{temp_user_id}/access", {"role":"duty","active":True,"version":test_user["version"]})
    check(status==200, "admin changes role")
    check(temporary.request("GET", "/api/auth/me")[0]==401, "role change revokes old JWT")
    temporary.login(TEMP_USERNAME,TEMP_PASSWORD)
    check(temporary.request("GET", "/api/auth/me",expected=200)[1]["role"]=="duty", "new JWT carries updated role")
    test_user = user_by_id(admin,temp_user_id)
    check(admin.request("PUT", f"/api/users/{temp_user_id}/access", {"role":"duty","active":False,"version":test_user["version"]})[0]==200, "admin disables test account")
    check(temporary.request("GET", "/api/auth/me")[0]==401, "disabled user JWT rejected")
    check(anonymous.request("POST", "/api/auth/login", {"username":TEMP_USERNAME,"password":TEMP_PASSWORD})[0]==401, "disabled user cannot sign in")
    me=admin.request("GET","/api/auth/me",expected=200)[1]
    own=user_by_id(admin,identifier(me))
    check(admin.request("PUT",f"/api/users/{identifier(me)}/access",{"role":"engineer","active":True,"version":own["version"]})[0]==409,"admin cannot demote self")
    check(admin.request("PUT",f"/api/users/{identifier(me)}/access",{"role":"admin","active":False,"version":own["version"]})[0]==409,"admin cannot disable self")
    check(admin.request("GET","/api/accidents/audit?page=1&pageSize=25")[0]==200,"admin can read audit")
    check(admin.request("GET","/api/accidents/2147483647")[0]==404,"missing accident returns 404")
    check(put(duty,dict(saved,id=2147483647))[0]==404,"updating missing accident returns 404")
    print(f"SUCCESS: {checks} checks passed; cleaning only {len(created)} test accidents",flush=True)
finally:
    cleanup_errors=[]
    if admin.token:
        for accident_id in sorted(created):
            try:
                status,current,_=admin.request("GET",f"/api/accidents/{accident_id}")
                if status==404:
                    continue
                if status!=200 or MARKER not in current.get("serviceElement",""):
                    raise RuntimeError("refusing cleanup: test marker missing")
                status,body,_=admin.request("DELETE",f"/api/accidents/{accident_id}?version={current['version']}")
                if status!=204:
                    raise RuntimeError(f"delete returned {status}: {body}")
            except Exception as error:
                cleanup_errors.append(f"accident {accident_id}: {error}")
        if temp_user_id is not None:
            try:
                current=user_by_id(admin,temp_user_id)
                if current["active"]:
                    admin.request("PUT",f"/api/users/{temp_user_id}/access",{"role":current["role"],"active":False,"version":current["version"]},200)
            except Exception as error:
                cleanup_errors.append("temporary user: "+str(error))
    if cleanup_errors:
        print("CLEANUP ERRORS:\n"+"\n".join(cleanup_errors),file=sys.stderr)
        raise RuntimeError("Test cleanup failed; inspect only IDs listed above")
    print("Cleanup complete. Test audit entries retained.",flush=True)
