<?php
// Guard: подключать в начале каждой защищённой страницы
session_start();

if (empty($_SESSION['role'])) {
    header('Location: login.php');
    exit;
}

$role       = $_SESSION['role'];
$username   = $_SESSION['username'] ?? 'Пользователь';
$isAdmin    = $role === 'admin';
$isOperator = $role === 'operator';

$roleNames = ['admin' => 'Администратор', 'engineer' => 'Инженер', 'operator' => 'Дежурный'];
$roleName  = $roleNames[$role] ?? $role;