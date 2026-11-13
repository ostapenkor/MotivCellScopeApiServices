-- Канонический модуль журнала аварий: совместим с MariaDB 10.1.
-- Существующие KPI-таблицы и users не удаляются и не перестраиваются.
SET NAMES utf8mb4;
CREATE TABLE IF NOT EXISTS users (
 user_id INT UNSIGNED NOT NULL AUTO_INCREMENT PRIMARY KEY,
 username VARCHAR(64) NOT NULL UNIQUE,
 password_hash VARCHAR(255) NOT NULL,
 created_at DATETIME(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
CREATE TABLE IF NOT EXISTS journal_user_access (
 user_id INT UNSIGNED NOT NULL PRIMARY KEY,
 role ENUM('admin','duty','engineer') NOT NULL DEFAULT 'engineer',
 active TINYINT(1) NOT NULL DEFAULT 1,
 version INT NOT NULL DEFAULT 1,
 FOREIGN KEY(user_id) REFERENCES users(user_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
-- Доступ уже существующих пользователей: admin получает admin, остальные — просмотр.
-- Повторный запуск не меняет назначенные администратором права.
INSERT IGNORE INTO journal_user_access(user_id,role)
 SELECT user_id,IF(username='admin','admin','engineer') FROM users;
CREATE TABLE IF NOT EXISTS accident_reports (
 id INT NOT NULL AUTO_INCREMENT PRIMARY KEY,
 PW VARCHAR(450) NOT NULL DEFAULT '', NAPR VARCHAR(450) NOT NULL,
 TEHC VARCHAR(450) NOT NULL, KATEG TINYINT NOT NULL,
 NETW_TYPE VARCHAR(450) NOT NULL, SERV_ELEM VARCHAR(450) NOT NULL,
 NUMB_RBS INT NOT NULL,
 EQIP_TYPE VARCHAR(450) NOT NULL DEFAULT '', ALARM_TYPE VARCHAR(450) NOT NULL,
 DESCR VARCHAR(450) NOT NULL, BEGIN_TIME DATETIME NOT NULL,
 END_TIME DATETIME NOT NULL DEFAULT '0000-00-00 00:00:00',
 AKB TIME NOT NULL DEFAULT '00:00:00', DGU TIME NOT NULL DEFAULT '00:00:00',
 LENGHT VARCHAR(450) NOT NULL, PROSTOY TIME NOT NULL DEFAULT '00:00:00',
 COMMON_PROSTOY TIME NULL,
 ALARM_WHO VARCHAR(450) NOT NULL DEFAULT '', DESC_NOTICE VARCHAR(450) NOT NULL DEFAULT '',
 created_by INT UNSIGNED NOT NULL, version INT NOT NULL DEFAULT 1,
 FOREIGN KEY(created_by) REFERENCES users(user_id),
 INDEX ix_journal_begin(BEGIN_TIME), INDEX ix_journal_end(END_TIME)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
CREATE TABLE IF NOT EXISTS accident_audit (
 id INT NOT NULL AUTO_INCREMENT PRIMARY KEY, user_id INT UNSIGNED NOT NULL,
 accident_id INT NULL, action VARCHAR(40) NOT NULL,
 occurred_at DATETIME NOT NULL,
 FOREIGN KEY(user_id) REFERENCES users(user_id), INDEX ix_journal_audit_time(occurred_at)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
-- accident_id без FK: история сохраняется после удаления аварии.
