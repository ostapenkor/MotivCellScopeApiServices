"""Real HTTP checks for the synchronous API; Python standard library only.

Run against a disposable local database after provisioning operator/engineer users:
  TEST_PASSWORD=... ALLOW_TEST_WRITES=1 python smoke.py
TEST_BASE_URL defaults to http://127.0.0.1:8080. TEST_OPERATOR/TEST_ENGINEER
default to checks_operator/checks_engineer. TEST_USERNAME aliases TEST_OPERATOR.
RUN_MAINTENANCE_TESTS=1 additionally invokes each monitoring maintenance wrapper
once; these operations can change seeded KPI/anomaly data throughout the database.
The script never calls /api/admin, creates users, fabricates JWTs, or prints secrets.
Only IDs returned by this run's incident POSTs are deleted, including on failure.
Login and incident audit records intentionally remain for separate SQL verification.
"""

import csv
import io
import ipaddress
import json
import os
import sys
import uuid
from datetime import datetime, timedelta, timezone
from decimal import Decimal
from urllib.error import HTTPError, URLError
from urllib.parse import urlencode, urlsplit
from urllib.request import HTTPRedirectHandler, ProxyHandler, Request, build_opener


BASE = os.environ.get("TEST_BASE_URL", "http://127.0.0.1:8080").rstrip("/")
PASSWORD = os.environ.get("TEST_PASSWORD", "")
OPERATOR = os.environ.get("TEST_OPERATOR", os.environ.get("TEST_USERNAME", "checks_operator"))
ENGINEER = os.environ.get("TEST_ENGINEER", "checks_engineer")
MARKER = "SYNCQA_" + uuid.uuid4().hex[:12]
UTC = timezone.utc
checks = 0
failures = []
created = set()


class StopSuite(Exception):
    pass


class NoRedirect(HTTPRedirectHandler):
    def redirect_request(self, req, fp, code, msg, headers, newurl):
        return None


def guard():
    parts = urlsplit(BASE)
    try:
        loopback = parts.hostname == "localhost" or ipaddress.ip_address(parts.hostname or "").is_loopback
    except ValueError:
        loopback = False
    if (not loopback or parts.scheme not in ("http", "https") or parts.username
            or parts.password or parts.query or parts.fragment or parts.path not in ("", "/")):
        raise SystemExit("Refusing writes: TEST_BASE_URL must be a loopback HTTP(S) origin.")
    if os.environ.get("ALLOW_TEST_WRITES") != "1":
        raise SystemExit("Set ALLOW_TEST_WRITES=1 only for a disposable local test database.")
    if not PASSWORD:
        raise SystemExit("TEST_PASSWORD is required; provision the test users before running.")


def check(condition, name, required=False):
    global checks
    checks += 1
    if not condition:
        failures.append(name)
        if required:
            raise StopSuite()


class Response:
    def __init__(self, status, headers, raw):
        self.status, self.headers, self.raw = status, headers, raw
        try:
            self.data = json.loads(raw.decode("utf-8-sig")) if raw else None
        except (ValueError, UnicodeError):
            self.data = None


class Client:
    def __init__(self):
        self.token = None
        self.opener = build_opener(ProxyHandler({}), NoRedirect())

    def request(self, method, path, body=None):
        headers = {"Accept": "application/json"}
        data = None
        if body is not None:
            data = json.dumps(body, ensure_ascii=False).encode("utf-8")
            headers["Content-Type"] = "application/json"
        if self.token:
            headers["Authorization"] = "Bearer " + self.token
        request = Request(BASE + path, data=data, headers=headers, method=method)
        try:
            with self.opener.open(request, timeout=30) as response:
                return Response(response.status, response.headers, response.read())
        except HTTPError as response:
            return Response(response.code, response.headers, response.read())


anonymous, operator, engineer = Client(), Client(), Client()


def call(name, method, path, body=None, status=200, client=operator):
    response = client.request(method, path, body)
    if response.status != status:
        check(False, name + " HTTP " + str(status), required=True)
    return response


def get(path, query=None, client=operator):
    return client.request("GET", path + ("?" + urlencode(query) if query else ""))


def iso(value):
    return value.astimezone(UTC).isoformat(timespec="microseconds").replace("+00:00", "Z")


