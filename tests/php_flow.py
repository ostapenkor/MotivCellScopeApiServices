"""Real PHP → synchronous cURL → API flow on loopback; credentials come from environment."""
from __future__ import annotations

import json
import os
import ipaddress
import re
import sys
import uuid
from datetime import datetime, timedelta, timezone
from html.parser import HTMLParser
from http.cookiejar import CookieJar
from urllib.error import HTTPError
from urllib.parse import parse_qs, urlencode, urljoin, urlsplit
from urllib.request import HTTPCookieProcessor, ProxyHandler, Request, build_opener


SITE = os.environ.get("TEST_SITE_URL", "http://127.0.0.1:5092/").rstrip("/")+"/"
API = os.environ.get("TEST_BASE_URL", "http://127.0.0.1:8080").rstrip("/")
COUNTS = {"passed": 0}
MARKER = "php-flow-" + datetime.now(timezone.utc).strftime("%Y%m%d%H%M%S") + "-" + uuid.uuid4().hex[:8]
NOW = datetime.now(timezone.utc).replace(microsecond=0)
QUERY = {"from": (NOW - timedelta(days=2)).strftime("%Y-%m-%dT00:00:00Z"),
         "to": (NOW + timedelta(days=2)).strftime("%Y-%m-%dT00:00:00Z"),
         "search": MARKER, "pageSize": 200}
API_OPENER = build_opener(ProxyHandler({}))


def check(condition: bool, message: str) -> None:
    if not condition:
        raise AssertionError(message)
    COUNTS["passed"] += 1


class Forms(HTMLParser):
    def __init__(self, html: str):
        super().__init__(convert_charrefs=True)
        self.forms: list[dict] = []
        self.current = None
        self.select = None
        self.option = None
        self.textarea = None
        self.feed(html)

    def handle_starttag(self, tag, attrs):
        attrs = dict(attrs)
        if tag == "form":
            self.current = {"action": attrs.get("action", "index.php"), "fields": {}, "method": attrs.get("method", "get")}
            self.forms.append(self.current)
        elif self.current is not None:
            if tag == "input" and attrs.get("name"):
                if attrs.get("type") not in ("checkbox", "radio") or "checked" in attrs:
                    self.current["fields"][attrs["name"]] = attrs.get("value", "")
            elif tag == "select" and attrs.get("name"):
                self.select = attrs["name"]
            elif tag == "option" and self.select:
                self.option = {"value": attrs.get("value", ""), "selected": "selected" in attrs}
                if self.select not in self.current["fields"] or self.option["selected"]:
                    self.current["fields"][self.select] = self.option["value"]
            elif tag == "textarea" and attrs.get("name"):
                self.textarea = attrs["name"]
                self.current["fields"][self.textarea] = ""

    def handle_endtag(self, tag):
        if tag == "form":
            self.current = None
        elif tag == "select":
            self.select = None
        elif tag == "textarea":
            self.textarea = None

    def handle_data(self, data):
        if self.current is not None and self.textarea:
            self.current["fields"][self.textarea] += data

    def action(self, name: str) -> dict:
        return next(form for form in self.forms if form["fields"].get("action") == name)


class Browser:
    def __init__(self):
        self.cookies = CookieJar()
        self.opener = build_opener(ProxyHandler({}), HTTPCookieProcessor(self.cookies))

    def request(self, path: str, fields: dict | None = None):
        body = urlencode(fields).encode("utf-8") if fields is not None else None
        request = Request(urljoin(SITE, path), data=body)
        try:
            response = self.opener.open(request, timeout=40)
        except HTTPError as error:
            response = error
        with response:
            return response.status, response.headers, response.read(), response.geturl()

    def page(self, path: str = "index.php", fields: dict | None = None, expected: int = 200):
        status, headers, body, url = self.request(path, fields)
        check(status == expected, f"PHP expected HTTP {expected}, got {status}")
        check("text/html" in headers.get("Content-Type", ""), "PHP page content type")
        return body.decode("utf-8"), headers, url


def api(method: str, path: str, token: str | None = None, body=None, expected: int = 200):
    headers = {"Accept": "application/json"}
    if token:
        headers["Authorization"] = "Bearer " + token
    data = None
    if body is not None:
        headers["Content-Type"] = "application/json"
        data = json.dumps(body, ensure_ascii=False).encode("utf-8")
    request = Request(API + path, headers=headers, data=data, method=method)
    try:
        response = API_OPENER.open(request, timeout=40)
    except HTTPError as error:
        response = error
    with response:
        status = response.status
        raw = response.read()
    check(status == expected, f"API {method} expected HTTP {expected}, got {status}")
    return json.loads(raw) if raw.strip() else None


def safe_html(html: str):
    check(not re.search(r"eyJ[A-Za-z0-9_-]{10,}\.[A-Za-z0-9_-]{10,}\.", html), "JWT must not appear in PHP HTML")
    check("localStorage" not in html and "sessionStorage" not in html, "Browser storage absent from PHP page")
    check("Fatal error" not in html and "Warning:" not in html, "No PHP diagnostics in page")


