# MotivCellScope — руководство по сборке, запуску в VS Code, Git/GitHub и PHP-сайту

Инструкция описывает работу именно с этим репозиторием:

- бэкенд — ASP.NET Core 8 (`MotivCellScopeApiServices/MotivCellScopeApiServices.csproj`);
- база — MariaDB, поднимается `docker-compose.yml`, инициализируется корневым `init.sql`;
- секреты API/БД пробрасываются через переменные окружения и Docker Secrets;
- поверх REST API предлагается PHP-сайт в отдельной папке `website/` (серверный рендеринг через cURL).

> ⚠️ В корневом `launchSettings.json` репозитория исторически лежат значения окружения
> для Docker Compose, включая пароли. В этой инструкции значения не приводятся.
> Не добавляйте новые пароли в коммиты; для локальной работы используйте `.env`
> (уже добавлен в `.gitignore`) и переменные окружения. Если пароли из git-истории
> реальные — их следует сменить.

---

## 1. Что установить

| Инструмент | Зачем | Проверка |
|---|---|---|
| Git | Клонирование, коммиты, пуш | `git --version` |
| VS Code | Редактор и отладка | — |
| .NET SDK 8.x | Сборка и локальный запуск API без контейнера | `dotnet --info` |
| Docker Desktop (Windows/macOS) или Docker Engine (Linux) | MariaDB и запуск всего стека | `docker --version`, `docker compose version` |
| PHP 8.2+ (или XAMPP) | PHP-сайт | `php --version` |

Расширения VS Code:

- **C# Dev Kit** (Microsoft) — сборка, F5-отладка .NET;
- **Docker** (Microsoft) — управление контейнерами из VS Code;
- **GitHub Pull Requests** (GitHub) — PR, не выходя из редактора;
- **PHP Intelephense** и **PHP Server** — подсветка и быстрый запуск PHP.

---

## 2. Первый запуск проекта

### 2.1. Клонирование и открытие

```bash
git clone https://github.com/ostapenkor/MotivCellScopeApiServices.git
cd MotivCellScopeApiServices
code .
```

В VS Code откройте именно **корень репозитория** (где лежат `docker-compose.yml` и
папка `MotivCellScopeApiServices`), а не вложенную папку проекта. Дождитесь, пока
C# Dev Kit подхватит решение `MotivCellScopeApiServices.slnx`.

### 2.2. Файл `.env` с секретами

В корне репозитория создайте файл `.env` (он в `.gitignore`, в GitHub не попадёт):

```dotenv
DB_ROOT_PASSWORD=придумайте_стойкий_пароль_root
DB_USER=admin
DB_PASSWORD=придумайте_стойкий_пароль_пользователя
JWT_SECRET_KEY=длинный_случайный_секрет_минимум_32_символа_например_64_hex
```

Как это работает:

- `docker-compose.yml` объявляет секреты из переменных окружения:
  `db_root_password` ← `DB_ROOT_PASSWORD`, `db_user` ← `DB_USER`,
  `db_password` ← `DB_PASSWORD`, `jwt_key` ← `JWT_SECRET_KEY`;
- MariaDB при **первой** инициализации создаёт пользователя `DB_USER` с паролем
  `DB_PASSWORD` и выполняет корневой `/init.sql`;
- API внутри контейнера читает те же секреты из `/run/secrets/*` (см. `Program.cs`).

Важные следствия:

- Меняете `DB_PASSWORD` после того, как база уже создана (том `motiv_db_data`
  существует) — пароль пользователя в MariaDB **не** поменяется, API перестанет
  подключаться. Либо меняйте пароль внутри БД, либо пересоздавайте том (п. 3.4).
- `JWT_SECRET_KEY` должен быть достаточно длинным (ключ HS256), иначе API упадёт
  при старте/авторизации.

### 2.3. Способ А (рекомендуется): весь стек в Docker

В терминале VS Code (`Terminal → New Terminal`), из корня репозитория:

```bash
docker compose up --build
```

Что произойдёт:

1. Соберётся образ API из `MotivCellScopeApiServices/Dockerfile`;
2. Поднимется MariaDB (`motiv_mariadb_container`, порт хоста `3306`);
3. При первом старте выполнится корневой `init.sql`: таблицы, представления,
   хранимые процедуры, демо-данные и пользователи `engineer`/`operator`/`admin`;
4. API (`motiv_api_container`) стартует на `http://localhost:8080`.
   `docker-compose.override.yml` выставляет `ASPNETCORE_ENVIRONMENT=Development`,
   поэтому Swagger доступен.

Проверка:

