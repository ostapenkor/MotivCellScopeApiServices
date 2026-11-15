<?php
require __DIR__ . '/../src/auth.php';
require __DIR__ . '/../src/layout.php';

renderHeader('Дашборд', 'index');
?>
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
<?php renderFooter();