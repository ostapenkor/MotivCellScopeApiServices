<?php
require __DIR__ . '/../src/auth.php';
require __DIR__ . '/../src/layout.php';

renderHeader('Мониторинг', 'monitoring');
?>
<div class="map-container">
    <div class="map-sidebar">
        <div class="card-title" style="margin-bottom:10px; padding-bottom:10px;">Фильтры</div>
        <div class="form-group" style="margin-bottom:15px;">
            <label>Регион</label>
            <select style="background:#17171c; border:1px solid #38383f; color:#fff; padding:8px; border-radius:4px; width:100%;"><option>Все регионы</option></select>
        </div>
        <div class="form-group" style="margin-bottom:25px;">
            <label>Статус</label>
            <select style="background:#17171c; border:1px solid #38383f; color:#fff; padding:8px; border-radius:4px; width:100%;">
                <option>Все статусы</option><option>Аномалия</option><option>Предупреждение</option><option>Норма</option>
            </select>
        </div>
        <div style="border-top:1px solid #38383f; margin-bottom:15px;"></div>
        <h4 style="color:#fff; font-size:14px; margin-bottom:10px;">Легенда</h4>
        <div style="font-size:13px; margin-top:8px; display:flex; align-items:center; gap:8px;"><span style="display:inline-block; width:12px; height:12px; background:#e30613; border-radius:50%;"></span> Аномалия</div>
        <div style="font-size:13px; margin-top:8px; display:flex; align-items:center; gap:8px;"><span style="display:inline-block; width:12px; height:12px; background:#fb8c00; border-radius:50%;"></span> Предупреждение</div>
        <div style="font-size:13px; margin-top:8px; display:flex; align-items:center; gap:8px;"><span style="display:inline-block; width:12px; height:12px; background:#2e7d32; border-radius:50%;"></span> Норма</div>
    </div>
    <div class="map-area">
        <div class="hex-grid">
            <div class="hex green"></div><div class="hex green"></div><div class="hex red"></div><div class="hex"></div><div class="hex"></div><div class="hex"></div><div class="hex"></div><div class="hex"></div>
            <div class="hex"></div><div class="hex red"></div><div class="hex orange"></div><div class="hex"></div><div class="hex"></div><div class="hex"></div><div class="hex green"></div><div class="hex green"></div>
            <div class="hex"></div><div class="hex"></div><div class="hex red"></div><div class="hex"></div><div class="hex orange"></div><div class="hex"></div><div class="hex green"></div><div class="hex"></div>
            <div class="hex"></div><div class="hex green"></div><div class="hex"></div><div class="hex"></div><div class="hex orange"></div><div class="hex orange"></div><div class="hex"></div><div class="hex"></div>
        </div>
    </div>
    <div class="map-details">
        <div class="card-title">Детали соты</div>
        <p style="margin-bottom:5px;">ID: SVE-EKB-0007</p>
        <p style="margin-bottom:5px;">Сектор: B</p>
        <p style="margin-bottom:15px;">Статус: <span style="color:#e30613; font-weight:bold;">Аномалия</span></p>
        <div style="background:#17171c; padding:10px; border-radius:4px; margin-bottom:10px;">
            <div style="font-size:11px; color:#8c8c99;">CSSR (Успешность)</div>
            <div style="font-size:20px; color:#e30613; font-weight:bold;">91.2%</div>
        </div>
        <div style="background:#17171c; padding:10px; border-radius:4px; margin-bottom:10px;">
            <div style="font-size:11px; color:#8c8c99;">DCR (Обрывы)</div>
            <div style="font-size:20px; color:#fb8c00; font-weight:bold;">4.5%</div>
        </div>
        <div style="background:rgba(227,6,19,0.1); padding:10px; border-radius:4px; border:1px solid rgba(227,6,19,0.3); margin-top:20px;">
            <div style="font-size:11px; color:#8c8c99;">Связан инцидент:</div>
            <div style="font-size:13px; color:#e30613; font-weight:bold; margin-top:3px;">NTT-63587</div>
        </div>
    </div>
</div>
<?php renderFooter();