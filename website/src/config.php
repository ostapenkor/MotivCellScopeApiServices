<?php
// Базовый адрес REST API; задаётся переменной окружения API_BASE_URL
return [
    'api_base' => rtrim(getenv('API_BASE_URL') ?: 'http://localhost:8080', '/'),
];