def instant(value):
    return datetime.fromisoformat(value.replace("Z", "+00:00")).astimezone(UTC)


def dec(value):
    return Decimal(str(value or 0))


def ratio(numerator, denominator):
    return None if not dec(denominator) else 100 * dec(numerator) / dec(denominator)


def close(actual, expected):
    return actual is None if expected is None else actual is not None and abs(dec(actual) - dec(expected)) <= Decimal("0.00011")


def csv_rows(response):
    return list(csv.reader(io.StringIO(response.raw.decode("utf-8-sig"), newline=""), delimiter=";"))


def page(name, path, query):
    response = get(path, query)
    check(response.status == 200 and isinstance(response.data, dict)
          and isinstance(response.data.get("items"), list), name, required=True)
    return response.data


def validation(name, method, path, body=None, query=None, field=None):
    if query:
        path += "?" + urlencode(query)
    response = operator.request(method, path, body)
    check(response.status == 400 and isinstance(response.data, dict)
          and response.data.get("error") == "validation_error"
          and (field is None or response.data.get("field") == field), name)
    # A defective validation handler must not leave a successfully inserted fixture.
    if method == "POST" and response.status == 201 and isinstance(response.data, dict):
        if isinstance(response.data.get("incidentId"), int):
            created.add(response.data["incidentId"])


def login(client, username, role):
    response = call(role + " real login", "POST", "/api/auth/login",
                    {"username": username, "password": PASSWORD}, client=client)
    check(isinstance(response.data, dict) and isinstance(response.data.get("token"), str)
          and response.data.get("role") == role, role + " login role", required=True)
    client.token = response.data["token"]
    response = client.request("GET", "/api/auth/me")
    check(response.status == 200 and response.data.get("username") == username
          and response.data.get("role") == role and response.data.get("userId", 0) > 0,
          role + " current identity", required=True)


