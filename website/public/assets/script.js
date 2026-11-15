// Проверка авторизации при загрузке страницы
document.addEventListener("DOMContentLoaded", () => {
    const role = localStorage.getItem('userRole');
    const roleNames = { 'admin': 'Администратор', 'engineer': 'Инженер', 'operator': 'Дежурный' };
    
    // Если мы на странице приложения, но роли нет — кидаем на вход
    if (window.location.pathname.includes('login.html') === false) {
        if (!role) {
            window.location.href = 'login.html';
            return;
        }
        
        // Показываем роль в шапке
        const roleSpan = document.getElementById('current-role');
        if (roleSpan) roleSpan.innerText = roleNames[role];
        
        // Настраиваем права
        applyPermissions(role);
    }
});

function logout() {
    localStorage.removeItem('userRole');
    window.location.href = 'login.html';
}

function applyPermissions(role) {
    // 1. Вкладка Админ
    const adminTabs = document.querySelectorAll('.admin-only');
    adminTabs.forEach(el => {
        el.style.display = (role === 'admin') ? 'flex' : 'none';
    });

    // 2. Кнопки редактирования (карандаши, сохранить)
    // Если Дежурный (operator) — прячем. Иначе показываем.
    const editElements = document.querySelectorAll('.hide-for-operator');
    editElements.forEach(el => {
        if (role === 'operator') {
            el.style.display = 'none';
        } else {
            if (el.tagName === 'TH' || el.tagName === 'TD') {
                el.style.display = 'table-cell';
            } else {
                el.style.display = 'inline-block';
            }
        }
    });
}