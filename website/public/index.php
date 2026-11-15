<!DOCTYPE html>
<html lang="ru">
<head>
    <meta charset="UTF-8">
    <title>Дашборд - Мотив</title>
    <link rel="stylesheet" href="style.css">
    <script src="script.js"></script>
</head>
<body>
    <header>
        <div class="logo">📡 Мотив</div>
        <div class="tabs">
            <a href="dashboard.html" class="tab active">Дашборд</a>
            <a href="incidents.html" class="tab">Аварии</a>
            <a href="reports.html" class="tab">Отчетность</a>
            <a href="monitoring.html" class="tab">Мониторинг</a>
            <a href="admin.html" class="tab admin-only">Админ-панель</a>
        </div>
        <div class="user-info">
            <span>Роль: <span id="current-role" style="color: #fff; font-weight: bold;">-</span></span>
            <button class="btn-logout" onclick="logout()">Выйти</button>
        </div>
    </header>

    <div class="content">
        <div class="dashboard-grid">
            <div class="kpi-card red"><div class="kpi-title">Всего аварий (сутки)</div><div class="kpi-value">42</div><div class="kpi-sub">+5 к прошлому дню</div></div>
            <div class="kpi-card orange"><div class="kpi-title">Открытые (НВ)</div><div class="kpi-value">12</div><div class="kpi-sub">Требуют внимания</div></div>
            <div class="kpi-card blue"><div class="kpi-title">Критичные (Кат. 3)</div><div class="kpi-value">4</div><div class="kpi-sub">2 на направлении СВН</div></div>
            <div class="kpi-card green"><div class="kpi-title">Доступность сети</div><div class="kpi-value">99.2%</div><div class="kpi-sub">Норма > 99.0%</div></div>
        </div>
        <div class="card">
            <div class="card-title">Последние инциденты</div>
            <table>
                <tr><th>NTT/PW</th><th>Направление</th><th>Сетевой элемент</th><th>Статус</th><th>Начало</th></tr>
                <tr><td>NTT-63635</td><td>ПФО</td><td>УЗ_152 — Узел МгМн</td><td><span class="badge badge-red">Авария</span></td><td>31.08 04:31</td></tr>
                <tr><td>NTT-63587</td><td>ЮЗН</td><td>УЗ_044 — БС «Таёжный-044»</td><td><span class="badge badge-yellow">В работе</span></td><td>29.08 11:08</td></tr>
                <tr><td>NTT-63339</td><td>ХМЯН</td><td>УЗ_045 — БС «Восточный-045»</td><td><span class="badge badge-green">Закрыта</span></td><td>31.08 04:17</td></tr>
            </table>
        </div>
    </div>
</body>
</html>