def main():
    response = call("anonymous health", "GET", "/api/health", client=anonymous)
    check(response.data.get("status") == "ok" and response.data.get("database") == "ok", "database health")
    check(all(anonymous.request("GET", path).status == 401 for path in
              ("/api/incidents", "/api/reference/all", "/api/monitoring/summary", "/api/auth/me")),
          "protected endpoints reject anonymous requests")
    login(operator, OPERATOR, "operator")
    login(engineer, ENGINEER, "engineer")

    reference = call("reference dictionaries", "GET", "/api/reference/all").data
    dictionary_keys = {"regions": "regionId", "directions": "directionId", "technicalCenters": "techCenterId",
                       "priorities": "priorityId", "networkTypes": "networkTypeId",
                       "equipmentTypes": "equipmentTypeId", "alarmTypes": "alarmTypeId"}
    check(all(reference.get(key) and all(isinstance(row.get(id_key), int) and row[id_key] > 0
                                       for row in reference[key]) for key, id_key in dictionary_keys.items()),
          "all form dictionaries contain real positive identifiers", required=True)
    check({x["code"] for x in reference["categories"]} ==
          {"power", "transport", "hardware", "software", "planned", "client", "external", "other"}
          and {x["code"] for x in reference["severities"]} == {"low", "medium", "high", "critical"},
          "category and severity choices")
    regions = call("legacy regions", "GET", "/api/regions").data
    check({r["regionId"] for r in regions} == {r["regionId"] for r in reference["regions"]}, "legacy region identifiers")
    response = call("legacy vendors retained", "GET", "/api/vendors", status=410)
    check(response.data.get("error") == "vendors_removed", "removed vendor dictionary is explicit")

    now = datetime.now(UTC).replace(microsecond=0)
    period = {"from": iso(now - timedelta(days=7)), "to": iso(now), "pageSize": 200}
    network = page("network element list", "/api/monitoring/network-elements", period)
    check(network["total"] == len(network["items"]) and network["total"] > 0, "network inventory untruncated", required=True)
    check(network.get("coordinatesHidden") is True and network.get("coordinatesExposed") is False
          and all(r.get("lat") is None and r.get("lon") is None and r.get("address") is None
                  and r.get("coordinatesHidden") is True for r in network["items"]), "default coordinate privacy")
    bts = page("legacy BTS list", "/api/bts", period)
    check(bts["items"] and all(r["type"] == "bts" for r in bts["items"]), "BTS subset contains only BTS", required=True)
    element = bts["items"][0]
    center = next((r for r in reference["technicalCenters"] if r["techCenterId"] == element.get("techCenterId")), None)
    check(center is not None, "BTS technical center is a real reference", required=True)
    begin = (now - timedelta(hours=60)).replace(microsecond=123456)
    end = begin + timedelta(hours=48)
    literal = MARKER + "%_="
    payload = {
        "pw": literal, "directionId": center["directionId"], "techCenterId": center["techCenterId"],
        "priorityId": reference["priorities"][0]["priorityId"],
        "networkTypeId": reference["networkTypes"][0]["networkTypeId"],
        "servElem": "client supplied value must be replaced", "numbRbs": "003",
        "equipmentTypeId": reference["equipmentTypes"][0]["equipmentTypeId"],
        "alarmTypeId": reference["alarmTypes"][0]["alarmTypeId"],
        "descr": 'Smoke;"quoted"\nПроверка', "beginTime": begin.astimezone(timezone(timedelta(hours=8))).isoformat(),
        "endTime": end.astimezone(timezone(timedelta(hours=8))).isoformat(),
        "akb": "48:01:02", "dgu": "838:59:59", "prostoy": "49:02:03",
        "alarmWho": "Дежурный " + MARKER, "descNotice": '=1+1; "notice"\nnext line',
        "adminDistrict": "Тестовый район", "networkElementId": element["id"], "regionId": element["regionId"],
        "severity": "critical", "category": "power", "lenght": "00:00:01", "commonProstoy": "00:00:01"
    }
    response = engineer.request("POST", "/api/incidents", payload)
    if response.status == 201 and isinstance(response.data, dict):
        created.add(response.data["incidentId"])
    check(response.status == 403, "engineer cannot create incidents")
    response = call("operator creates incident", "POST", "/api/incidents", payload, status=201)
    first = response.data
    check(isinstance(first.get("incidentId"), int), "created incident has identifier", required=True)
    first_id = first["incidentId"]
    created.add(first_id)
    check(response.headers.get("Location", "").endswith("/api/incidents/" + str(first_id)), "created resource location")
    plain = ("pw", "directionId", "techCenterId", "priorityId", "networkTypeId", "equipmentTypeId", "alarmTypeId",
             "descr", "akb", "dgu", "prostoy", "alarmWho", "descNotice", "adminDistrict", "networkElementId", "regionId",
             "severity", "category")
    check(all(first.get(key) == payload[key] for key in plain) and first.get("numbRbs") == "3"
          and first.get("servElem") == element.get("fullName"), "all writable incident fields round trip")
    check(instant(first["beginTime"]) == begin.replace(microsecond=0)
          and instant(first["endTime"]) == end.replace(microsecond=0), "explicit offsets normalize to UTC whole seconds")
    downtime = 49 * 3600 + 2 * 60 + 3
    check(first.get("durationSeconds") == 48 * 3600 and first.get("lenght") == "48:00:00"
          and first.get("commonProstoySeconds") == downtime * 3 and first.get("commonProstoy") == "147:06:09",
          "long TIME and server computed fields ignore client values")
    check(first.get("createdByUsername") == OPERATOR and first.get("createdByUserId", 0) > 0
          and instant(first["updatedAt"]) >= instant(first["createdAt"]), "server actor and timestamps")
    detail = call("legacy accident detail", "GET", "/api/accidents/" + str(first_id), client=engineer).data
    check(detail == first, "engineer reads identical detail through compatibility alias")
    response = call("incident anomaly links", "GET", f"/api/incidents/{first_id}/anomalies")
    check(isinstance(response.data, list) and not response.data, "new fixture has no linked anomalies")

    nullable = {key: None for key in ("pw", "directionId", "techCenterId", "priorityId", "networkTypeId", "servElem", "numbRbs",
                                    "equipmentTypeId", "alarmTypeId", "descr", "akb", "dgu", "alarmWho", "descNotice",
                                    "adminDistrict", "networkElementId", "regionId")}
    nullable.update({"pw": MARKER + " nullable", "beginTime": iso(begin + timedelta(hours=1)),
                     "endTime": None, "prostoy": "01:00:00", "severity": "low", "category": "other"})
    second = call("nullable incident create via alias", "POST", "/api/accidents", nullable, status=201).data
    second_id = second["incidentId"]
    created.add(second_id)
    check(second.get("numbRbs") is None and second.get("commonProstoy") is None
          and second.get("commonProstoySeconds") is None and second.get("endTime") is None
          and second.get("status") == "open" and second.get("directionId") is None, "nullable counts and open incident semantics")
    sectors = dict(nullable, numbRbs="C", expectedUpdatedAt=second["updatedAt"])
    second = call("sector update", "PUT", f"/api/incidents/{second_id}", sectors).data
    check(second.get("numbRbs") == "С" and second.get("commonProstoy") is None
          and second.get("commonProstoySeconds") is None, "sector marker never invents a station count")
    response = engineer.request("PUT", f"/api/incidents/{first_id}", payload)
    check(response.status == 403 and engineer.request("DELETE", f"/api/incidents/{first_id}").status == 403,
          "engineer cannot update or delete")
    updated_input = dict(payload, numbRbs="4", expectedUpdatedAt=first["updatedAt"])
    updated = call("concurrent-safe update via alias", "PUT", f"/api/accidents/{first_id}", updated_input).data
    check(updated.get("commonProstoySeconds") == downtime * 4 and updated["updatedAt"] != first["updatedAt"], "update recomputes downtime and advances version")
    response = operator.request("PUT", f"/api/incidents/{first_id}", updated_input)
    check(response.status == 409 and response.data.get("error") == "concurrent_update", "stale update rejected")
    response = operator.request("DELETE", f"/api/incidents/{first_id}?" + urlencode({"expectedUpdatedAt": first["updatedAt"]}))
    check(response.status == 409 and response.data.get("error") == "concurrent_update", "stale delete rejected")

    fixture_filter = dict(period, search=MARKER, sort="begin_time", order="asc", pageSize=1)
    first_page = page("filtered first incident page", "/api/incidents", fixture_filter)
    second_page = page("filtered second incident page", "/api/accidents", dict(fixture_filter, page=2))
    check(first_page["total"] == second_page["total"] == 2 and first_page["pageSize"] == 1
          and first_page["items"][0]["incidentId"] == first_id and second_page["items"][0]["incidentId"] == second_id,
          "search and stable pagination isolate owned fixtures")
    literal_page = page("literal wildcard search", "/api/incidents", dict(period, search=literal))
    check(literal_page["total"] == 1 and literal_page["items"][0]["incidentId"] == first_id,
          "percent underscore and escape characters are literal")
    filtered = page("combined incident filters", "/api/incidents", dict(period, search=MARKER, category="power",
                    severity="critical", status="closed", directionId=payload["directionId"], techCenterId=payload["techCenterId"],
                    priorityId=payload["priorityId"], networkTypeId=payload["networkTypeId"], alarmTypeId=payload["alarmTypeId"], regionId=payload["regionId"]))
    check(filtered["total"] == 1 and filtered["items"][0]["incidentId"] == first_id, "all incident filters match the same row")
    events = page("legacy report events", "/api/reports/events", dict(period, search=MARKER))
    check(events["total"] == 2, "legacy events share incident filters")
    exports = [get(path, dict(period, search=MARKER, pageSize=1)) for path in
               ("/api/incidents/export", "/api/accidents/export", "/api/reports/export")]
    check(all(r.status == 200 and r.raw.startswith(b"\xef\xbb\xbf")
              and "text/csv" in r.headers.get("Content-Type", "")
              and "attachment" in r.headers.get("Content-Disposition", "") for r in exports), "all CSV aliases return UTF8 BOM downloads")
    tables = [csv_rows(r) for r in exports]
    check(all(len(t) == 3 and len(t[0]) == 23 for t in tables)
          and tables[0] == tables[1] == tables[2], "CSV uses semicolons and all filtered rows without pagination")
    header = tables[0][0]
    exported = next(row for row in tables[0][1:] if row[header.index("pw")] == literal)
    check(exported[header.index("descr")] == payload["descr"]
          and exported[header.index("descNotice")] == "'" + payload["descNotice"], "CSV preserves quoted multiline text and neutralizes formulas")
    summary = get("/api/reports/incidents-summary", dict(period, search=MARKER, groupBy="category"))
    check(summary.status == 200 and summary.data["total"] == 2
          and summary.data["knownDowntimeSeconds"] == downtime * 4 and summary.data["excludedDowntimeCount"] == 1,
          "report totals include only known station downtime")
    check(sum(g["count"] for g in summary.data["groups"]) == 2
          and {g["key"] for g in summary.data["groups"]} == {"power", "other"}, "report grouping partitions filtered incidents")
    for group in ("severity", "direction", "techCenter", "alarmType"):
        response = get("/api/reports/incidents-summary", dict(period, search=MARKER, groupBy=group))
        check(response.status == 200 and sum(r["count"] for r in response.data["groups"]) == 2,
              "report groupBy " + group)
    response = get("/api/reports/incidents-export", dict(period, search=MARKER, groupBy="category"))
    rows = csv_rows(response)
    check(response.status == 200 and response.raw.startswith(b"\xef\xbb\xbf") and len(rows) == 3
          and sum(int(r[3]) for r in rows[1:]) == 2, "grouped CSV matches summary filters")
    periods = call("available report periods", "GET", "/api/reports/available-periods").data
    check(periods["total"] >= 2 and instant(periods["from"]) <= begin
          and sum(row["count"] for row in periods["months"]) == periods["total"], "available periods cover stored incidents")

    invalid_payloads = [
        ("offset is mandatory", {"beginTime": "2026-01-01T00:00:00"}, "beginTime"),
        ("dates require seconds", {"beginTime": "2026-01-01T00:00Z"}, "beginTime"),
        ("end before begin", {"endTime": iso(begin - timedelta(seconds=1))}, "endTime"),
        ("future begin rejected", {"beginTime": iso(now + timedelta(hours=1)), "endTime": None}, "beginTime"),
        ("future end rejected", {"endTime": iso(now + timedelta(hours=1))}, "endTime"),
        ("TIME bounds", {"akb": "839:00:00"}, "akb"),
        ("count zero", {"numbRbs": "0"}, "numbRbs"),
        ("count arbitrary text", {"numbRbs": "unknown"}, "numbRbs"),
        ("category domain", {"category": "A0"}, "category"),
        ("severity domain", {"severity": "1"}, "severity"),
        ("unknown FK", {"networkElementId": 4294967295}, "networkElementId"),
        ("zero FK", {"priorityId": 0}, "priorityId"),
        ("text length", {"descr": "x" * 451}, "descr"),
    ]
    for name, change, field in invalid_payloads:
        validation(name, "POST", "/api/incidents", dict(payload, **change), field=field)
    validation("list period pair", "GET", "/api/incidents", query={"from": period["from"]}, field="from")
    validation("invalid pagination", "GET", "/api/incidents", query={"pageSize": 201}, field="pageSize")
    validation("group whitelist", "GET", "/api/reports/incidents-summary", query=dict(period, groupBy="region"), field="groupBy")
    check(operator.request("GET", "/api/incidents/18446744073709551615").status == 404
          and operator.request("GET", "/api/incidents/not-an-id").status == 400,
          "missing and malformed incident IDs are distinguished")

    monitoring(period, element, now)
    response = operator.request("DELETE", f"/api/incidents/{first_id}?" + urlencode({"expectedUpdatedAt": updated["updatedAt"]}))
    check(response.status == 204, "operator deletes owned fixture")
    if response.status == 204:
        created.remove(first_id)
    check(operator.request("GET", f"/api/accidents/{first_id}").status == 404
          and operator.request("GET", f"/api/incidents/{first_id}/anomalies").status == 404,
          "deleted incident and relation return 404")