```bash
curl http://localhost:8080/api/health
# {"status":"ok","database":"ok","version":"1.0.0","timestamp":"..."}

start http://localhost:8080/swagger   # Windows; на macOS: open, на Linux: xdg-open
```

Если API-контейнер при самом первом запуске завершился с ошибкой подключения
(MariaDB ещё не успела инициализироваться), просто перезапустите его:

```bash
docker compose restart motivcellscopeapiservices
docker compose logs -f motivcellscopeapiservices
```

Остановка: `Ctrl+C` в терминале compose, либо `docker compose down`
(контейнеры остановятся, данные БД в томе сохранятся).

### 2.4. Способ Б: API локально через dotnet (удобно для отладки F5)

База остаётся в Docker, API запускается на хосте:

```bash
docker compose up -d motiv-db
```

Теперь нюанс: в `appsettings.json` строка подключения —
`server=motiv-db;port=3306;database=motiv_report;`, а имя `motiv-db` резолвится
только внутри docker-сети. На хосте её надо переопределить переменной окружения,
равно как и секреты, которые `Program.cs` читает ключами `db_user`, `db_password`,
`jwt_key`.

PowerShell (Windows):

```powershell
$env:ConnectionStrings__MotivReportConnection = "server=localhost;port=3306;database=motiv_report;"
$env:db_user = "admin"
$env:db_password = "ваш_пароль_из_.env"
$env:jwt_key = "ваш_jwt_секрет_из_.env"
cd MotivCellScopeApiServices
dotnet restore
dotnet build
dotnet run --launch-profile http
```

bash (Linux/macOS):

```bash
export ConnectionStrings__MotivReportConnection="server=localhost;port=3306;database=motiv_report;"
export db_user="admin"
export db_password="ваш_пароль_из_.env"
export jwt_key="ваш_jwt_секрет_из_.env"
cd MotivCellScopeApiServices
dotnet restore && dotnet build
dotnet run --launch-profile http
```

API поднимется на `http://localhost:5205`, Swagger сам откроется в браузере
(профиль `http` из `Properties/launchSettings.json`). Остановка — `Ctrl+C`.

Отладка по F5 в VS Code: папки `.vscode` в репозитории нет — при первом нажатии
F5 VS Code сам предложит создать `.vscode/launch.json`. Выберите конфигурацию
«.NET Core Launch (web)». Секреты в `launch.json` не вписывайте значениями —
используйте ссылки на переменные окружения, например:

```jsonc
"env": {
  "ASPNETCORE_ENVIRONMENT": "Development",
  "ConnectionStrings__MotivReportConnection": "server=localhost;port=3306;database=motiv_report;",
  "db_user": "${env:DB_USER}",
  "db_password": "${env:DB_PASSWORD}",
  "jwt_key": "${env:JWT_SECRET_KEY}"
}
```

и задайте `DB_USER`/`DB_PASSWORD`/`JWT_SECRET_KEY` в окружении своей ОС или в
`.env`-совместимом способе вашей команды. Сам `launch.json` без секретов коммитить
можно и полезно.

### 2.5. Быстрая проверка API после запуска

```bash
# 1. Здоровье
curl http://localhost:8080/api/health

# 2. Логин (подставьте реальные креды развёрнутой БД)
curl -X POST http://localhost:8080/api/auth/login \
  -H "Content-Type: application/json" \
  -d '{"username":"operator","password":"********"}'

# 3. Защищённый запрос с токеном
curl -H "Authorization: Bearer <token>" http://localhost:8080/api/auth/me
```

В Swagger: кнопка **Authorize** → вставить токен → вызывать защищённые эндпоинты.
Полный контракт — в `API_DOCUMENTATION.md`.

---

## 3. Повседневная работа

### 3.1. Сборка и перезапуск после правок C#

```bash
dotnet build MotivCellScopeApiServices/MotivCellScopeApiServices.csproj
```

- Локальный запуск: остановить (`Ctrl+C`) и снова `dotnet run --launch-profile http`
  (или F5). Hot Reload для этого проекта не настроен.
- Docker: `docker compose up --build motivcellscopeapiservices` пересоберёт только API.

### 3.2. Полезные команды Docker

```bash
docker compose ps                    # состояние сервисов
docker compose logs -f motivcellscopeapiservices   # логи API
docker compose logs -f motiv-db                    # логи MariaDB
docker compose restart motivcellscopeapiservices   # рестарт API
docker compose down                                # остановить всё, данные целы
```

### 3.3. Подключение к БД для ручных проверок

