/* Обычный JavaScript: браузер получает HTML, а данные читает и сохраняет через C# API.
   Токен живёт только в этой переменной. В localStorage/sessionStorage ничего не пишем. */
(function () {
    'use strict';

    var state = {
        token: null, user: null, expiresAt: null, options: null,
        page: 'reports', reportPage: 1, auditPage: 1,
        reportSequence: 0, userSequence: 0, auditSequence: 0, detailSequence: 0,
        filters: null, editingId: null, editingVersion: 0,
        dirty: false, serverOffset: 0, modalItem: null, previousFocus: null
    };
    var maximumSeconds = 3020399; // MariaDB TIME: 838:59:59.
    var roleNames = { admin: 'Администратор', duty: 'Дежурный', engineer: 'Инженер' };

    function byId(id) { return document.getElementById(id); }
    function empty(element) { while (element.firstChild) element.removeChild(element.firstChild); }
    function node(tag, text, className) {
        var element = document.createElement(tag);
        if (text !== undefined && text !== null) element.textContent = String(text);
        if (className) element.className = className;
        return element;
    }
    function actionButton(text, className, action) {
        var button = node('button', text, 'button ' + className);
        button.type = 'button';
        button.addEventListener('click', action);
        return button;
    }
    function showMessage(id, text, error) {
        var box = byId(id);
        box.textContent = text || '';
        box.hidden = !text;
        if (id === 'notice') box.className = 'message' + (error ? ' message-error' : '');
    }
    function errorText(error) {
        if (error.name === 'TypeError') return 'Нет соединения с сервером. Проверьте сеть и повторите запрос.';
        var data = error.data || {};
        var lines = [];
        if (data.errors) {
            Object.keys(data.errors).forEach(function (key) {
                var messages = data.errors[key];
                if (!Array.isArray(messages)) messages = [messages];
                messages.forEach(function (message) { lines.push(String(message)); });
            });
        }
        if (lines.length) return lines.join('\n');
        if (error.status === 409) return data.detail || data.message || data.title || 'Запись уже изменена коллегой. Откройте её заново и согласуйте правки. Ваши данные ещё не сохранены.';
        return data.detail || data.message || data.title || error.message || 'Не удалось выполнить запрос.';
    }
    function synchronizeClock(value) {
        var milliseconds = Date.parse(value);
        if (!isNaN(milliseconds)) state.serverOffset = milliseconds - Date.now();
    }
    function serverNow() { return Date.now() + state.serverOffset; }
    function formatSeconds(seconds) {
        if (seconds === null || seconds === undefined) return 'Не определён для сектора';
        seconds = Math.max(0, Math.floor(Number(seconds)));
        function pad(value) { return value < 10 ? '0' + value : String(value); }
        return pad(Math.floor(seconds / 3600)) + ':' + pad(Math.floor(seconds / 60) % 60) + ':' + pad(seconds % 60);
    }
    function parseSeconds(text, name) {
        if (!/^\d{1,3}:\d{2}:\d{2}$/.test(text)) throw new Error(name + ': используйте ЧЧ:ММ:СС.');
        var parts = text.split(':').map(Number);
        var result = parts[0] * 3600 + parts[1] * 60 + parts[2];
        if (parts[1] > 59 || parts[2] > 59 || result > maximumSeconds) throw new Error(name + ': максимум 838:59:59.');
        return result;
    }
    function utcInput(value, seconds) {
        var date = value ? new Date(value) : new Date(serverNow());
        return isNaN(date.getTime()) ? '' : date.toISOString().slice(0, seconds ? 19 : 16);
    }
    function utcValue(id, optional) {
        var text = byId(id).value;
        if (!text && optional) return null;
        var date = new Date(text + 'Z');
        if (!text || isNaN(date.getTime())) throw new Error('Укажите корректную дату и время UTC.');
        return date.toISOString();
    }
    function dateText(value) {
        if (!value) return 'НВ';
        var date = new Date(value);
        return isNaN(date.getTime()) ? String(value) : date.toISOString().slice(0, 19).replace('T', ' ');
    }
    function option(select, value, text, selected) {
        var item = node('option', text);
        item.value = value;
        item.selected = selected;
        select.appendChild(item);
    }
    function fillSelect(id, values, placeholder) {
        var select = byId(id);
        empty(select);
        if (placeholder) option(select, '', placeholder, true);
        values.forEach(function (value) { option(select, value, value, false); });
    }
    async function api(path, settings) {
        settings = settings || {};
        var headers = { Accept: settings.csv ? 'text/csv' : 'application/json' };
        if (state.token) headers.Authorization = 'Bearer ' + state.token;
        var options = { method: settings.method || 'GET', headers: headers, credentials: 'same-origin', cache: 'no-store' };
        if (settings.body !== undefined) {
            headers['Content-Type'] = 'application/json';
            options.body = JSON.stringify(settings.body);
        }
        var response = await fetch('/api' + path, options);
        if (!response.ok) {
            var data = {};
            try { data = await response.json(); } catch (_) { /* Сервер может вернуть пустой 401/403. */ }
            var error = new Error('Ошибка запроса: ' + response.status);
            error.status = response.status;
            error.data = data;
            if (response.status === 401 && state.token) {
                resetSession('Сессия завершена. Войдите снова. Несохранённая форма останется открытой после входа.');
            }
            throw error;
        }
        if (response.status === 204) return null;
        return settings.csv ? response.blob() : response.json();
    }
    async function pending(button, operation) {
        if (button.disabled) return;
        button.disabled = true;
        button.setAttribute('aria-busy', 'true');
        try { await operation(); }
        finally { button.disabled = false; button.removeAttribute('aria-busy'); }
    }
    function resetSession(message) {
        state.token = null;
        state.user = null;
        state.reportSequence += 1;
        state.userSequence += 1;
        state.auditSequence += 1;
        state.detailSequence += 1;
        byId('application').hidden = true;
        byId('session').hidden = true;
        byId('login-screen').hidden = false;
        byId('login-password').value = '';
        closeDetails();
        showMessage('login-error', message, true);
        byId('login-name').focus();
    }
    function discardAllowed() {
        return !state.dirty || window.confirm('Есть несохранённые изменения. Покинуть форму и потерять их?');
    }
    async function changePage(page, force) {
        if (!force && state.page === 'editor' && page !== 'editor' && !discardAllowed()) return;
        if (page !== 'reports' && page !== 'editor' && !state.options.permissions.canManageUsers) return;
        if (page === 'editor' && !state.options.permissions.canWrite) return;
        state.page = page;
        ['reports', 'editor', 'users', 'audit'].forEach(function (name) { byId(name + '-page').hidden = name !== page; });
        Array.prototype.forEach.call(document.querySelectorAll('[data-page]'), function (button) {
            var active = button.getAttribute('data-page') === page;
            button.classList.toggle('active', active);
            button.setAttribute('aria-current', active ? 'page' : 'false');
        });
        showMessage('notice', '');
        if (page !== 'editor') state.dirty = false;
        if (page === 'reports') await loadReport();
        if (page === 'users') await loadUsers();
        if (page === 'audit') await loadAudit();
    }
    async function login(event) {
        event.preventDefault();
        var button = event.target.querySelector('button[type=submit]');
        await pending(button, async function () {
            showMessage('login-error', '');
            try {
                var result = await api('/auth/login', { method: 'POST', body: { username: byId('login-name').value.trim(), password: byId('login-password').value } });
                state.token = result.token;
                state.expiresAt = result.expiresAt;
                state.user = await api('/auth/me');
                try {
                    state.options = await api('/accidents/options');
                } catch (optionsError) {
                    if (optionsError.status === 404) {
                        throw new Error('В этом запуске журнал аварий недоступен: открыт профиль мониторинга KPI. Попросите администратора запустить профиль журнала по инструкции проекта.');
                    }
                    throw optionsError;
                }
                synchronizeClock(state.options.serverTimeUtc);
                byId('login-password').value = '';
                byId('login-screen').hidden = true;
                byId('application').hidden = false;
                byId('session').hidden = false;
                byId('session-name').textContent = state.user.username + ' · ' + (roleNames[state.user.role] || state.user.role);
                var permissions = state.options.permissions;
                byId('new-tab').hidden = !permissions.canWrite;
                byId('new-accident').hidden = !permissions.canWrite;
                byId('users-tab').hidden = !permissions.canManageUsers;
                byId('audit-tab').hidden = !permissions.canManageUsers;
                setupOptions();
                if (!state.filters) resetFilters();
                if (state.dirty && state.page === 'editor' && permissions.canWrite) await changePage('editor', true);
                else await changePage('reports', true);
            } catch (error) {
                state.token = null;
                byId('login-screen').hidden = false;
                byId('application').hidden = true;
                byId('session').hidden = true;
                showMessage('login-error', errorText(error), true);
            }
        });
    }
    function setupOptions() {
        var values = state.options;
        [['direction', values.directions], ['center', values.centers], ['networkType', values.networkTypes], ['alarmType', values.alarmTypes]].forEach(function (pair) {
            var existing = byId(pair[0]).value;
            fillSelect(pair[0], pair[1]);
            if (pair[1].indexOf(existing) !== -1) byId(pair[0]).value = existing;
        });
        fillSelect('filter-direction', values.directions, 'Все направления');
        fillSelect('filter-center', values.centers, 'Все ТЦ');
        fillSelect('filter-network', values.networkTypes, 'Все типы сети');
        if (state.filters) {
            byId('filter-direction').value = state.filters.direction || '';
            byId('filter-center').value = state.filters.center || '';
            byId('filter-network').value = state.filters.networkType || '';
        }
    }
    function resetFilters() {
        var end = new Date(serverNow());
        end.setUTCHours(0, 0, 0, 0);
        end.setUTCDate(end.getUTCDate() + 1);
        var start = new Date(end.getTime() - 7 * 24 * 3600 * 1000);
        byId('filter-from').value = utcInput(start.toISOString());
        byId('filter-to').value = utcInput(end.toISOString());
        ['filter-direction', 'filter-center', 'filter-category', 'filter-network', 'filter-search'].forEach(function (id) { byId(id).value = ''; });
        byId('filter-status').value = 'all';
        state.filters = readFilters();
        state.reportPage = 1;
    }
    function readFilters() {
        var result = { from: utcValue('filter-from'), to: utcValue('filter-to'), status: byId('filter-status').value };
        if (Date.parse(result.from) >= Date.parse(result.to)) throw new Error('Начало периода должно быть раньше конца.');
        [['direction', 'filter-direction'], ['center', 'filter-center'], ['category', 'filter-category'], ['networkType', 'filter-network'], ['search', 'filter-search']].forEach(function (pair) {
            var value = byId(pair[1]).value.trim();
            if (value) result[pair[0]] = value;
        });
        return result;
    }
    function filterQuery(paged) {
        var query = new URLSearchParams();
        Object.keys(state.filters).forEach(function (key) { query.set(key, state.filters[key]); });
        if (paged) { query.set('page', state.reportPage); query.set('pageSize', 25); }
        return '?' + query.toString();
    }
    function addCell(row, main, secondary, className) {
        var cell = node('td', null, className);
        cell.appendChild(node('span', main));
        if (secondary) cell.appendChild(node('small', secondary));
        row.appendChild(cell);
        return cell;
    }
    function emptyRow(body, columns, text) {
        var row = node('tr');
        var cell = node('td', text, 'empty');
        cell.colSpan = columns;
        row.appendChild(cell);
        body.appendChild(row);
    }
    async function loadReport() {
        var sequence = ++state.reportSequence;
        byId('report-state').textContent = 'Загружаем события…';
        byId('export').disabled = true;
        try {
            var result = await api('/accidents' + filterQuery(true));
            if (sequence !== state.reportSequence || !state.token) return;
            synchronizeClock(result.serverTimeUtc);
            var summary = result.summary;
            byId('stat-total').textContent = summary.total;
            byId('stat-open').textContent = summary.open;
            byId('stat-closed').textContent = summary.closed;
            byId('stat-category').textContent = summary.categoryOne;
            byId('table-count').textContent = result.total;
            byId('report-state').textContent = 'Обновлено: ' + dateText(result.serverTimeUtc) + ' UTC';
            var body = byId('accident-rows');
            empty(body);
            result.items.forEach(function (item) {
                var row = node('tr');
                addCell(row, '#' + item.id, 'Категория ' + item.category);
                addCell(row, item.direction, item.center);
                addCell(row, item.serviceElement, item.description);
                addCell(row, item.alarmType, item.networkType + ' · БС: ' + (item.isSector ? 'С' : item.stationCount));
                addCell(row, dateText(item.beginTime).slice(0, 10), dateText(item.beginTime).slice(11));
                var status = node('td');
                status.appendChild(node('span', item.endTime ? 'Завершена' : '● НВ', 'badge' + (item.endTime ? '' : ' badge-open')));
                row.appendChild(status);
                addCell(row, formatSeconds(item.durationSeconds), null, 'mono');
                var actions = node('td');
                actions.appendChild(actionButton('Открыть →', 'button-quiet', function () { openDetails(item.id); }));
                row.appendChild(actions);
                body.appendChild(row);
            });
            if (!result.items.length) emptyRow(body, 8, 'Нет событий. Измените период или зарегистрируйте новую аварию.');
            byId('page-label').textContent = 'Страница ' + result.page + ' из ' + Math.max(1, Math.ceil(result.total / result.pageSize));
            byId('previous-page').disabled = result.page <= 1;
            byId('next-page').disabled = result.page * result.pageSize >= result.total;
            byId('export').disabled = false;
        } catch (error) {
            if (sequence !== state.reportSequence) return;
            byId('report-state').textContent = 'Данные не обновлены';
            showMessage('notice', errorText(error), true);
        }
    }
    async function exportReport() {
        await pending(byId('export'), async function () {
            try {
                var blob = await api('/accidents/export.csv' + filterQuery(false), { csv: true });
                var url = URL.createObjectURL(blob);
                var link = node('a');
                link.href = url;
                link.download = 'motiv_' + state.filters.from.slice(0, 10) + '_' + state.filters.to.slice(0, 10) + '.csv';
                document.body.appendChild(link);
                link.click();
                link.remove();
                setTimeout(function () { URL.revokeObjectURL(url); }, 1000);
            } catch (error) { showMessage('notice', errorText(error), true); }
        });
    }
    function newAccident() {
        if (!state.options.permissions.canWrite || !discardAllowed()) return;
        byId('accident-form').reset();
        state.editingId = null;
        state.editingVersion = 0;
        byId('editor-title').textContent = 'Регистрация аварии';
        byId('editor-eyebrow').textContent = 'АВАРИИ / НОВАЯ ЗАПИСЬ';
        byId('beginTime').value = utcInput(null, true);
        byId('endTime').value = '';
        byId('stationCount').value = '1';
        byId('stationCount').disabled = false;
        byId('stationCount').required = true;
        ['batterySeconds', 'generatorSeconds', 'downtimeSeconds'].forEach(function (id) { byId(id).value = '00:00:00'; });
        showMessage('editor-errors', '');
        state.dirty = false;
        changePage('editor', true);
        updateCalculations();
        byId('pw').focus();
    }
    function readAccident() {
        var result = {};
        ['pw', 'direction', 'center', 'networkType', 'serviceElement', 'equipment', 'alarmType', 'description', 'notified', 'notes'].forEach(function (id) { result[id] = byId(id).value.trim(); });
        result.category = Number(byId('category').value);
        result.isSector = byId('isSector').checked;
        result.stationCount = result.isSector ? null : Number(byId('stationCount').value);
        result.beginTime = utcValue('beginTime');
        result.endTime = utcValue('endTime', true);
        result.batterySeconds = parseSeconds(byId('batterySeconds').value, 'АКБ');
        result.generatorSeconds = parseSeconds(byId('generatorSeconds').value, 'Генератор');
        result.downtimeSeconds = parseSeconds(byId('downtimeSeconds').value, 'Перерыв связи');
        if (result.endTime && Date.parse(result.endTime) < Date.parse(result.beginTime)) throw new Error('Окончание не может быть раньше начала.');
        if (!result.isSector && result.downtimeSeconds * result.stationCount > maximumSeconds) throw new Error('Общий простой превышает 838:59:59 — предел поля TIME. Разделите запись или согласуйте другой способ учёта.');
        if (state.editingId !== null) result.version = state.editingVersion;
        return result;
    }
    function updateCalculations() {
        if (state.page !== 'editor') return;
        var sector = byId('isSector').checked;
        byId('stationCount').disabled = sector;
        byId('stationCount').required = !sector;
        var start = Date.parse(byId('beginTime').value + 'Z');
        var finish = byId('endTime').value ? Date.parse(byId('endTime').value + 'Z') : serverNow();
        byId('calculated-duration').textContent = isNaN(start) || isNaN(finish) || finish < start ? 'Проверьте даты' : formatSeconds((finish - start) / 1000);
        if (sector) byId('calculated-total').textContent = 'Не определён для сектора';
        else {
            try {
                var down = parseSeconds(byId('downtimeSeconds').value, 'Перерыв');
                var count = Number(byId('stationCount').value);
                if (count < 1 || count > 10000 || Math.floor(count) !== count) throw new Error('Количество');
                var total = down * count;
                byId('calculated-total').textContent = total > maximumSeconds ? 'Превышен предел TIME' : formatSeconds(total);
            } catch (_) { byId('calculated-total').textContent = 'Проверьте количество и время'; }
        }
    }
    async function saveAccident(event) {
        event.preventDefault();
        var button = event.target.querySelector('button[type=submit]');
        await pending(button, async function () {
            showMessage('editor-errors', '');
            try {
                var body = readAccident();
                var path = '/accidents' + (state.editingId === null ? '' : '/' + state.editingId);
                var result = await api(path, { method: state.editingId === null ? 'POST' : 'PUT', body: body });
                state.dirty = false;
                await changePage('reports', true);
                showMessage('notice', 'Авария #' + result.id + ' сохранена.');
            } catch (error) {
                showMessage('editor-errors', errorText(error), true);
                byId('editor-errors').scrollIntoView({ block: 'center' });
            }
        });
    }
    async function editAccident(id) {
        if (!discardAllowed()) return;
        try {
            var item = await api('/accidents/' + id);
            synchronizeClock(item.serverTimeUtc);
            state.editingId = item.id;
            state.editingVersion = item.version;
            ['pw', 'direction', 'center', 'category', 'networkType', 'serviceElement', 'equipment', 'alarmType', 'description', 'notified', 'notes'].forEach(function (key) { byId(key).value = item[key] === null ? '' : item[key]; });
            byId('isSector').checked = item.isSector;
            byId('stationCount').value = item.stationCount === null ? '1' : item.stationCount;
            byId('beginTime').value = utcInput(item.beginTime, true);
            byId('endTime').value = item.endTime ? utcInput(item.endTime, true) : '';
            ['batterySeconds', 'generatorSeconds', 'downtimeSeconds'].forEach(function (key) { byId(key).value = formatSeconds(item[key]); });
            byId('editor-title').textContent = 'Редактирование аварии #' + item.id;
            byId('editor-eyebrow').textContent = 'АВАРИИ / #' + item.id + ' / ВЕРСИЯ ' + item.version;
            showMessage('editor-errors', '');
            state.dirty = false;
            closeDetails();
            await changePage('editor', true);
            updateCalculations();
            byId('pw').focus();
        } catch (error) { showMessage('notice', errorText(error), true); }
    }
    async function openDetails(id) {
        var sequence = ++state.detailSequence;
        try {
            var item = await api('/accidents/' + id);
            if (sequence !== state.detailSequence || !state.token) return;
            state.modalItem = item;
            synchronizeClock(item.serverTimeUtc);
            byId('detail-title').textContent = '#' + item.id + ' · ' + item.serviceElement;
            var details = byId('detail-fields');
            empty(details);
            var fields = [
                ['1. PW/NTT', item.pw], ['2. Направление', item.direction], ['3. ТЦ', item.center], ['4. Категория', item.category],
                ['5. Тип сети', item.networkType], ['6. Сетевой элемент', item.serviceElement], ['7. Количество БС', item.isSector ? 'С — отдельный сектор' : item.stationCount],
                ['8. Оборудование', item.equipment], ['9. Тип аварии', item.alarmType], ['10. Описание', item.description],
                ['11. Начало UTC', dateText(item.beginTime)], ['12. Окончание UTC', dateText(item.endTime)],
                ['13. АКБ', formatSeconds(item.batterySeconds)], ['14. Генератор', formatSeconds(item.generatorSeconds)],
                ['15. Длительность', formatSeconds(item.durationSeconds)], ['16. Перерыв связи 1 БС', formatSeconds(item.downtimeSeconds)],
                ['17. Общий простой', formatSeconds(item.totalDowntimeSeconds)], ['18. Кому сообщили', item.notified], ['19. Подробности', item.notes],
                ['Создал', item.createdByName], ['Версия записи', item.version]
            ];
            fields.forEach(function (pair) {
                var group = node('div');
                group.appendChild(node('dt', pair[0]));
                group.appendChild(node('dd', pair[1] === null || pair[1] === undefined || pair[1] === '' ? '—' : pair[1]));
                details.appendChild(group);
            });
            var actions = byId('detail-actions');
            empty(actions);
            if (state.options.permissions.canWrite) actions.appendChild(actionButton('Редактировать', 'button-primary', function () { editAccident(item.id); }));
            if (state.options.permissions.canDelete) actions.appendChild(actionButton('Удалить запись', 'button-danger', async function (event) {
                if (!window.confirm('Удалить аварию #' + item.id + ' без возможности восстановления? История действия останется в журнале.')) return;
                await pending(event.currentTarget, async function () {
                    try {
                        await api('/accidents/' + item.id + '?version=' + item.version, { method: 'DELETE' });
                        closeDetails();
                        await loadReport();
                        showMessage('notice', 'Авария #' + item.id + ' удалена.');
                    } catch (error) { closeDetails(); showMessage('notice', errorText(error), true); }
                });
            }));
            state.previousFocus = document.activeElement;
            byId('detail-overlay').hidden = false;
            document.body.classList.add('modal-open');
            byId('close-details').focus();
        } catch (error) { showMessage('notice', errorText(error), true); }
    }
    function closeDetails() {
        state.detailSequence += 1;
        byId('detail-overlay').hidden = true;
        document.body.classList.remove('modal-open');
        state.modalItem = null;
        if (state.previousFocus && document.body.contains(state.previousFocus)) state.previousFocus.focus();
        state.previousFocus = null;
    }
    async function loadUsers() {
        var sequence = ++state.userSequence;
        try {
            var users = await api('/users');
            if (sequence !== state.userSequence || !state.token) return;
            var body = byId('user-rows');
            empty(body);
            users.forEach(function (user) {
                var row = node('tr');
                addCell(row, user.username, Number(user.userId) === Number(state.user.userId) ? 'Это вы' : null);
                var roleCell = node('td');
                var role = node('select');
                role.setAttribute('aria-label', 'Роль пользователя ' + user.username);
                Object.keys(roleNames).forEach(function (name) { option(role, name, roleNames[name], user.role === name); });
                roleCell.appendChild(role);
                row.appendChild(roleCell);
                var activeCell = node('td');
                var active = node('input');
                active.type = 'checkbox'; active.checked = user.active;
                active.setAttribute('aria-label', 'Активен: ' + user.username);
                activeCell.appendChild(active); row.appendChild(activeCell);
                var actions = node('td');
                actions.appendChild(actionButton('Сохранить', 'button-quiet', async function (event) {
                    await pending(event.currentTarget, async function () {
                        try {
                            await api('/users/' + user.userId + '/access', { method: 'PUT', body: { role: role.value, active: active.checked, version: user.version } });
                            await loadUsers();
                            showMessage('notice', 'Доступ пользователя ' + user.username + ' обновлён.');
                        } catch (error) { showMessage('notice', errorText(error), true); }
                    });
                }));
                row.appendChild(actions); body.appendChild(row);
            });
            if (!users.length) emptyRow(body, 4, 'Пользователей нет.');
        } catch (error) { if (sequence === state.userSequence) showMessage('notice', errorText(error), true); }
    }
    async function createUser(event) {
        event.preventDefault();
        await pending(event.target.querySelector('button[type=submit]'), async function () {
            showMessage('user-errors', '');
            try {
                if (new TextEncoder().encode(byId('new-password').value).length > 72) throw new Error('Пароль превышает 72 байта UTF-8. Сократите его; кириллица занимает больше одного байта на символ.');
                await api('/users', { method: 'POST', body: { username: byId('new-username').value.trim(), password: byId('new-password').value, role: byId('new-role').value } });
                event.target.reset();
                await loadUsers();
                showMessage('notice', 'Пользователь создан. Передайте ему начальный пароль безопасным способом.');
            } catch (error) { showMessage('user-errors', errorText(error), true); }
        });
    }
    async function loadAudit() {
        var sequence = ++state.auditSequence;
        try {
            var result = await api('/accidents/audit?page=' + state.auditPage + '&pageSize=25');
            if (sequence !== state.auditSequence || !state.token) return;
            var body = byId('audit-rows');
            empty(body);
            var actionNames = { create: 'Создание', update: 'Изменение', delete: 'Удаление', user_create: 'Создание пользователя', access_update: 'Изменение доступа' };
            result.items.forEach(function (item) {
                var row = node('tr');
                addCell(row, dateText(item.occurredAt));
                addCell(row, item.username);
                addCell(row, item.accidentId === null ? '—' : '#' + item.accidentId);
                var actionParts = item.action.split(':');
                var actionText = actionNames[actionParts[0]] || item.action;
                if (actionParts.length > 1 && (actionParts[0] === 'user_create' || actionParts[0] === 'access_update')) actionText += ' #' + actionParts[1];
                addCell(row, actionText);
                body.appendChild(row);
            });
            if (!result.items.length) emptyRow(body, 4, 'Действий пока нет.');
            byId('audit-label').textContent = 'Страница ' + result.page + ' · записей: ' + result.total;
            byId('previous-audit').disabled = result.page <= 1;
            byId('next-audit').disabled = result.page * result.pageSize >= result.total;
        } catch (error) { if (sequence === state.auditSequence) showMessage('notice', errorText(error), true); }
    }

    // Подписки на события видны в одном месте — их легко проследить от кнопки до API.
    byId('login-form').addEventListener('submit', login);
    byId('logout').addEventListener('click', function () {
        if (!discardAllowed()) return;
        state.dirty = false;
        state.editingId = null;
        byId('accident-form').reset();
        resetSession('Вы вышли из системы.');
    });
    byId('brand').addEventListener('click', function (event) { event.preventDefault(); if (state.token) changePage('reports'); });
    Array.prototype.forEach.call(document.querySelectorAll('[data-page]'), function (button) {
        button.addEventListener('click', function () {
            var page = button.getAttribute('data-page');
            if (page === 'editor') newAccident(); else changePage(page);
        });
    });
    byId('new-accident').addEventListener('click', newAccident);
    byId('cancel-edit').addEventListener('click', function () { changePage('reports'); });
    byId('accident-form').addEventListener('submit', saveAccident);
    byId('accident-form').addEventListener('input', function () { state.dirty = true; updateCalculations(); });
    byId('accident-form').addEventListener('change', function () { state.dirty = true; updateCalculations(); });
    byId('filter-form').addEventListener('submit', function (event) {
        event.preventDefault();
        try { state.filters = readFilters(); state.reportPage = 1; showMessage('notice', ''); loadReport(); }
        catch (error) { showMessage('notice', errorText(error), true); }
    });
    byId('filter-form').addEventListener('input', function () { byId('report-state').textContent = 'Фильтры изменены. Нажмите «Показать события». CSV пока использует предыдущие применённые фильтры.'; });
    byId('reset-filters').addEventListener('click', function () { resetFilters(); loadReport(); });
    byId('previous-page').addEventListener('click', function () { if (state.reportPage > 1) { state.reportPage -= 1; loadReport(); } });
    byId('next-page').addEventListener('click', function () { state.reportPage += 1; loadReport(); });
    byId('export').addEventListener('click', exportReport);
    byId('close-details').addEventListener('click', closeDetails);
    byId('detail-overlay').addEventListener('click', function (event) { if (event.target === byId('detail-overlay')) closeDetails(); });
    byId('refresh-users').addEventListener('click', loadUsers);
    byId('user-form').addEventListener('submit', createUser);
    byId('refresh-audit').addEventListener('click', loadAudit);
    byId('previous-audit').addEventListener('click', function () { if (state.auditPage > 1) { state.auditPage -= 1; loadAudit(); } });
    byId('next-audit').addEventListener('click', function () { state.auditPage += 1; loadAudit(); });
    document.addEventListener('keydown', function (event) {
        if (byId('detail-overlay').hidden) return;
        if (event.key === 'Escape') closeDetails();
        if (event.key === 'Tab') {
            var controls = byId('detail-overlay').querySelectorAll('button:not([disabled]),a[href],input,select,textarea');
            if (!controls.length) return;
            var first = controls[0], last = controls[controls.length - 1];
            if (event.shiftKey && document.activeElement === first) { event.preventDefault(); last.focus(); }
            else if (!event.shiftKey && document.activeElement === last) { event.preventDefault(); first.focus(); }
        }
    });
    window.addEventListener('beforeunload', function (event) {
        if (!state.dirty) return;
        event.preventDefault(); event.returnValue = '';
    });
    setInterval(function () {
        updateCalculations();
        if (state.token && state.expiresAt && Date.parse(state.expiresAt) <= serverNow()) resetSession('Срок входа истёк. Войдите снова; несохранённая форма сохранена в памяти страницы.');
    }, 1000);
}());
