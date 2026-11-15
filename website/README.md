# MotivCellScope — PHP-сайт поверх REST API

Серверный PHP без фреймворка: браузер ходит только на сайт, сайт через cURL
ходит в REST API, JWT хранится только в PHP-сессии.

## Запуск

Весь стек (БД + API + сайт) из корня репозитория, нужен корневой `.env`
с секретами (см. `../DEVELOPMENT_GUIDE.md`):

```bash
docker compose up --build
```

- Сайт: http://localhost:8000
- API: http://localhost:8080, Swagger: http://localhost:8080/swagger

Только сайт при уже работающем API:

```bash
API_BASE_URL=http://localhost:8080 php -S localhost:8000 -t website/public
```

## Структура

- `public/` — страницы: `login.php`, `index.php` (дашборд), `incidents.php`,
  `incident.php`, `incident_form.php`, `monitoring.php`, `reports.php`,
  `admin.php`, `export.php` (CSV), `logout.php`
- `src/` — `bootstrap.php` (сессия, guard, CSRF), `ApiClient.php`,
  `config.php` (`API_BASE_URL`), `layout.php`, `helpers.php`

## Проблемы

- **«Ошибка API (HTTP 307)» при входе** — API отвечает редиректом на HTTPS.
  В Docker API должен слушать только HTTP внутри сети: в `docker-compose.yml`
  для сервиса API задано `ASPNETCORE_URLS=http://+:8080`. После правки compose
  пересоздайте контейнеры: `docker compose up --build --force-recreate`.
- **«API недоступен»** — проверьте `curl http://localhost:8080/api/health`
  и что в `.env` заданы секреты БД/JWT.