```bash
docker exec -it motiv_mariadb_container mariadb -u admin -p motiv_report
# внутри: SHOW TABLES; SHOW CREATE TABLE audit_log;
```

Либо любым GUI-клиентом: хост `localhost`, порт `3306`, база `motiv_report`.

### 3.4. Полный сброс базы (инициализация заново)

Корневой `init.sql` выполняется **только** при создании пустого тома. Чтобы
переинициализировать БД (например, после правок `init.sql` или смены паролей):

```bash
docker compose down -v   # ⚠️ удаляет том motiv_db_data — ВСЕ данные БД пропадут
docker compose up --build
```

### 3.5. Типовые проблемы запуска

| Симптом | Причина | Решение |
|---|---|---|
| `server=motiv-db` не резолвится при локальном запуске | API на хосте, а имя из docker-сети | Переопределить `ConnectionStrings__MotivReportConnection` на `localhost` (п. 2.4) |
| Падение при старте из-за JWT | `jwt_key` пуст/короткий | Задать `JWT_SECRET_KEY` (контейнер) или `jwt_key` (локально), минимум 32 символа |
| API не подключается к БД после смены пароля в `.env` | Пароль в существующем томе MariaDB не обновляется | П. 3.4 или смена пароля внутри БД |
| Контейнер API завершился при первом старте | MariaDB ещё инициализировалась | `docker compose restart motivcellscopeapiservices` |
| Swagger открывается, но 404 | Окружение не Development | Для compose проверьте, что подхватился `docker-compose.override.yml`; локально используйте профиль `http` |
| Порт 8080/3306/5205 занят | Другое приложение или старый контейнер | `docker compose down`, проверить `docker ps`; либо сменить проброс порта |
| Изменения `init.sql` не применились | Том БД уже существовал | П. 3.4 |

---

## 4. Git и GitHub из VS Code

Ремоут уже настроен: `origin → https://github.com/ostapenkor/MotivCellScopeApiServices.git`.
Авторизация GitHub — по HTTPS (Git Credential Manager откроет вход в браузере)
или по SSH-ключу. Токены/пароли в чаты и в код не вставляем.

### 4.1. Ежедневный цикл (терминал)

```bash
git status                     # что изменено
git pull origin main           # забрать свежий main (или свою ветку)
git checkout -b feature/имя-задачи    # ветка под задачу от актуального main
# ... правки ...
dotnet build MotivCellScopeApiServices/MotivCellScopeApiServices.csproj
git add -A                     # или точечно: git add путь/к/файлу
git status                     # ОБЯЗАТЕЛЬНО проверить, что в коммит не едет лишнее
git commit -m "feat(incidents): добавить фильтр по severity"
git push -u origin feature/имя-задачи
```

Дальше — Pull Request: на GitHub (кнопка «Compare & pull request») или из
расширения **GitHub Pull Requests** в VS Code. После мержа ветку удалить, локально:

```bash
git checkout main
git pull origin main
git branch -d feature/имя-задачи
```

Тот же цикл без терминала: панель **Source Control** в VS Code (иконка ветки) —
изменения → `+` (stage) → сообщение → **Commit** → **Sync Changes** (push/pull).
Ветка переключается/создаётся в левом нижнем углу окна.

### 4.2. Правила, чтобы не уронить репозиторий

1. **Не коммитить секреты.** `.env` уже в `.gitignore`. Перед коммитом смотрите
   `git status`/`git diff --staged`: в диффе не должно быть паролей, токенов,
   `launch.json` со значениями секретов (только `${env:...}`).
2. **Не коммитить сборку**: `bin/`, `obj/`, `.vs/` уже игнорируются стандартным
   VisualStudio `.gitignore` — не добавляйте их силой (`git add -f`).
3. **Одна задача — одна ветка — один PR.** В `main` напрямую не пушим.
4. Сообщения коммитов по смыслу: `feat:`, `fix:`, `docs:`, `refactor:` +
   короткое описание. Например, документы этого цикла:
   `docs: документация REST API, требования к интерфейсу и руководство по запуску`.
5. Конфликт при `pull`: VS Code подсветит файлы с `<<<<<<<` — разрешить вручную,
   `dotnet build`, затем `git add` и завершить merge/rebase коммитом.
6. Случайно закоммитили лишнее до пуша: `git reset --soft HEAD~1` (откатит коммит,
   правки останутся). Уже запушили — разбираемся отдельным коммитом, историю
   чужих веток не переписываем без договорённости.

### 4.3. Что с текущими документами

Файлы `API_DOCUMENTATION.md`, `UI_REQUIREMENTS.md` и это руководство — обычные
markdown-документы в корне; коммитятся как всё остальное:

