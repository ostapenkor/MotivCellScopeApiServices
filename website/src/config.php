<?php
// Конфигурация сайта. Адрес API задаётся переменной окружения API_BASE_URL:
//  - PHP на хосте, API в Docker:        http://localhost:8080
//  - PHP на хосте, API через dotnet:    http://localhost:5205
//  - PHP в docker-compose:              http://motivcellscopeapiservices:8080
return [
    'api_base' => rtrim(getenv('API_BASE_URL') ?: 'http://localhost:8080', '/'),
    'timeout'  => 20,
];
