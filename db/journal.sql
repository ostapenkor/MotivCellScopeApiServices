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
CREATE TABLE IF NOT EXISTS accident_audit (
 id INT NOT NULL AUTO_INCREMENT PRIMARY KEY, user_id INT UNSIGNED NOT NULL,
 accident_id INT NULL, action VARCHAR(40) NOT NULL,
 occurred_at DATETIME NOT NULL,
 FOREIGN KEY(user_id) REFERENCES users(user_id), INDEX ix_journal_audit_time(occurred_at)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
-- accident_id без FK: история сохраняется после удаления аварии.

-- MariaDB 10.1: справочники, факты, связи и неизменяемый источник.
SET NAMES utf8mb4;
CREATE TABLE IF NOT EXISTS journal_directions (
 id INT AUTO_INCREMENT PRIMARY KEY, code VARCHAR(64) COLLATE utf8mb4_bin NOT NULL UNIQUE,
 label VARCHAR(450) NOT NULL, external_code VARCHAR(64) NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
CREATE TABLE IF NOT EXISTS journal_centers LIKE journal_directions;
CREATE TABLE IF NOT EXISTS journal_categories (
 id INT AUTO_INCREMENT PRIMARY KEY, code VARCHAR(16) COLLATE utf8mb4_bin NOT NULL UNIQUE,
 mapped_category TINYINT NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
INSERT IGNORE INTO journal_categories(code,mapped_category) VALUES('1',1),('2',2),('3',3);
CREATE TABLE IF NOT EXISTS journal_network_types LIKE journal_directions;
CREATE TABLE IF NOT EXISTS journal_alarm_types LIKE journal_directions;
CREATE TABLE IF NOT EXISTS journal_equipment_models (
 id INT AUTO_INCREMENT PRIMARY KEY, label VARCHAR(450) COLLATE utf8mb4_bin NOT NULL,
 label_hash BINARY(32) NOT NULL UNIQUE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
CREATE TABLE IF NOT EXISTS journal_service_elements LIKE journal_equipment_models;
CREATE TABLE IF NOT EXISTS journal_tickets LIKE journal_equipment_models;
CREATE TABLE IF NOT EXISTS journal_import_batches (
 id INT AUTO_INCREMENT PRIMARY KEY, file_hash CHAR(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL UNIQUE,
 source_name VARCHAR(255) NOT NULL, time_zone VARCHAR(40) NULL,
 snapshot_at DATETIME(3) NULL, imported_at DATETIME(3) NOT NULL, result_json TEXT NULL,
 imported_by INT UNSIGNED NOT NULL, FOREIGN KEY(imported_by) REFERENCES users(user_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
CREATE TABLE IF NOT EXISTS journal_import_rows (
 id INT AUTO_INCREMENT PRIMARY KEY, batch_id INT NOT NULL,
 sheet_name VARCHAR(100) NOT NULL, row_number INT NOT NULL,
 kind VARCHAR(16) NOT NULL, raw_json LONGTEXT NOT NULL, issues_json TEXT NOT NULL,
 UNIQUE(batch_id,sheet_name,row_number),
 FOREIGN KEY(batch_id) REFERENCES journal_import_batches(id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
CREATE TABLE IF NOT EXISTS journal_import_sheets (
 batch_id INT NOT NULL, sheet_name VARCHAR(100) NOT NULL, snapshot_at DATETIME(3) NULL,
 PRIMARY KEY(batch_id,sheet_name), FOREIGN KEY(batch_id) REFERENCES journal_import_batches(id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
CREATE TABLE IF NOT EXISTS journal_accidents (
 id INT AUTO_INCREMENT PRIMARY KEY,
 direction_id INT NOT NULL, center_id INT NULL, category_id INT NOT NULL,
 service_element_id INT NOT NULL, ticket_id INT NULL, alarm_type_id INT NOT NULL,
 description VARCHAR(450) NOT NULL, begin_at DATETIME(3) NOT NULL, end_at DATETIME(3) NULL,
 time_basis ENUM('utc','source_local') NOT NULL DEFAULT 'utc',
 station_count INT NULL, is_sector TINYINT(1) NOT NULL DEFAULT 0,
 battery_seconds DECIMAL(20,3) NULL, generator_seconds DECIMAL(20,3) NULL,
 downtime_seconds DECIMAL(20,3) NULL,
 notified VARCHAR(450) NOT NULL DEFAULT '', notes VARCHAR(450) NOT NULL DEFAULT '',
 source_row_id INT NULL UNIQUE, created_by INT UNSIGNED NOT NULL, version INT NOT NULL DEFAULT 1,
 FOREIGN KEY(direction_id) REFERENCES journal_directions(id),
 FOREIGN KEY(center_id) REFERENCES journal_centers(id),
 FOREIGN KEY(category_id) REFERENCES journal_categories(id),
 FOREIGN KEY(service_element_id) REFERENCES journal_service_elements(id),
 FOREIGN KEY(ticket_id) REFERENCES journal_tickets(id),
 FOREIGN KEY(alarm_type_id) REFERENCES journal_alarm_types(id),
 FOREIGN KEY(source_row_id) REFERENCES journal_import_rows(id),
 FOREIGN KEY(created_by) REFERENCES users(user_id),
 INDEX ix_accident_begin(begin_at), INDEX ix_accident_end(end_at)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
CREATE TABLE IF NOT EXISTS journal_accident_networks (
 accident_id INT NOT NULL, network_type_id INT NOT NULL, PRIMARY KEY(accident_id,network_type_id),
 FOREIGN KEY(accident_id) REFERENCES journal_accidents(id) ON DELETE CASCADE,
 FOREIGN KEY(network_type_id) REFERENCES journal_network_types(id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
CREATE TABLE IF NOT EXISTS journal_accident_equipment (
 accident_id INT NOT NULL, equipment_model_id INT NOT NULL, PRIMARY KEY(accident_id,equipment_model_id),
 FOREIGN KEY(accident_id) REFERENCES journal_accidents(id) ON DELETE CASCADE,
 FOREIGN KEY(equipment_model_id) REFERENCES journal_equipment_models(id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
CREATE OR REPLACE VIEW v_journal_networks AS
 SELECT x.accident_id,
 CASE WHEN COUNT(*)=2 AND SUM(n.code IN('GSM','LTE'))=2 THEN 'LTE/GSM'
 ELSE GROUP_CONCAT(n.code ORDER BY n.code SEPARATOR ' / ') END AS network_label
 FROM journal_accident_networks x JOIN journal_network_types n ON n.id=x.network_type_id GROUP BY x.accident_id;
CREATE OR REPLACE VIEW v_journal_equipment AS
 SELECT x.accident_id,GROUP_CONCAT(m.label ORDER BY m.id SEPARATOR '\n') AS equipment_label
 FROM journal_accident_equipment x JOIN journal_equipment_models m ON m.id=x.equipment_model_id GROUP BY x.accident_id;
CREATE OR REPLACE VIEW journal_report_base AS
 SELECT a.*,COALESCE(t.label,'') PW,d.code NAPR,COALESCE(c.code,'') TEHC,
 k.mapped_category KATEG,k.code category_code,COALESCE(n.network_label,'') NETW_TYPE,s.label SERV_ELEM,
 IF(a.is_sector,-1,a.station_count) NUMB_RBS,COALESCE(e.equipment_label,'') EQIP_TYPE,
 al.code ALARM_TYPE,a.description DESCR,a.begin_at BEGIN_TIME,
 a.notified ALARM_WHO,a.notes DESC_NOTICE,
 ir.batch_id,ir.sheet_name,ir.row_number,ib.time_zone,ish.snapshot_at,
 CASE WHEN a.end_at IS NOT NULL THEN GREATEST(0,TIMESTAMPDIFF(MICROSECOND,a.begin_at,a.end_at)/1000000.0)
 WHEN a.source_row_id IS NULL AND a.time_basis='utc' THEN GREATEST(0,TIMESTAMPDIFF(MICROSECOND,a.begin_at,UTC_TIMESTAMP(3))/1000000.0)
 WHEN ish.snapshot_at IS NOT NULL THEN GREATEST(0,TIMESTAMPDIFF(MICROSECOND,a.begin_at,ish.snapshot_at)/1000000.0)
 ELSE NULL END AS duration_seconds,
 CASE WHEN a.is_sector=0 AND a.station_count IS NOT NULL AND a.downtime_seconds IS NOT NULL
 THEN a.station_count*a.downtime_seconds ELSE NULL END AS total_downtime_seconds
 FROM journal_accidents a
 JOIN journal_directions d ON d.id=a.direction_id LEFT JOIN journal_centers c ON c.id=a.center_id
 JOIN journal_categories k ON k.id=a.category_id JOIN journal_service_elements s ON s.id=a.service_element_id
 LEFT JOIN journal_tickets t ON t.id=a.ticket_id JOIN journal_alarm_types al ON al.id=a.alarm_type_id
 LEFT JOIN v_journal_networks n ON n.accident_id=a.id LEFT JOIN v_journal_equipment e ON e.accident_id=a.id
 LEFT JOIN journal_import_rows ir ON ir.id=a.source_row_id LEFT JOIN journal_import_batches ib ON ib.id=ir.batch_id
 LEFT JOIN journal_import_sheets ish ON ish.batch_id=ir.batch_id AND ish.sheet_name=ir.sheet_name;
-- Представление 19 полей PDF. НВ и неизвестные значения остаются NULL.
-- Длительности в секундах не ограничены диапазоном устаревшего TIME.
CREATE OR REPLACE VIEW journal_report AS
 SELECT b.*,b.end_at END_TIME,b.battery_seconds AKB,b.generator_seconds DGU,
 b.duration_seconds LENGHT,b.downtime_seconds PROSTOY,b.total_downtime_seconds COMMON_PROSTOY
 FROM journal_report_base b;
