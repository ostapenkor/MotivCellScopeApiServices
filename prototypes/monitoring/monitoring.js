/* Чистый JS. Смените mode на api после подключения PHP-обработчика к REST API.
   PHP TODO_AUTH / TODO_REST / TODO_SERVER находятся в api/monitoring.php.
   Демо содержит только вымышленные БС, без их координат и адресов. */
(function () {
    'use strict';
    var config = window.MONITORING_CONFIG || { mode: 'demo', endpoint: 'api/monitoring.php' };
    var state = { map: null, data: null, facts: {}, selected: null, bounds: null, nodes: {}, loading: false };
    var labels = { critical: 'Критическая проблема', warning: 'Предупреждение', ok: 'Норма', unknown: 'Нет телеметрии', stale: 'Данные устарели', empty: 'Без БС' };
    var ns = 'http://www.w3.org/2000/svg';
    function el(id) { return document.getElementById(id); }
    function clear(node) { while (node.firstChild) node.removeChild(node.firstChild); }
    function text(tag, value, cls) { var n = document.createElement(tag); n.textContent = value; if (cls) n.className = cls; return n; }
    function svg(tag, attributes) { var n = document.createElementNS(ns, tag); Object.keys(attributes).forEach(function (k) { n.setAttribute(k, attributes[k]); }); return n; }
    function regionName(id) { return state.map.regions.filter(function (r) { return r.id === id; })[0].name; }
    function time(value) { var d = new Date(value); return isNaN(d.getTime()) ? 'не указано' : d.toISOString().slice(0,19).replace('T',' ') + ' UTC'; }
    function stale(value) { var stamp = Date.parse(value), reference = state.data.mode === 'demo' ? Date.parse(state.data.asOf) : Date.now(); return !isNaN(stamp) && reference-stamp > 10*60*1000; }
    function status(cell) { if (!cell) return 'empty'; if (!cell.updatedAt || isNaN(Date.parse(cell.updatedAt))) return 'unknown'; return stale(cell.updatedAt) ? 'stale' : cell.status; }
    function stationStatus(station) { return !station.updatedAt ? 'unknown' : stale(station.updatedAt) ? 'stale' : station.status; }
    async function json(url) {
        var controller = new AbortController(), timer = setTimeout(function () { controller.abort(); }, 12000);
        try { var response = await fetch(url, {signal:controller.signal, credentials:'same-origin', cache:'no-store', headers:{Accept:'application/json'}}); if (!response.ok) throw new Error('HTTP ' + response.status); return await response.json(); }
        finally { clearTimeout(timer); }
    }
    function url(action, id) { return config.endpoint + (config.endpoint.indexOf('?') < 0 ? '?' : '&') + 'action=' + action + (id ? '&id=' + encodeURIComponent(id) : ''); }
    function setBounds(bounds) {
        var pad = 100, width = bounds[2]-bounds[0], height = bounds[3]-bounds[1];
        state.bounds = [bounds[0]-pad,bounds[1]-pad,width+pad*2,height+pad*2];
        el('monitor-map').setAttribute('viewBox', state.bounds.join(' '));
    }
    function fit() { var region = state.map.regions.filter(function(r) { return r.id === el('monitor-region').value; })[0]; setBounds(region ? region.bounds : state.map.bounds); }
    function zoom(factor) {
        if (!state.bounds) return;
        var b = state.bounds, width = b[2]*factor, height = b[3]*factor;
        if (width < 200 || width > (state.map.bounds[2]-state.map.bounds[0])*3) return;
        state.bounds = [b[0]+(b[2]-width)/2,b[1]+(b[3]-height)/2,width,height];
        el('monitor-map').setAttribute('viewBox',state.bounds.join(' '));
    }
    function initializeMap() {
        clear(el('monitor-regions')); clear(el('monitor-hexagons')); clear(el('monitor-labels')); state.nodes = {};
        state.map.regions.forEach(function (r) {
            var d = r.polygons.map(function (polygon) { return polygon.map(function (ring) { return 'M' + ring.map(function (p) { return p.join(','); }).join('L') + 'Z'; }).join(' '); }).join(' ');
            var path = svg('path', {d:d,'class':'region-path','data-region':r.id}); path.appendChild(svg('title',{})).textContent = r.name; el('monitor-regions').appendChild(path);
            var label = svg('text', {x:(r.bounds[0]+r.bounds[2])/2,y:(r.bounds[1]+r.bounds[3])/2,'class':'region-name','data-region':r.id});
            label.textContent = {'RU-SVE':'СВЕРДЛОВСКАЯ','RU-KGN':'КУРГАНСКАЯ','RU-KHM':'ХМАО — ЮГРА','RU-YAN':'ЯНАО'}[r.id]; el('monitor-labels').appendChild(label);
        });
        state.map.cells.forEach(function (g) {
            var p = svg('polygon', {points:g.polygon.map(function (point) { return point.join(','); }).join(' '),role:'button',tabindex:'0','data-cell':g.id});
            p.addEventListener('click',function () { select(g.id); });
            p.addEventListener('keydown',function (e) { if(e.key === 'Enter' || e.key === ' ') { e.preventDefault(); select(g.id); } });
            p.appendChild(svg('title',{})); el('monitor-hexagons').appendChild(p); state.nodes[g.id] = p;
        });
        fit();
    }
    function visible(g) {
        var cell = state.facts[g.id], region = el('monitor-region').value, filter = el('monitor-status').value, query = el('monitor-search').value.trim().toLowerCase();
        if (region && g.regionId !== region || filter && status(cell) !== filter) return false;
        if (!query) return true;
        return g.id.toLowerCase().indexOf(query) >= 0 || cell && (cell.stations || []).some(function (s) { return (s.id+' '+s.name).toLowerCase().indexOf(query) >= 0; });
    }
    function render() {
        var totals = {stations:0,critical:0,warning:0,unknown:0,shown:0,occupied:0};
        state.map.cells.forEach(function (g) {
            var p = state.nodes[g.id], cell = state.facts[g.id], value = status(cell), count = cell ? (cell.stationCount === undefined ? cell.stations.length : cell.stationCount) : 0;
            var show = visible(g); p.style.display = show ? '' : 'none';
            p.setAttribute('class','map-hex '+value+(g.id === state.selected ? ' selected' : ''));
            var caption = regionName(g.regionId) + ' · ' + labels[value] + ' · БС: ' + count + ' · ' + g.id;
            p.setAttribute('aria-label',caption); p.setAttribute('aria-pressed',g.id === state.selected ? 'true' : 'false'); p.firstChild.textContent = caption;
            if(show) { totals.shown++; totals.stations+=count; if(cell) totals.occupied++; if(value==='critical') totals.critical++; if(value==='warning') totals.warning++; if(value==='unknown'||value==='stale') totals.unknown++; }
        });
        Array.prototype.forEach.call(el('monitor-regions').children,function (p) { p.style.opacity = !el('monitor-region').value || p.getAttribute('data-region') === el('monitor-region').value ? '1' : '.15'; });
        Array.prototype.forEach.call(el('monitor-labels').children,function (p) { p.style.display = !el('monitor-region').value || p.getAttribute('data-region') === el('monitor-region').value ? '' : 'none'; });
        el('monitor-total').textContent=totals.stations; el('monitor-critical').textContent=totals.critical; el('monitor-warning').textContent=totals.warning; el('monitor-unknown').textContent=totals.unknown;
        el('monitor-count').textContent='Ячеек: '+totals.shown+' · с БС: '+totals.occupied;
        if(state.selected && state.nodes[state.selected].style.display === 'none') { state.selected=null; details(null); }
    }
    function details(cell, geometry) {
        var box=el('monitor-details'); clear(box); box.appendChild(text('span','ПОДРОБНОСТИ ЯЧЕЙКИ','monitor-eyebrow'));
        if(!geometry) { box.appendChild(text('h2','Выберите гексагон')); box.appendChild(text('p','Нажмите на ячейку карты. Здесь появятся БС, состояние и время обновления.')); return; }
        box.appendChild(text('h2',regionName(geometry.regionId)));
        box.appendChild(text('p',geometry.id,'monitor-help'));
        var value=status(cell); box.appendChild(text('span',labels[value],'monitor-chip '+value));
        if(!cell) { box.appendChild(text('p','В демонстрационном реестре за этой ячейкой БС не закреплены. Это не означает отсутствие покрытия.')); return; }
        box.appendChild(text('p','БС: '+cell.stations.length+' · обновлено '+time(cell.updatedAt),'monitor-cell-stats'));
        if(value === 'stale') box.appendChild(text('p','Данные старше 10 минут. Последние известные состояния не выдаются за текущие.','monitor-help'));
        cell.stations.forEach(function(s) {
            var row=text('div','','monitor-station'); row.appendChild(text('strong',s.name)); row.appendChild(text('small',s.id));
            row.appendChild(text('div',labels[stationStatus(s)],'state-label')); if(s.issue) row.appendChild(text('p',s.issue)); row.appendChild(text('p',time(s.updatedAt))); box.appendChild(row);
        });
    }
    async function select(id) {
        state.selected=id; render(); var geometry=state.map.cells.filter(function (g) { return g.id === id; })[0];
        if(config.mode === 'demo' || !state.facts[id]) { details(state.facts[id],geometry); return; }
        details(null); el('monitor-details').appendChild(text('p','Получаем подробности…'));
        try { var response=await json(url('cell',id)); if(state.selected===id) details(response.cell,geometry); }
        catch(error) { if(state.selected===id) { clear(el('monitor-details')); el('monitor-details').appendChild(text('p','Не удалось получить сведения по ячейке. Повторите выбор.')); } }
    }
    async function load() {
        if(state.loading) return; state.loading=true; el('monitor-refresh').disabled=true; el('monitor-error').hidden=true; el('monitor-count').textContent='Получаем снимок данных…';
        try {
            var values=await Promise.all([state.map ? Promise.resolve(state.map) : json('data/map.json'),json(config.mode==='demo' ? 'data/monitoring-demo.json' : url('cells'))]);
            var map=values[0],data=values[1],facts={};
            if(data.gridVersion!==map.gridVersion || !Array.isArray(data.cells) || !isFinite(Date.parse(data.asOf))) throw new Error('Несовместимый контракт');
            data.cells.forEach(function(c) { if(facts[c.id] || !map.cells.some(function(g){return g.id===c.id;}) || !labels[c.status] || c.status==='empty'||c.status==='stale') throw new Error('Неверная ячейка'); facts[c.id]=c; if(config.mode==='demo'&&!Array.isArray(c.stations)) throw new Error('Неверный список БС'); });
            var first=!state.map; state.map=map;state.data=data;state.facts=facts;
            if(first) { map.regions.forEach(function(r){ var option=text('option',r.name);option.value=r.id;el('monitor-region').appendChild(option); });initializeMap(); }
            el('monitor-map').hidden=false;el('monitor-map').style.display='block';render();
            el('monitor-mode').textContent=data.mode==='demo'?'Демонстрационные БС и аномалии':'Данные мониторинга';
            el('monitor-updated').textContent=(data.mode==='demo'?'Демонстрационный снимок: ':'Снимок сервера: ')+time(data.asOf)+' · порог свежести: 10 минут';
            if(state.selected) select(state.selected);
        } catch(error) {
            el('monitor-error').textContent='Карта не обновлена: '+(error.name==='AbortError'?'сервер не ответил вовремя.':error.message)+ ' Проверьте сервер и повторите запрос. Демо-данные вместо ошибки API не подставляются.';
            el('monitor-error').hidden=false;el('monitor-map').style.display='none';el('monitor-count').textContent='Нет актуального снимка';
            ['monitor-total','monitor-critical','monitor-warning','monitor-unknown'].forEach(function(id){el(id).textContent='—';});details(null);
        } finally {state.loading=false;el('monitor-refresh').disabled=false;}
    }
    el('monitor-region').addEventListener('change',function(){if(state.map){fit();render();}});
    el('monitor-status').addEventListener('change',function(){if(state.map)render();});el('monitor-search').addEventListener('input',function(){if(state.map)render();});
    el('monitor-reset').addEventListener('click',function(){el('monitor-region').value='';el('monitor-status').value='';el('monitor-search').value='';if(state.map){fit();render();}});
    el('monitor-zoom-in').addEventListener('click',function(){zoom(.7);});el('monitor-zoom-out').addEventListener('click',function(){zoom(1/.7);});el('monitor-fit').addEventListener('click',function(){if(state.map)fit();});el('monitor-refresh').addEventListener('click',load);
    load();
}());
