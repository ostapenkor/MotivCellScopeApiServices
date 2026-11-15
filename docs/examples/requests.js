// Учебные примеры для main db7f930. Выполняйте в консоли Swagger того же API.
// Файл ничего не отправляет сам: вызовите нужную функцию явно.
const apiBaseUrl = window.location.origin;
let apiToken = null; // Только память страницы. После обновления нужен новый вход.

function queryString(values) {
    const query = new URLSearchParams();
    for (const [name, value] of Object.entries(values)) {
        if (value !== null && value !== undefined && value !== "") {
            query.set(name, String(value));
        }
    }
    return query.toString();
}

async function apiFetch(method, path, body) {
    const headers = { Accept: "application/json" };
    if (apiToken) headers.Authorization = "Bearer " + apiToken;
    if (body !== undefined) headers["Content-Type"] = "application/json";

    const response = await fetch(apiBaseUrl + path, {
        method,
        headers,
        body: body === undefined ? undefined : JSON.stringify(body)
    });

    // fetch не считает ответы 400/401/403/404 сетевой ошибкой.
    if (!response.ok) {
        const text = await response.text();
        let data = {};
        try { data = JSON.parse(text); } catch { /* Например, ответ прокси без JSON. */ }
        const message = data.message || data.error || data.title || response.statusText;
        const error = new Error("HTTP " + response.status + ": " + message);
        error.status = response.status;
        error.data = data;
        throw error;
    }
    return response;
}

async function apiJson(method, path, body) {
    const response = await apiFetch(method, path, body);
    if (response.status === 204) return null; // DELETE не возвращает JSON.
    return response.json();
}

async function login(username, password) {
    const result = await apiJson("POST", "/api/auth/login", { username, password });
    apiToken = result.token;
    // Не выводим JWT в консоль и не добавляем его в URL.
    return { username: result.username, role: result.role, expiresAt: result.expiresAt };
}

function forgetToken() { apiToken = null; }

function currentUser() { return apiJson("GET", "/api/auth/me"); }
function references() { return apiJson("GET", "/api/reference/all"); }

function incidents(from, to) {
    return apiJson("GET", "/api/incidents?" + queryString({ from, to }));
}

function incident(id) { return apiJson("GET", "/api/incidents/" + encodeURIComponent(id)); }
function createIncident(input) { return apiJson("POST", "/api/incidents", input); }
function updateIncident(id, input) {
    return apiJson("PUT", "/api/incidents/" + encodeURIComponent(id), input);
}
function deleteIncident(id) {
    return apiJson("DELETE", "/api/incidents/" + encodeURIComponent(id));
}

function reportSummary(from, to) {
    return apiJson("GET", "/api/reports/summary?" + queryString({ from, to }));
}

function monitoringSummary(from, to, regionId) {
    return apiJson("GET", "/api/monitoring/summary?" + queryString({ from, to, regionId }));
}

// filters должен содержать from и to; остальные параметры необязательны.
function anomalies(filters) {
    return apiJson("GET", "/api/anomalies?" + queryString(filters));
}

function explainAnomaly(anomalyId, incidentId) {
    return apiJson("POST", "/api/anomalies/" + encodeURIComponent(anomalyId) + "/explain", { incidentId });
}

function updateAnomalyStatus(anomalyId, status) {
    return apiJson("PATCH", "/api/anomalies/" + encodeURIComponent(anomalyId) + "/status", { status });
}

async function downloadIncidentsCsv(from, to) {
    const response = await apiFetch("GET", "/api/incidents/export?" + queryString({ from, to }));
    const objectUrl = URL.createObjectURL(await response.blob());
    const link = document.createElement("a");
    link.href = objectUrl;
    link.download = "incidents.csv";
    document.body.appendChild(link);
    link.click();
    link.remove();
    setTimeout(() => URL.revokeObjectURL(objectUrl), 1000);
}
