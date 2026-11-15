<?php
require __DIR__ . '/../src/auth.php';
require __DIR__ . '/../src/layout.php';

renderHeader('Аварии', 'incidents');
?>
<div class="card">
    <div class="card-title">Регистрация новой аварии</div>
    <div class="form-grid">
        <div class="form-group"><label>Номер PW/NTT</label><input type="text" placeholder="NTT-63587"></div>
        <div class="form-group"><label>Направление</label><select><option>СВН</option><option>ЮЗН</option><option>ХМЯН</option></select></div>
        <div class="form-group"><label>ТЦ</label><select><option>ЕКБ</option><option>АСБ</option><option>ИРБ</option></select></div>
        <div class="form-group"><label>Категория</label><select><option>1</option><option>2</option><option>3</option></select></div>
        <div class="form-group"><label>Критичность</label><select><option>Critical</option><option>High</option><option>Low</option></select></div>
        <div class="form-group"><label>Тип сети</label><select><option>LTE</option><option>GSM</option></select></div>
    </div>
    <div class="form-group" style="margin-top: 15px;"><label>Сетевой элемент (адрес БС)</label><input type="text" placeholder="УЗ_044 — БС..."></div>
    <div class="form-group" style="margin-top: 15px;"><label>Описание</label><textarea rows="3" placeholder="Детали..."></textarea></div>
    <div style="text-align: right; margin-top: 20px;">
        <button class="btn btn-secondary">Отмена</button>
        <?php if (!$isOperator): ?>
            <button class="btn btn-primary" onclick="alert('Сохранено!')">Сохранить аварию</button>
        <?php endif; ?>
    </div>
</div>
<?php renderFooter();