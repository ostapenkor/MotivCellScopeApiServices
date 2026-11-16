<?php

function e(mixed $value): string
{
    return htmlspecialchars((string) ($value ?? ''), ENT_QUOTES, 'UTF-8');
}

function fmtDate(?string $iso, string $format = 'd.m H:i'): string
{
    if (!$iso) {
        return '—';
    }
    $ts = strtotime($iso);
    return $ts ? date($format, $ts) : $iso;
}

function fmtDateTimeLocal(?string $iso): string
{
    if (!$iso) {
        return '';
    }
    $ts = strtotime($iso);
    return $ts ? date('Y-m-d\TH:i', $ts) : '';
}

/** datetime-local (Y-m-d\TH:i) -> ISO 8601 UTC для API. */
function toIso(?string $local): ?string
{
    if ($local === null || trim($local) === '') {
        return null;
    }
    $local = trim($local);
    if (preg_match('/Z$|[+-]\d{2}:\d{2}$/', $local)) {
        return $local;
    }
    return str_replace(' ', 'T', $local) . (strlen($local) === 16 ? ':00Z' : 'Z');
}

function severityBadge(?string $severity): string
{
    $map = [
        'critical' => ['badge-red', 'Critical'],
        'high'     => ['badge-red', 'High'],
        'warning'  => ['badge-yellow', 'Warning'],
        'medium'   => ['badge-yellow', 'Medium'],
        'info'     => ['badge-blue', 'Info'],
        'low'      => ['badge-green', 'Low'],
    ];
    [$class, $label] = $map[strtolower((string) $severity)] ?? ['badge-blue', (string) $severity];
    return '<span class="badge ' . $class . '">' . e($label) . '</span>';
}

function anomalyStatusLabel(?string $status): string
{
    return [
        'new'            => 'Новая',
        'acknowledged'   => 'Подтверждена',
        'explained'      => 'Объяснена',
        'resolved'       => 'Решена',
        'false_positive' => 'Ложная тревога',
    ][$status ?? ''] ?? (string) $status;
}

function anomalyStatusBadge(?string $status): string
{
    $class = [
        'new' => 'badge-red', 'acknowledged' => 'badge-yellow', 'explained' => 'badge-blue',
        'resolved' => 'badge-green', 'false_positive' => 'badge-blue',
    ][$status ?? ''] ?? 'badge-blue';
    return '<span class="badge ' . $class . '">' . e(anomalyStatusLabel($status)) . '</span>';
}

function roleBadge(?string $role): string
{
    [$class, $label] = [
        'admin'    => ['role-admin', 'Администратор'],
        'engineer' => ['role-eng', 'Инженер'],
        'operator' => ['role-op', 'Дежурный'],
    ][$role ?? ''] ?? ['', (string) $role];
    return '<span class="role-badge ' . $class . '">' . e($label) . '</span>';
}

function auditActionLabel(?string $action): string
{
    return [
        'login' => 'Вход', 'create_user' => 'Создание пользователя',
        'create_incident' => 'Создание аварии', 'update_incident' => 'Изменение аварии',
        'delete_incident' => 'Удаление аварии', 'explain_anomaly' => 'Объяснение аномалии',
        'update_anomaly_status' => 'Смена статуса аномалии', 'export_incidents' => 'Выгрузка инцидентов',
        'export_report' => 'Выгрузка отчёта', 'view_audit_log' => 'Просмотр аудита',
        'view_users' => 'Просмотр пользователей', 'detect_anomalies' => 'Детектор аномалий',
        'detect_availability_anomalies' => 'Детектор доступности', 'refresh_kpi' => 'Пересчёт KPI',
    ][$action ?? ''] ?? (string) $action;
}

/** Достаёт текст ошибки из тел ответов API разных форматов. */
function apiErrorText(array $res): string
{
    $body = $res['body'] ?? null;
    if (!empty($res['error'])) {
        return $res['error'];
    }
    if (is_array($body)) {
        if (!empty($body['message'])) {
            return (string) $body['message'];
        }
        if (!empty($body['error'])) {
            $text = (string) $body['error'];
            if (!empty($body['field'])) {
                $text .= ' (поле: ' . $body['field'] . ')';
            }
            return $text;
        }
    }
    return 'Ошибка API (HTTP ' . ($res['status'] ?? 0) . ')';
}
