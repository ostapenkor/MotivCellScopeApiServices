/* Только выбор транспорта для демонстрации. Это не переключатель прав доступа.
   /                — статический JSON, работает с Python-сервером.
   /?transport=php  — JSON через api/monitoring.php, нужен PHP или Docker.
   Настоящее подключение C# REST выполняется на сервере в TODO_REST.
*/
(function () {
    'use strict';
    var params = new URLSearchParams(window.location.search);
    window.MONITORING_CONFIG = {
        mode: params.get('transport') === 'php' ? 'api' : 'demo',
        endpoint: 'api/monitoring.php'
    };
}());