```bash
git add API_DOCUMENTATION.md UI_REQUIREMENTS.md DEVELOPMENT_GUIDE.md
git commit -m "docs: документация API, требования к UI и руководство разработчика"
```

---

## 5. PHP-сайт поверх REST API

### 5.1. Архитектура (важно выбрать правильно)

```
Браузер ──HTTP──► PHP-сайт ──cURL + JWT──► REST API ──► MariaDB
                  (сессия PHP)              (localhost:8080 или :5205)
```

**Рекомендуемая схема — серверный PHP.** PHP-код сам ходит в API через cURL,
токен хранится в PHP-сессии (`$_SESSION`) и в браузер не отдаётся. Плюсы:

- не нужна CORS-настройка (сейчас в `Program.cs` CORS **не подключён**: браузерные
  запросы с другого порта к API будут блокироваться);
- токен не светится в `localStorage`/JS, где его видно из любого скрипта;
- роль для меню берётся из ответа логина и подтверждается `/api/auth/me`,
  а доступ всё равно перепроверяет API (403 обрабатываем как «нет прав»).

Альтернатива «PHP только раздаёт HTML, а браузерный JS ходит в API напрямую»
потребует: добавить CORS в `Program.cs` (`AddCors` + `UseCors` с явным origin
сайта), хранить токен на клиенте и переписать весь дизайнерский `script.js`.
Для этого проекта серверный вариант проще и безопаснее.

Дизайнерские страницы из макетов (`login/dashboard/incidents/monitoring/
reports/admin.html`, `style.css`) при этом не выбрасываются: HTML/CSS становятся
шаблонами PHP-страниц, а `script.js` с фейковой ролью удаляется — вместо него
сессии и серверные проверки (подробный разбор экранов — в `UI_REQUIREMENTS.md`).

### 5.2. Структура папок (предложение)

Сайт кладём в тот же репозиторий, отдельной папкой `website/`:

```
website/
├── public/                 # точка входа веб-сервера (document root)
│   ├── index.php           # дашборд
│   ├── login.php
│   ├── incidents.php       # реестр аварий
│   ├── incident.php        # карточка/создание/редактирование (?id=, ?new=1)
│   ├── monitoring.php      # аномалии
│   ├── reports.php
│   ├── admin.php           # пользователи и аудит (вкладки)
│   ├── logout.php
│   └── assets/
│       ├── style.css       # сюда переезжает дизайнерский style.css
│       └── app.js          # только клиентские мелочи (подтверждения, маски)
├── src/
│   ├── config.php          # API_BASE_URL из окружения, без секретов в коде
│   ├── ApiClient.php       # cURL-обёртка над REST API
│   ├── auth.php            # guard: проверка сессии, роли, редирект на login
│   └── layout.php          # шапка/меню/подвал из макетов (один раз на все страницы)
└── .env.example            # пример переменных без значений
```

### 5.3. Конфигурация

PHP-сайту нужен только базовый адрес API — задаётся окружением:

| Где запущен PHP | `API_BASE_URL` |
|---|---|
| На хосте, API в Docker Compose | `http://localhost:8080` |
| На хосте, API через `dotnet run` | `http://localhost:5205` |
| PHP добавлен сервисом в тот же compose | `http://motivcellscopeapiservices:8080` (имя сервиса внутри сети) |

В `src/config.php`:

```php
<?php
// Базовый адрес REST API; задаётся переменной окружения API_BASE_URL
return [
    'api_base' => rtrim(getenv('API_BASE_URL') ?: 'http://localhost:8080', '/'),
];
```

### 5.4. Клиент к API (минимальный каркас)

`src/ApiClient.php` — один класс на все вызовы; страницы не должны знать про cURL:

```php
<?php
final class ApiClient
{
    public function __construct(private string $base) {}

    /** @return array{status:int, body:mixed} */
    public function request(string $method, string $path, ?array $json = null, ?string $token = null): array
    {
        $ch = curl_init($this->base . $path);
        $headers = ['Accept: application/json'];
        if ($token)   $headers[] = 'Authorization: Bearer ' . $token;
        if ($json !== null) $headers[] = 'Content-Type: application/json';

        curl_setopt_array($ch, [
            CURLOPT_CUSTOMREQUEST  => $method,
            CURLOPT_RETURNTRANSFER => true,
            CURLOPT_HTTPHEADER     => $headers,
            CURLOPT_POSTFIELDS     => $json !== null ? json_encode($json) : null,
            CURLOPT_TIMEOUT        => 15,
        ]);
        $raw    = curl_exec($ch);
        $status = curl_getinfo($ch, CURLINFO_RESPONSE_CODE);
        curl_close($ch);

        return ['status' => $status, 'body' => $raw ? json_decode($raw, true) : null];
    }
}
```

