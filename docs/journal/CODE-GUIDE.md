# Как устроен код

Актуальная полная документация: [модель, API, запуск, импорт и объяснение кода](DATABASE.md).

Порядок изучения: wwwroot/index.html → wwwroot/app.js → Contracts/Accidents.cs → Controllers/AccidentsController.cs → Data/AccidentStore.cs → Data/JournalSchema.sql. Import отделён в AccidentStore.Import.cs, агрегаты — AccidentStore.Dashboard.cs, парсер Excel — tools/import_workbook.py. Никакого фронтенд-фреймворка не требуется.