def username(value):
    return value.get("username", value.get("login")) if isinstance(value, dict) else value


def login(browser: Browser, name: str, password: str):
    html, _, _ = browser.page()
    form = Forms(html).action("login")
    check(len(form["fields"].get("csrf", "")) == 64, "Login form CSRF token")
    fields = form["fields"] | {"username": name, "password": password}
    html, headers, final_url = browser.page(form["action"], fields)
    check("Дашборд" in html and "action\" value=\"logout" in html, "Login redirects to authenticated dashboard")
    check("no-store" in headers.get("Cache-Control", ""), "Authenticated page is not cached")
    check("connect-src 'none'" in headers.get("Content-Security-Policy", ""), "Browser API calls disabled by CSP")
    check(any("httponly" in {key.lower() for key in cookie._rest} for cookie in browser.cookies), "Session cookie HttpOnly")
    check(all("eyJ" not in cookie.value for cookie in browser.cookies), "Session cookie does not contain JWT")
    safe_html(html)
    return html


def main():
    if os.environ.get("ALLOW_TEST_WRITES") != "1":
        raise SystemExit("ALLOW_TEST_WRITES=1 is required for this local test.")
    for value in [SITE, API]:
        host=urlsplit(value).hostname or ""
        if host != "localhost" and not ipaddress.ip_address(host).is_loopback:
            raise SystemExit("Only loopback test hosts are allowed.")
    credentials = {"password":os.environ.get("TEST_PASSWORD", ""), "operator":os.environ.get("TEST_OPERATOR", "checks_operator"), "engineer":os.environ.get("TEST_ENGINEER", "checks_engineer")}
    if not credentials["password"]: raise SystemExit("TEST_PASSWORD is required.")
    password = credentials["password"]
    operator_name = username(credentials["operator"])
    engineer_name = username(credentials["engineer"])
    token = api("POST", "/api/auth/login", body={"username": operator_name, "password": password})["token"]
    fixture_id = None
    operator = Browser()
    try:
        dashboard = login(operator, operator_name, password)
        check("Нет исходных данных" in dashboard, "Unavailable availability is explicitly labelled")
        for tab, title in [("dashboard", "Ключевые показатели"), ("incidents", "Журнал инцидентов"), ("reports", "Экспорт отчёта"), ("monitoring", "Географический слой не подключён")]:
            html, _, _ = operator.page("index.php?" + urlencode({"tab": tab}))
            check(title in html, f"Rendered {tab} tab")
            check("Не удалось загрузить часть данных" not in html, f"No API load error in {tab}")
            safe_html(html)
            if tab == "incidents":
                check("Новый инцидент" in html, "Operator create action visible")
            if tab == "monitoring":
                check("Точные координаты по умолчанию скрыты" in html, "Coordinate privacy stub is explicit")

        refs = api("GET", "/api/reference/all", token)
        check(bool(refs.get("regions")) and bool(refs.get("directions")), "Reference data present")
        html, _, _ = operator.page("index.php?tab=incidents&new=1")
        create_form = Forms(html).action("save")
        fields = create_form["fields"] | {
            "descr": MARKER, "category": "other", "severity": "medium", "numbRbs": "2", "prostoy": "27:15:00",
            "beginTime": (NOW - timedelta(hours=2)).strftime("%Y-%m-%dT%H:%M:%S"),
            "endTime": (NOW - timedelta(hours=1)).strftime("%Y-%m-%dT%H:%M:%S"),
            "regionId": str(refs["regions"][0]["regionId"]),
            "directionId": str(refs["directions"][0]["directionId"]),
        }
        if refs.get("priorities"):
            fields["priorityId"] = str(refs["priorities"][0]["priorityId"])
        html, _, final_url = operator.page(create_form["action"], fields)
        check("Инцидент создан." in html, "PHP create succeeds")
        fixture_id = parse_qs(urlsplit(final_url).query).get("id", [None])[0]
        check(fixture_id is not None, "Create uses redirect with fixture ID")
        created = api("GET", f"/api/incidents/{fixture_id}", token)
        check(created["descr"] == MARKER and created["prostoy"] == "27:15:00", "API contains PHP fixture and duration above24h")
        check(created["commonProstoy"] == "54:30:00", "Derived weighted downtime comes from API")
        safe_html(html)

        html, _, _ = operator.page("index.php?" + urlencode({"tab": "incidents", "edit": fixture_id}))
        edit_form = Forms(html).action("save")
        check(edit_form["fields"]["expectedUpdatedAt"] == created["updatedAt"], "PHP edit includes optimistic version")
        notes = MARKER + " edited <b>PHP</b> & reviewed"
        html, _, _ = operator.page(edit_form["action"], edit_form["fields"] | {"descNotice": notes})
        check("Изменения сохранены." in html, "PHP edit succeeds")
        check("&lt;b&gt;PHP&lt;/b&gt; &amp; reviewed" in html, "Edited HTML is escaped")
        updated = api("GET", f"/api/incidents/{fixture_id}", token)
        check(updated["descNotice"] == notes, "Edited note persisted")
        check(updated["beginTime"] == created["beginTime"], "Edit preserves UTC seconds")

        stale_note = MARKER + "-stale-edit-preserved"
        html, _, _ = operator.page(edit_form["action"], edit_form["fields"] | {"descNotice": stale_note})
        check("id=\"form-errors\"" in html and stale_note in html, "Stale edit shows error and preserves submitted note")
        check(api("GET", f"/api/incidents/{fixture_id}", token)["descNotice"] == notes, "Optimistic conflict does not overwrite current record")

        filter_query = {"tab": "incidents", "search": MARKER, "from": (NOW - timedelta(days=1)).strftime("%Y-%m-%d"), "to": NOW.strftime("%Y-%m-%d")}
        html, _, _ = operator.page("index.php?" + urlencode(filter_query))
        check(f"#{fixture_id}" in html and MARKER in html, "PHP journal filters find fixture")
        for mode in ("details", "grouped"):
            status, headers, raw, _ = operator.request("index.php?" + urlencode(filter_query | {"download": mode, "groupBy": "direction"}))
            check(status == 200 and "text/csv" in headers.get("Content-Type", ""), f"PHP {mode} CSV response")
            csv = raw.decode("utf-8-sig")
            check((MARKER in csv) if mode == "details" else ("knownDowntimeSeconds" in csv and "direction" in csv), f"PHP {mode} CSV content")

        html, _, _ = operator.page("index.php?tab=incidents&new=1")
        missing_csrf = Forms(html).action("save")["fields"] | fields | {"descr": MARKER + "-csrf-denied"}
        missing_csrf.pop("csrf", None)
        html, _, _ = operator.page("index.php?tab=incidents", missing_csrf, expected=403)
        check("Срок действия формы истёк" in html, "Missing CSRF is rejected")

        engineer = Browser()
        login(engineer, engineer_name, password)
        html, _, _ = engineer.page("index.php?tab=incidents")
        check("Новый инцидент" not in html and "Режим просмотра" in html, "Engineer UI read-only")
        check(not any(form["fields"].get("action") in ("save", "delete") for form in Forms(html).forms), "Engineer mutation forms absent")
        engineer_csrf = Forms(html).action("logout")["fields"]["csrf"]
        forged = fields | {"csrf": engineer_csrf, "descr": MARKER + "-engineer-denied"}
        html, _, _ = engineer.page("index.php?tab=incidents", forged)
        check("Изменение инцидентов доступно оператору" in html, "Forged engineer mutation denied on PHP server")
        own_rows = api("GET", "/api/incidents?" + urlencode(QUERY), token)
        check(len(own_rows["items"]) == 1 and str(own_rows["items"][0]["incidentId"]) == str(fixture_id), "Denied forms created no extra records")

        html, _, _ = operator.page("index.php?" + urlencode({"tab": "incidents", "id": fixture_id}))
        delete_form = Forms(html).action("delete")
        html, _, _ = operator.page(delete_form["action"], delete_form["fields"])
        check("удалён." in html, "PHP deletes own fixture")
        api("GET", f"/api/incidents/{fixture_id}", token, expected=404)
        fixture_id = None

        html, _, _ = operator.page("index.php?tab=dashboard")
        logout_form = Forms(html).action("logout")
        previous_cookie = next(iter(operator.cookies)).value
        html, _, _ = operator.page(logout_form["action"], logout_form["fields"])
        check("Войти в систему" in html and "Вы вышли из системы." in html, "PHP logout succeeds")
        check(next(iter(operator.cookies)).value != previous_cookie, "Logout regenerates session ID")
        html, _, _ = operator.page("index.php?tab=incidents")
        check("Войти в систему" in html and "Журнал инцидентов <" not in html, "Protected page unavailable after logout")
        safe_html(html)
        status, _, raw, _ = operator.request("assets/ui.js")
        check(status == 200, "Plain JS asset reachable")
        check(not re.search(rb"\bfetch\s*\(|XMLHttpRequest|localStorage|sessionStorage", raw), "JS has no API fetch or token storage")
    finally:
        # Only this run's unpredictable marker can be removed. Audit history remains intact.
        rows = api("GET", "/api/incidents?" + urlencode(QUERY), token)["items"]
        cleaned = 0
        for row in rows:
            if str(row.get("descr", "")).startswith(MARKER):
                api("DELETE", f"/api/incidents/{row['incidentId']}", token, expected=204)
                cleaned += 1
        check(not api("GET", "/api/incidents?" + urlencode(QUERY), token)["items"], "Own PHP fixtures fully cleaned up")
        print(f"Cleanup: {cleaned} remaining own fixture(s) removed; audit retained.")
    print(f"PHP end-to-end: {COUNTS['passed']} checks passed.")


if __name__ == "__main__":
    try:
        main()
    except Exception as error:
        # Do not dump request objects, cookies, HTML, JWTs, or credential values.
        print(f"PHP end-to-end FAILED after {COUNTS['passed']} checks: {type(error).__name__}: {error}", file=sys.stderr)
        raise SystemExit(1)