def monitoring(period, element, now):
    summary = get("/api/monitoring/summary", period)
    legacy = get("/api/dashboard/summary", period)
    check(summary.status == legacy.status == 200 and summary.data == legacy.data, "legacy dashboard shares monitoring summary", required=True)
    summary = summary.data
    check(sum(summary["networkStatusCounts"].values()) == summary["networkElementsTotal"]
          and summary["btsTotal"] <= summary["networkElementsTotal"], "network counts do not multiply by sectors")
    cells = page("legacy cells list", "/api/cells", period)
    check(cells["total"] > 0 and cells["total"] == len(cells["items"]) == summary["cellTotal"],
          "cell inventory and summary match", required=True)
    kpis = [row["kpis"] for row in cells["items"]]
    attempts = sum(dec(k["connAttempt"]) for k in kpis)
    successes = sum(dec(k["connSuccess"]) for k in kpis)
    drops = sum(dec(k["connDrop"]) for k in kpis)
    check(close(summary["avgCssr"], ratio(successes, attempts)) and close(summary["avgDcr"], ratio(drops, successes))
          and dec(summary["kpis"]["connAttempt"]) == attempts, "summary ratios weight counter totals")
    coverage = summary["coverage"]
    check(coverage["observedSamples"] > 0 and coverage["expectedSamples"] > 0
          and coverage["observedSamples"] == sum(k["coverage"]["observedSamples"] for k in kpis)
          and coverage["missingSamples"] == max(0, coverage["expectedSamples"] - coverage["observedSamples"])
          and close(coverage["coveragePct"], ratio(coverage["observedSamples"], coverage["expectedSamples"])),
          "seeded data has nonzero and arithmetically consistent coverage")
    check(summary["avgAvailability"] is None and summary["kpis"]["availabilityPct"] is None
          and summary["availabilityBasis"] == "traffic_activity_proxy", "traffic activity is not reported as measured availability")
    detail = get(f"/api/monitoring/network-elements/{element['id']}", period)
    bts_detail = get(f"/api/bts/{element['id']}", period)
    check(detail.status == bts_detail.status == 200 and detail.data == bts_detail.data
          and detail.data["networkElement"]["lat"] is None and detail.data["networkElement"]["lon"] is None
          and detail.data["networkElement"]["address"] is None, "network and legacy BTS detail preserve coordinate privacy")
    check(detail.data["sectorsTotal"] == len(detail.data["sectors"])
          and all(r["btsId"] == element["id"] for r in detail.data["sectors"]), "BTS detail sectors belong to requested BTS")
    cell = next((r for r in cells["items"] if r["kpis"]["coverage"]["observedSamples"] > 0), None)
    check(cell is not None, "seed contains an observed KPI cell", required=True)
    cell_id = cell["cellId"]
    cell_detail = get(f"/api/cells/{cell_id}", period)
    check(cell_detail.status == 200 and cell_detail.data["cell"]["cellId"] == cell_id, "legacy cell detail")
    hourly = get(f"/api/cells/{cell_id}/kpi", period)
    check(hourly.status == 200 and hourly.data["total"] == len(hourly.data["items"]) > 0,
          "legacy cell KPI hourly series", required=True)
    hours = hourly.data["items"]
    attempt = sum(dec(r["connAttempt"]) for r in hours)
    success = sum(dec(r["connSuccess"]) for r in hours)
    drop = sum(dec(r["connDrop"]) for r in hours)
    aggregate = hourly.data["kpis"]
    check(close(aggregate["cssrPct"], ratio(success, attempt)) and close(aggregate["dcrPct"], ratio(drop, success))
          and dec(aggregate["connAttempt"]) == attempt, "cell aggregate matches hourly counters")
    check(all(close(r["cssrPct"], ratio(r["connSuccess"], r["connAttempt"]))
              and close(r["dcrPct"], ratio(r["connDrop"], r["connSuccess"]))
              and instant(period["from"]) <= instant(r["tsHour"]) < instant(period["to"])
              and r["availabilityPct"] is None for r in hours), "hourly ratios and half-open timestamps")
    absent = get("/api/monitoring/summary", {"from": "1970-01-01T00:00:00Z", "to": "1970-01-01T01:00:00Z"})
    check(absent.status == 200 and absent.data["coverage"]["observedSamples"] == 0
          and absent.data["coverage"]["expectedSamples"] > 0 and absent.data["coverage"]["coveragePct"] == 0
          and absent.data["kpis"]["connAttempt"] is None and absent.data["avgCssr"] is None,
          "missing KPI data is unknown with zero coverage")
    scoped = page("monitoring network scope", "/api/monitoring/network-elements", dict(period, btsId=element["id"], type="bts", regionId=element["regionId"]))
    check(scoped["total"] == 1 and scoped["items"][0]["id"] == element["id"], "combined monitoring scope filters")
    anomalies = page("legacy anomaly list", "/api/anomalies", period)
    check(anomalies["total"] > 0 and anomalies["items"], "seeded anomalies are available", required=True)
    anomaly = anomalies["items"][0]
    anomaly_detail = operator.request("GET", f"/api/anomalies/{anomaly['anomalyId']}")
    check(anomaly_detail.status == 200 and anomaly_detail.data["anomaly"]["anomalyId"] == anomaly["anomalyId"], "legacy anomaly detail")
    filters = dict(period, status=anomaly["status"], type=anomaly["type"], severity=anomaly["severity"])
    if anomaly.get("kpiCode"):
        filters["kpiCode"] = anomaly["kpiCode"]
    filtered = page("anomaly filters", "/api/monitoring/anomalies", filters)
    check(filtered["total"] > 0 and all(r["status"] == anomaly["status"] and r["type"] == anomaly["type"]
          and r["severity"] == anomaly["severity"] and (not anomaly.get("kpiCode") or r["kpiCode"] == anomaly["kpiCode"])
          for r in filtered["items"]), "anomaly status type severity and KPI filters intersect")
    check(all(get(path, period).status == 404 for path in
              ("/api/monitoring/network-elements/4294967295", "/api/bts/4294967295",
               "/api/cells/4294967295", "/api/cells/4294967295/kpi", "/api/anomalies/9223372036854775807")),
          "unknown monitoring resources return 404")
    check(all(get("/api/monitoring/summary", query).status == 400 for query in
              ({"from": period["from"]}, {"from": "2026-01-01T00:00:00", "to": period["to"]},
               {"from": period["to"], "to": period["from"]}, {"pageSize": 201}, {"status": "invalid"})),
          "monitoring validates periods pagination and enums")
    refresh = {"from": iso(now - timedelta(hours=2)), "to": iso(now - timedelta(hours=1))}
    check(engineer.request("POST", "/api/monitoring/refresh-kpi", refresh).status == 403
          and engineer.request("POST", "/api/monitoring/detect-anomalies", {"hoursBack": 1}).status == 403,
          "engineer cannot invoke monitoring maintenance wrappers")
    check(operator.request("POST", "/api/monitoring/refresh-kpi", {"from": refresh["from"]}).status == 400
          and operator.request("POST", "/api/monitoring/detect-anomalies", {"hoursBack": 0}).status == 400,
          "maintenance wrappers reject invalid requests before side effects")
    if os.environ.get("RUN_MAINTENANCE_TESTS") == "1":
        response = operator.request("POST", "/api/monitoring/refresh-kpi", refresh)
        check(response.status == 200 and isinstance(response.data.get("processed"), int), "operator refresh KPI wrapper")
        response = operator.request("POST", "/api/monitoring/detect-anomalies", {"hoursBack": 1})
        check(response.status == 200 and isinstance(response.data.get("found"), int), "operator anomaly detection wrapper")


def cleanup():
    for incident_id in tuple(created):
        try:
            response = operator.request("DELETE", f"/api/incidents/{incident_id}")
            check(response.status in (204, 404), "cleanup owned fixture " + str(incident_id))
            if response.status in (204, 404):
                created.remove(incident_id)
        except (URLError, TimeoutError, OSError):
            check(False, "cleanup transport failure for owned fixture " + str(incident_id))


if __name__ == "__main__":
    guard()
    try:
        main()
    except StopSuite:
        pass
    except (URLError, TimeoutError, OSError):
        check(False, "HTTP transport failed")
    except (KeyError, TypeError, ValueError, StopIteration, IndexError):
        check(False, "response contract could not be read")
    finally:
        cleanup()
    print(f"Checks: {checks}; passed: {checks - len(failures)}; failed: {len(failures)}; remaining fixtures: {len(created)}")
    for name in failures:
        print("FAIL " + name)
    sys.exit(1 if failures or created else 0)