Для CSV-выгрузок (`/api/incidents/export`, `/api/reports/export`) нужен отдельный
метод, возвращающий сырое тело и заголовки файла, и страница-отдающая файл с
`header('Content-Type: text/csv; charset=utf-8')` — JSON там нет.

### 5.5. Авторизация в PHP (сессии)

Логика страниц входа и guard'а:

1. `login.php` (POST): `$api->request('POST', '/api/auth/login', ['username'=>…, 'password'=>…])`.
   При `200` сохранить в `$_SESSION`: `token`, `username`, `role`, момент истечения
   (`expiresAt`). При `401` — показать «Неверный логин или пароль» на той же странице.
2. Каждая защищённая страница в начале: нет `$_SESSION['token']` → редирект на
   `login.php`; токен истёк по времени → туда же с пометкой «сессия истекла».
3. При любом ответе API `401` в рабочем запросе — очистить сессию, редирект на вход.
   При `403` — страница/плашка «Недостаточно прав».
4. Меню по ролям (из `UI_REQUIREMENTS.md`, раздел 9): админка — только `admin`;
   кнопки создания/правок — только `operator`/`admin`. Это косметика: API права
   проверит сам, при `403` сайт показывает корректную ошибку.
5. `logout.php`: `session_destroy()` → `login.php`. (Logout в API нет — токен
   просто перестаёт использоваться и умрёт сам через 2 часа.)

Сессионную cookie держим `HttpOnly` (в PHP это `session_cookie_httponly=1`,
по умолчанию для `session_start()` выставляется в `php.ini`/кодом) и не отдаём
токен в JavaScript.

### 5.6. Запуск PHP-сайта локально

Вариант 1 — встроенный сервер PHP (быстрый старт):

```bash
# API уже запущен (Docker :8080 или dotnet :5205)
API_BASE_URL=http://localhost:8080 php -S localhost:8000 -t website/public
# сайт: http://localhost:8000/login.php
```

В PowerShell перед запуском: `$env:API_BASE_URL = "http://localhost:8080"`.

Вариант 2 — XAMPP/Apache: положить `website/public` как DocumentRoot виртуального
хоста (например, `motiv.local`), `API_BASE_URL` задать в конфиге хоста
(`SetEnv API_BASE_URL http://localhost:8080`) или в `config.php` окружения.

Вариант 3 — добавить сайт третьим сервисом в `docker-compose.yml` (по аналогии
с существующими, отдельным коммитом и после проверки вариантов 1–2):

```yaml
  motiv-web:
    image: php:8.3-apache
    volumes:
      - ./website:/var/www/html
    environment:
      - API_BASE_URL=http://motivcellscopeapiservices:8080
    ports:
      - "8000:80"
    depends_on:
      - motivcellscopeapiservices
```

Тогда сайт — `http://localhost:8000`, а внутри сети он ходит в API по имени
сервиса, минуя проброс портов.

### 5.7. Порядок переноса дизайнерских страниц

1. `style.css` → `website/public/assets/style.css` без изменений (тема сохраняется).
2. Шапку с вкладками вынести в `src/layout.php`, роль/имя — из сессии, вкладку
   админки — только для `admin`; `script.js` с выбором роли удалить.
3. Страницы по приоритету из `UI_REQUIREMENTS.md`: вход → дашборд → аварии
   (реестр+карточка) → мониторинг (таблица аномалий) → отчётность → админка.
4. Каждую страницу подключать к «своим» эндпоинтам по `API_DOCUMENTATION.md`;
   справочники в селектах наполнять из `/api/reference/all`, не хардкодить.
5. Сразу заложить состояния: загрузка, пустой ответ, ошибка API, 403, истёкшая
   сессия — иначе потом придётся перелопачивать все шаблоны.

---

## 6. Краткая шпаргалка команд

```bash
# Поднять всё
docker compose up --build

# Только БД (для локального dotnet-запуска и PHP-разработки)
docker compose up -d motiv-db

# Локальный API
dotnet run --project MotivCellScopeApiServices --launch-profile http

# Сборка-проверка перед коммитом
dotnet build MotivCellScopeApiServices/MotivCellScopeApiServices.csproj

# PHP-сайт (API уже работает)
php -S localhost:8000 -t website/public

# Git-цикл
git pull origin main
git checkout -b feature/задача
git add -A && git commit -m "feat: описание"
git push -u origin feature/задача
```
