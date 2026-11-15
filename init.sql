-- =====================================================================
--  motiv_report — ПОЛНАЯ СХЕМА v2 (01_schema.sql)
--  MariaDB 10.1, InnoDB, utf8mb4, UTC
-- =====================================================================

DROP DATABASE IF EXISTS motiv_report;
CREATE DATABASE motiv_report
  CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;
USE motiv_report;

SET NAMES utf8mb4 COLLATE utf8mb4_unicode_ci;
SET time_zone = '+00:00';

-- =====================================================================
-- ЧАСТЬ 1. ГЕОГРАФИЯ И АДМИНИСТРАТИВНОЕ ДЕЛЕНИЕ (3 таблицы)
-- =====================================================================

-- 1.1. Области (географическая ось)
CREATE TABLE regions (
  region_id     SMALLINT UNSIGNED NOT NULL AUTO_INCREMENT,
  code          VARCHAR(8)        NOT NULL,
  name          VARCHAR(128)      NOT NULL,
  tz_offset_min SMALLINT          NOT NULL DEFAULT 300,
  PRIMARY KEY (region_id),
  UNIQUE KEY uq_regions_code (code)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- 1.2. Направления (операционная макро-ось) — 9 штук
--      Убраны ЯНАО/ХМАО: их роль — через incidents.region_id
CREATE TABLE directions (
  direction_id SMALLINT UNSIGNED NOT NULL AUTO_INCREMENT,
  code         VARCHAR(16)       NOT NULL,
  name         VARCHAR(128)      NOT NULL,
  kind         ENUM('direction','federal_district') NOT NULL DEFAULT 'direction',
  PRIMARY KEY (direction_id),
  UNIQUE KEY uq_directions_code (code)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- 1.3. Техцентры (18 штук, FK на directions)
CREATE TABLE technical_centers (
  tech_center_id SMALLINT UNSIGNED NOT NULL AUTO_INCREMENT,
  direction_id   SMALLINT UNSIGNED NOT NULL,
  code           VARCHAR(16)       NOT NULL,
  name           VARCHAR(128)      NOT NULL,
  PRIMARY KEY (tech_center_id),
  UNIQUE KEY uq_tc_code (code),
  KEY ix_tc_direction (direction_id),
  CONSTRAINT fk_tc_direction FOREIGN KEY (direction_id)
    REFERENCES directions (direction_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- =====================================================================
-- ЧАСТЬ 2. СЕТЕВЫЕ ЭЛЕМЕНТЫ (2 таблицы, БЕЗ vendors)
-- =====================================================================

-- 2.1. Базовая таблица для ВСЕХ сетевых элементов
CREATE TABLE network_elements (
  id                    INT UNSIGNED NOT NULL AUTO_INCREMENT,
  element_type          ENUM(
                          'bts','ip_node','optical_node','dwdm_node',
                          'rrl_node','opts_node','sss_node','client_channel','mgmn_node'
                        ) NOT NULL,
  code                  VARCHAR(64)   NOT NULL,
  network_element_code  VARCHAR(16)   NULL,
  full_name             VARCHAR(450)  NULL,
  short_name            VARCHAR(128)  NULL,
  description           TEXT          NULL,
  region_id             SMALLINT UNSIGNED NULL,
  tech_center_id        SMALLINT UNSIGNED NULL,
  status                ENUM('active','degraded','down','maintenance','decommissioned')
                        NOT NULL DEFAULT 'active',
  status_since          DATETIME(3)   NULL,
  stop_reason           VARCHAR(500)  NULL,
  expected_restore_date DATETIME      NULL,
  actual_restore_date   DATETIME      NULL,
  commissioned_at       DATETIME      NULL,
  decommissioned_at     DATETIME      NULL,
  created_at            DATETIME(3)   NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
  PRIMARY KEY (id),
  UNIQUE KEY uq_ne_code (code),
  UNIQUE KEY uq_ne_network_code (network_element_code),
  KEY ix_ne_type_status (element_type, status),
  KEY ix_ne_region (region_id),
  KEY ix_ne_tech_center (tech_center_id),
  CONSTRAINT fk_ne_region FOREIGN KEY (region_id) REFERENCES regions (region_id),
  CONSTRAINT fk_ne_tech_center FOREIGN KEY (tech_center_id) REFERENCES technical_centers (tech_center_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- 2.2. BTS — дочерняя (shared PK). vendor_id убран.
--      Вендор выводится из equipment_types при необходимости.
CREATE TABLE bts (
  bts_id     INT UNSIGNED      NOT NULL,
  address    VARCHAR(255)      NULL,
  lat        DECIMAL(9,6)      NULL,
  lon        DECIMAL(9,6)      NULL,
  site_type  ENUM('macro','micro','indoor','rural') NOT NULL DEFAULT 'macro',
  PRIMARY KEY (bts_id),
  CONSTRAINT fk_bts_ne FOREIGN KEY (bts_id) REFERENCES network_elements (id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- =====================================================================
-- ЧАСТЬ 3. СОТЫ И МЕТРИКИ (3 таблицы)
-- =====================================================================

CREATE TABLE cells (
  cell_id       INT UNSIGNED      NOT NULL AUTO_INCREMENT,
  bts_id        INT UNSIGNED      NOT NULL,
  local_cell_id SMALLINT UNSIGNED NOT NULL,
  sector        ENUM('A','B','C','D','O') NOT NULL,
  PRIMARY KEY (cell_id),
  UNIQUE KEY uq_cells_bts_local (bts_id, local_cell_id),
  KEY ix_cells_bts (bts_id),
  CONSTRAINT fk_cells_bts FOREIGN KEY (bts_id) REFERENCES bts (bts_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE metric_catalog (
  metric_id   SMALLINT UNSIGNED NOT NULL AUTO_INCREMENT,
  metric_code VARCHAR(64)       NOT NULL,
  metric_name VARCHAR(128)      NOT NULL,
  metric_kind ENUM('counter','volume','duration','gauge') NOT NULL DEFAULT 'counter',
  unit        VARCHAR(16)       NULL,
  PRIMARY KEY (metric_id),
  UNIQUE KEY uq_metric_code (metric_code)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- pm_counters_hourly: PK (cell_id, ts_hour, metric_id). vendor_id убран.
CREATE TABLE pm_counters_hourly (
  cell_id   INT UNSIGNED      NOT NULL,
  ts_hour   DATETIME(3)       NOT NULL,
  metric_id SMALLINT UNSIGNED NOT NULL,
  value     DECIMAL(20,4)     NOT NULL,
  PRIMARY KEY (cell_id, ts_hour, metric_id),
  KEY ix_pm_ts (ts_hour),
  KEY ix_pm_metric_ts (metric_id, ts_hour),
  CONSTRAINT fk_pm_cell   FOREIGN KEY (cell_id)   REFERENCES cells (cell_id),
  CONSTRAINT fk_pm_metric FOREIGN KEY (metric_id) REFERENCES metric_catalog (metric_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- =====================================================================
-- ЧАСТЬ 4. ПРОИЗВОДНЫЕ KPI И АНОМАЛИИ (3 таблицы)
-- =====================================================================

-- kpi_cell_hourly: vendor_id убран, PK (cell_id, ts_hour)
CREATE TABLE kpi_cell_hourly (
  cell_id          INT UNSIGNED    NOT NULL,
  ts_hour          DATETIME(3)     NOT NULL,
  conn_attempt     BIGINT UNSIGNED NOT NULL DEFAULT 0,
  conn_success     BIGINT UNSIGNED NOT NULL DEFAULT 0,
  conn_drop        BIGINT UNSIGNED NOT NULL DEFAULT 0,
  traffic_dl_bytes BIGINT UNSIGNED NOT NULL DEFAULT 0,
  traffic_ul_bytes BIGINT UNSIGNED NOT NULL DEFAULT 0,
  cssr_pct         DECIMAL(7,4)    NULL,
  dcr_pct          DECIMAL(7,4)    NULL,
  computed_at      DATETIME(3)     NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
  PRIMARY KEY (cell_id, ts_hour),
  KEY ix_kpi_ts (ts_hour),
  CONSTRAINT fk_kpi_cell FOREIGN KEY (cell_id) REFERENCES cells (cell_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE anomalies (
  anomaly_id     BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
  detected_at    DATETIME(3)     NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
  cell_id        INT UNSIGNED    NULL,
  ts_from        DATETIME(3)     NOT NULL,
  ts_to          DATETIME(3)     NOT NULL,
  anomaly_type   ENUM('threshold','zscore','missing_data','logical','cross_source') NOT NULL,
  kpi_code       VARCHAR(32)     NULL,
  severity       ENUM('info','warning','critical') NOT NULL DEFAULT 'warning',
  observed_value DECIMAL(20,4)   NULL,
  expected_value DECIMAL(20,4)   NULL,
  score          DECIMAL(10,4)   NULL,
  explained_by_incident BIGINT UNSIGNED NULL,
  status         ENUM('new','acknowledged','explained','resolved','false_positive') NOT NULL DEFAULT 'new',
  dedup_hash     CHAR(64)        NOT NULL,
  PRIMARY KEY (anomaly_id),
  UNIQUE KEY uq_an_dedup (dedup_hash),
  KEY ix_an_cell_time (cell_id, ts_from),
  KEY ix_an_status (status, severity),
  KEY ix_an_type (anomaly_type, ts_from),
  KEY ix_an_incident (explained_by_incident),
  CONSTRAINT fk_an_cell FOREIGN KEY (cell_id) REFERENCES cells (cell_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE kpi_thresholds (
  threshold_id BIGINT UNSIGNED   NOT NULL AUTO_INCREMENT,
  kpi_code     VARCHAR(32)       NOT NULL,
  region_id    SMALLINT UNSIGNED NULL,
  min_value    DECIMAL(12,4)     NULL,
  max_value    DECIMAL(12,4)     NULL,
  direction    ENUM('lower_is_worse','higher_is_worse') NOT NULL DEFAULT 'lower_is_worse',
  severity     ENUM('info','warning','critical') NOT NULL DEFAULT 'warning',
  enabled      TINYINT(1)        NOT NULL DEFAULT 1,
  PRIMARY KEY (threshold_id),
  UNIQUE KEY uq_thr (kpi_code, region_id),
  CONSTRAINT fk_thr_region FOREIGN KEY (region_id) REFERENCES regions (region_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- =====================================================================
-- ЧАСТЬ 5. ИНЦИДЕНТЫ (6 таблиц: 4 справочника + priorities + users)
-- =====================================================================

-- 5.1. Приоритеты (1,2,3,А0,В,B — не TINYINT!)
CREATE TABLE incident_priorities (
  priority_id SMALLINT UNSIGNED NOT NULL AUTO_INCREMENT,
  code        VARCHAR(8)        NOT NULL,
  name        VARCHAR(64)       NOT NULL,
  sort_order  TINYINT           NOT NULL DEFAULT 0,
  PRIMARY KEY (priority_id),
  UNIQUE KEY uq_priority_code (code)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- 5.2. Типы сетей — ТОЛЬКО технологии (без ПР/ЗОК/Обращение клиента)
CREATE TABLE network_types (
  network_type_id SMALLINT UNSIGNED NOT NULL AUTO_INCREMENT,
  code            VARCHAR(64)       NOT NULL,
  name            VARCHAR(128)      NOT NULL,
  PRIMARY KEY (network_type_id),
  UNIQUE KEY uq_netw_code (code)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- 5.3. Типы оборудования (5216 = Ericsson RBS 5216 — vendor уже в названии)
CREATE TABLE equipment_types (
  equipment_type_id SMALLINT UNSIGNED NOT NULL AUTO_INCREMENT,
  code              VARCHAR(64)       NOT NULL,
  name              VARCHAR(128)      NOT NULL,
  PRIMARY KEY (equipment_type_id),
  UNIQUE KEY uq_equip_code (code)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- 5.4. Типы аварий — ТОЛЬКО оборудование (без ПР/ЗОК/Недозвон)
CREATE TABLE alarm_types (
  alarm_type_id SMALLINT UNSIGNED NOT NULL AUTO_INCREMENT,
  code          VARCHAR(64)       NOT NULL,
  name          VARCHAR(128)      NOT NULL,
  PRIMARY KEY (alarm_type_id),
  UNIQUE KEY uq_alarm_code (code)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- 5.5. Пользователи (3 роли)
CREATE TABLE users (
  user_id       INT UNSIGNED NOT NULL AUTO_INCREMENT,
  username      VARCHAR(64)  NOT NULL,
  password_hash VARCHAR(255) NOT NULL,
  role          ENUM('engineer','operator','admin') NOT NULL DEFAULT 'engineer',
  created_at    DATETIME(3)  NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
  PRIMARY KEY (user_id),
  UNIQUE KEY uq_users_username (username)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- 5.6. Инциденты
--      category ENUM расширен: planned / client / external покрывают
--      вынесенные из справочников значения (ПР / Обращение клиента / ЗОК / Недозвон)
CREATE TABLE incidents (
  incident_id          BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,

  -- 19 полей ТЗ
  pw                   VARCHAR(450)  NULL,
  direction_id         SMALLINT UNSIGNED NULL,
  tech_center_id       SMALLINT UNSIGNED NULL,
  priority_id          SMALLINT UNSIGNED NULL,
  network_type_id      SMALLINT UNSIGNED NULL,
  serv_elem            VARCHAR(450)  NULL,
  numb_rbs             VARCHAR(20)   NULL,
  equipment_type_id    SMALLINT UNSIGNED NULL,
  alarm_type_id        SMALLINT UNSIGNED NULL,
  descr                VARCHAR(450)  NULL,
  begin_time           DATETIME      NOT NULL,
  end_time             DATETIME      NULL,
  akb                  TIME          NULL,
  dgu                  TIME          NULL,
  lenght               VARCHAR(100)  NULL,
  prostoy              TIME          NULL,
  common_prostoy       VARCHAR(100)  NULL,
  alarm_who            VARCHAR(450)  NULL,
  desc_notice          TEXT          NULL,

  -- Расширения
  admin_district       VARCHAR(128)  NULL,
  network_element_id   INT UNSIGNED  NULL,
  region_id            SMALLINT UNSIGNED NULL,
  severity             ENUM('low','medium','high','critical') NOT NULL DEFAULT 'medium',
  -- planned: ПР (плановые работы)
  -- client: Обращение клиента
  -- external: ЗОК (зона ответственности клиента), Недозвон
  category             ENUM('power','transport','hardware','software',
                            'planned','client','external','other')
                       NOT NULL DEFAULT 'other',
  created_by_user_id   INT UNSIGNED  NULL,
  created_at           DATETIME(3)   NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
  updated_at           DATETIME(3)   NOT NULL DEFAULT CURRENT_TIMESTAMP(3) ON UPDATE CURRENT_TIMESTAMP(3),

  PRIMARY KEY (incident_id),
  KEY ix_inc_time        (begin_time, end_time),
  KEY ix_inc_direction   (direction_id),
  KEY ix_inc_tech_center (tech_center_id),
  KEY ix_inc_alarm       (alarm_type_id),
  KEY ix_inc_ne          (network_element_id, begin_time),
  KEY ix_inc_region      (region_id),
  KEY ix_inc_category    (category, severity),
  KEY ix_inc_created_by  (created_by_user_id),
  CONSTRAINT fk_inc_direction    FOREIGN KEY (direction_id)       REFERENCES directions (direction_id),
  CONSTRAINT fk_inc_tech_center  FOREIGN KEY (tech_center_id)     REFERENCES technical_centers (tech_center_id),
  CONSTRAINT fk_inc_priority     FOREIGN KEY (priority_id)        REFERENCES incident_priorities (priority_id),
  CONSTRAINT fk_inc_network_type FOREIGN KEY (network_type_id)    REFERENCES network_types (network_type_id),
  CONSTRAINT fk_inc_equipment    FOREIGN KEY (equipment_type_id)  REFERENCES equipment_types (equipment_type_id),
  CONSTRAINT fk_inc_alarm        FOREIGN KEY (alarm_type_id)      REFERENCES alarm_types (alarm_type_id),
  CONSTRAINT fk_inc_ne           FOREIGN KEY (network_element_id) REFERENCES network_elements (id),
  CONSTRAINT fk_inc_region       FOREIGN KEY (region_id)          REFERENCES regions (region_id),
  CONSTRAINT fk_inc_user         FOREIGN KEY (created_by_user_id) REFERENCES users (user_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- Теперь FK anomalies → incidents
ALTER TABLE anomalies
  ADD CONSTRAINT fk_an_incident
    FOREIGN KEY (explained_by_incident) REFERENCES incidents (incident_id);

-- =====================================================================
-- ЧАСТЬ 6. АУДИТ (1 таблица)
-- =====================================================================

CREATE TABLE audit_log (
  audit_id     BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
  ts           DATETIME(3)     NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
  user_id      INT UNSIGNED    NULL,
  username     VARCHAR(64)     NOT NULL,
  role         VARCHAR(16)     NULL,
  action       ENUM(
                 'login','create_user','create_incident','update_incident','delete_incident',
                 'explain_anomaly','update_anomaly_status','export_incidents','export_report',
                 'view_audit_log','view_users','detect_anomalies','detect_availability_anomalies','refresh_kpi'
               ) NOT NULL,
  entity_type  VARCHAR(32)     NULL,
  entity_id    BIGINT UNSIGNED NULL,
  ip_address   VARCHAR(45)     NULL,
  success      TINYINT(1)      NOT NULL DEFAULT 1,
  PRIMARY KEY (audit_id),
  KEY ix_audit_ts     (ts),
  KEY ix_audit_user   (username, ts),
  KEY ix_audit_action (action, ts),
  KEY ix_audit_entity (entity_type, entity_id),
  CONSTRAINT fk_audit_user FOREIGN KEY (user_id) REFERENCES users (user_id) ON DELETE SET NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- =====================================================================
-- ЧАСТЬ 7. ПРЕДСТАВЛЕНИЯ (5 view)
-- =====================================================================

-- 7.1. BTS с общими полями (без vendor)
CREATE OR REPLACE VIEW v_bts_full AS
SELECT
  ne.id AS bts_id,
  ne.code, ne.network_element_code, ne.full_name, ne.short_name,
  ne.region_id, ne.tech_center_id, ne.status, ne.status_since,
  ne.stop_reason, ne.expected_restore_date, ne.actual_restore_date,
  ne.commissioned_at, ne.decommissioned_at,
  b.address, b.lat, b.lon, b.site_type,
  r.code  AS region_code,
  r.name  AS region_name,
  tc.code AS tech_center_code,
  tc.name AS tech_center_name,
  d.code  AS direction_code
FROM network_elements ne
JOIN bts b                    ON b.bts_id         = ne.id
LEFT JOIN regions r           ON r.region_id      = ne.region_id
LEFT JOIN technical_centers tc ON tc.tech_center_id = ne.tech_center_id
LEFT JOIN directions d        ON d.direction_id   = tc.direction_id
WHERE ne.element_type = 'bts';

-- 7.2. Инциденты с человекочитаемыми названиями
CREATE OR REPLACE VIEW v_incidents_full AS
SELECT
  i.incident_id, i.pw,
  d.code  AS direction_code,
  tc.code AS tech_center_code,
  p.code  AS priority_code,
  nt.code AS network_type_code,
  et.code AS equipment_type_code,
  at.code AS alarm_type_code,
  ne.element_type AS network_element_type,
  ne.full_name    AS network_element_name,
  i.serv_elem, i.numb_rbs, i.descr, i.desc_notice,
  i.begin_time, i.end_time, i.akb, i.dgu,
  i.lenght, i.prostoy, i.common_prostoy,
  i.alarm_who, i.admin_district,
  i.region_id, i.severity, i.category,
  i.created_by_user_id, i.created_at, i.updated_at
FROM incidents i
LEFT JOIN directions         d  ON d.direction_id      = i.direction_id
LEFT JOIN technical_centers  tc ON tc.tech_center_id   = i.tech_center_id
LEFT JOIN incident_priorities p ON p.priority_id        = i.priority_id
LEFT JOIN network_types      nt ON nt.network_type_id   = i.network_type_id
LEFT JOIN equipment_types    et ON et.equipment_type_id = i.equipment_type_id
LEFT JOIN alarm_types        at ON at.alarm_type_id     = i.alarm_type_id
LEFT JOIN network_elements   ne ON ne.id                = i.network_element_id;

-- 7.3. Аномалии без объяснения
CREATE OR REPLACE VIEW v_anomalies_open AS
SELECT
  a.anomaly_id, a.detected_at, a.cell_id, a.ts_from, a.ts_to,
  a.anomaly_type, a.kpi_code, a.severity,
  a.observed_value, a.expected_value, a.score, a.status,
  c.bts_id, ne.code AS bts_code, ne.full_name AS bts_full_name,
  ne.region_id, r.code AS region_code
FROM anomalies a
LEFT JOIN cells            c  ON c.cell_id  = a.cell_id
LEFT JOIN network_elements ne ON ne.id      = c.bts_id
LEFT JOIN regions          r  ON r.region_id = ne.region_id
WHERE a.status IN ('new','acknowledged')
  AND a.explained_by_incident IS NULL;

-- 7.4. Сводка по регионам
CREATE OR REPLACE VIEW v_network_snapshot AS
SELECT
  r.region_id, r.code AS region_code, r.name AS region_name,
  COUNT(DISTINCT ne.id)                 AS bts_total,
  SUM(ne.status = 'active')             AS bts_active,
  SUM(ne.status IN ('down','degraded')) AS bts_problem,
  SUM(ne.status = 'maintenance')        AS bts_maintenance,
  SUM(ne.status = 'decommissioned')     AS bts_decommissioned,
  COUNT(DISTINCT c.cell_id)             AS cell_total
FROM regions r
LEFT JOIN network_elements ne ON ne.region_id = r.region_id AND ne.element_type = 'bts'
LEFT JOIN cells c             ON c.bts_id     = ne.id
GROUP BY r.region_id, r.code, r.name;

-- 7.5. Эффективные пороги (регион → дефолт)
CREATE OR REPLACE VIEW v_kpi_thresholds_effective AS
SELECT t.* FROM kpi_thresholds t
WHERE t.enabled = 1 AND t.region_id IS NOT NULL
UNION ALL
SELECT t.* FROM kpi_thresholds t
WHERE t.enabled = 1 AND t.region_id IS NULL
  AND NOT EXISTS (
    SELECT 1 FROM kpi_thresholds t2
    WHERE t2.enabled = 1 AND t2.kpi_code = t.kpi_code AND t2.region_id IS NOT NULL
  );

-- =====================================================================
-- ЧАСТЬ 8. СИД СПРАВОЧНИКОВ
-- =====================================================================

-- 8.1. Области
INSERT INTO regions (code, name, tz_offset_min) VALUES
  ('SVE', 'Свердловская область', 300),
  ('KGN', 'Курганская область',   300),
  ('KHM', 'ХМАО — Югра',          300),
  ('YNA', 'ЯНАО',                 300);

-- 8.2. Направления (9 штук — без ЯНАО/ХМАО)
INSERT INTO directions (code, name, kind) VALUES
  ('СВН',      'Северо-восточное направление',  'direction'),
  ('ЮЗН',      'Юго-западное направление',      'direction'),
  ('ХМЯН',     'ХМАО-ЯНАО',                     'direction'),
  ('Вся сеть', 'Вся сеть',                      'direction'),
  ('ПФО',      'Приволжский федеральный округ', 'federal_district'),
  ('ЦФО',      'Центральный федеральный округ', 'federal_district'),
  ('УрФО',     'Уральский федеральный округ',   'federal_district'),
  ('СФО',      'Сибирский федеральный округ',   'federal_district'),
  ('ЮФО',      'Южный федеральный округ',       'federal_district');

-- 8.3. Техцентры (18 штук)
INSERT INTO technical_centers (direction_id, code, name) VALUES
  ((SELECT direction_id FROM directions WHERE code='ЮЗН'),  'ЕКБ',  'Екатеринбургский центр'),
  ((SELECT direction_id FROM directions WHERE code='СВН'),  'АСБ',  'Асбестовский центр'),
  ((SELECT direction_id FROM directions WHERE code='СВН'),  'ИРБ',  'Ирбитский центр'),
  ((SELECT direction_id FROM directions WHERE code='ЮЗН'),  'КМУ',  'Каменск-Уральский центр'),
  ((SELECT direction_id FROM directions WHERE code='СВН'),  'КРТ',  'Краснотурьинский центр'),
  ((SELECT direction_id FROM directions WHERE code='ЮЗН'),  'КРГ',  'Курганский центр'),
  ((SELECT direction_id FROM directions WHERE code='ХМЯН'), 'НВРТ', 'Нижневартовское отделение'),
  ((SELECT direction_id FROM directions WHERE code='ЮЗН'),  'НСР',  'Нижнесергинский центр'),
  ((SELECT direction_id FROM directions WHERE code='СВН'),  'НТГ',  'Нижнетагильский центр'),
  ((SELECT direction_id FROM directions WHERE code='СВН'),  'НТР',  'Нижнетуринский центр'),
  ((SELECT direction_id FROM directions WHERE code='СВН'),  'НУРЛ', 'Новоуральское отделение'),
  ((SELECT direction_id FROM directions WHERE code='ХМЯН'), 'НУРГ', 'Новоуренгойское отделение'),
  ((SELECT direction_id FROM directions WHERE code='ХМЯН'), 'НБР',  'Ноябрьский центр'),
  ((SELECT direction_id FROM directions WHERE code='ХМЯН'), 'СЛХ',  'Салехардское отделение'),
  ((SELECT direction_id FROM directions WHERE code='ХМЯН'), 'СОВ',  'Советское отделение'),
  ((SELECT direction_id FROM directions WHERE code='ХМЯН'), 'СРГ',  'Сургутский центр'),
  ((SELECT direction_id FROM directions WHERE code='ХМЯН'), 'ХМН',  'Ханты-Мансийское отделение'),
  ((SELECT direction_id FROM directions WHERE code='ХМЯН'), 'ОКТ',  'Октябрьское отделение');

-- 8.4. Приоритеты
INSERT INTO incident_priorities (code, name, sort_order) VALUES
  ('А0', 'Авария 0 (высший приоритет)', 0),
  ('1',  'Категория 1',                 1),
  ('2',  'Категория 2',                 2),
  ('3',  'Категория 3',                 3),
  ('В',  'Внеплановая',                 4),
  ('B',  'Внеплановая (лат. B)',        5);

-- 8.5. Типы сетей — ТОЛЬКО технологии (20 значений)
--      Убраны: Обращение клиента, ПР, ЗОК → incidents.category
INSERT INTO network_types (code, name) VALUES
  ('GSM',     'GSM'),
  ('LTE',     'LTE'),
  ('GSM LTE', 'GSM + LTE'),
  ('DWDM',    'DWDM'),
  ('МСПД',    'Мультисервисная сеть передачи данных'),
  ('РРС',     'Радиорелейная сеть связи'),
  ('ССС',     'Спутниковая сеть связи'),
  ('OPTS',    'Оптическая сеть передачи'),
  ('МгМн',    'Междугородняя/международная'),
  ('КСПД',    'Корпоративная СПД'),
  ('SDH',     'SDH'),
  ('PDH',     'PDH'),
  ('ТМ',      'Телеметрия'),
  ('ЗСС',     'Зоновая сеть связи'),
  ('ОПТС',    'Местная телефонная сеть'),
  ('ВОЛС',    'Волоконно-оптическая сеть связи'),
  ('ЛКСС',    'Линейно-кабельные сооружения связи'),
  ('eNB',     'RBS LTE'),
  ('СПД',     'Сеть передачи данных'),
  ('АСР',     'Автоматизированная система расчётов');

-- 8.6. Типы оборудования (25 значений)
INSERT INTO equipment_types (code, name) VALUES
  ('5216',                     'Ericsson RBS 5216'),
  ('6201',                     'Ericsson RBS 6201'),
  ('6301',                     'Ericsson RBS 6301'),
  ('6601',                     'Ericsson RBS 6601'),
  ('6602',                     'Ericsson RBS 6602'),
  ('6630',                     'Ericsson RBS 6630'),
  ('PBC6200',                  'PBC 6200'),
  ('ELTEK',                    'ELTEK (ИБП)'),
  ('Гранит-4',                 'Гранит-4 (ОПС)'),
  ('ДГУ',                      'Дизель-генератор'),
  ('ТМ',                       'Телеметрия'),
  ('HUAWEI',                   'Huawei (прочее)'),
  ('Ubiquiti GigaBeam LR 60/5','Ubiquiti GigaBeam LR 60/5'),
  ('МИК-РЛ400М',               'МИК-РЛ400М (РРЛ)'),
  ('WinLink',                  'WinLink (РРЛ)'),
  ('WinLink-1000 (2,4 ГГц)',   'WinLink-1000 (2,4 ГГц)'),
  ('FA-IP-20E',                'FA-IP-20E'),
  ('Mediant-2000',             'Mediant-2000'),
  ('CSR BPE',                  'CSR BPE'),
  ('CYAN',                     'Cyan (DWDM)'),
  ('iDirect',                  'iDirect (ССС)'),
  ('SW',                       'Коммутатор'),
  ('UMG890',                   'UMG890'),
  ('ML-TN',                    'ML-TN'),
  ('-',                        'Не указано');

-- 8.7. Типы аварий — ТОЛЬКО оборудование (14 значений)
--      Убраны: ПР, ЗОК, Недозвон → incidents.category
INSERT INTO alarm_types (code, name) VALUES
  ('RBS',  'Базовая станция'),
  ('ЭЛ',   'Электроснабжение'),
  ('ТМ',   'Телеметрия'),
  ('ИБП',  'ИБП'),
  ('ДГУ',  'Дизель-генератор'),
  ('ОПС',  'Охранно-пожарная сигнализация'),
  ('ВОЛС', 'ВОЛС'),
  ('РРЛ',  'РРЛ'),
  ('МСПД', 'МСПД'),
  ('DWDM', 'DWDM'),
  ('ССС',  'ССС'),
  ('ЭПУ',  'ЭПУ'),
  ('МгМн', 'МгМн'),
  ('OPTS', 'OPTS');

-- 8.8. Канонические метрики (5 штук)
INSERT INTO metric_catalog (metric_code, metric_name, metric_kind, unit) VALUES
  ('rrc_conn_attempt', 'Попытки установления RRC-соединения',  'counter','count'),
  ('rrc_conn_success', 'Успешные установления RRC-соединения', 'counter','count'),
  ('rrc_conn_drop',    'Аномальные обрывы RRC-соединения',     'counter','count'),
  ('traffic_dl_bytes', 'Трафик данных DL',                     'volume', 'byte'),
  ('traffic_ul_bytes', 'Трафик данных UL',                     'volume', 'byte');

-- 8.9. Пороги SLA
INSERT INTO kpi_thresholds (kpi_code, region_id, min_value, max_value, direction, severity) VALUES
  ('cssr',         NULL, 95.0, NULL, 'lower_is_worse',  'warning'),
  ('dcr',          NULL, NULL, 3.0,  'higher_is_worse', 'warning'),
  ('availability', NULL, 99.0, NULL, 'lower_is_worse',  'critical');

-- =====================================================================
-- ЧАСТЬ 9. СИД ПОЛЬЗОВАТЕЛЕЙ
-- =====================================================================
-- ⚠️ BCrypt-хэши — ЗАГЛУШКИ. Заменить перед запуском!
--   Сгенерировать: BCrypt.Net.BCrypt.HashPassword("<pwd>", 11)

INSERT INTO users (username, password_hash, role) VALUES
  ('engineer', '$2a$11$rBNElySHdDBBz.Urzlgsk.twaNpSf19Ncqti6b/FlIlp0nkzHtYhW', 'engineer'),
  ('operator', '$2a$11$DfUad7Tzs.5kQIL1R1AgoukuqRl25IprBZh3yxnbjhC4pzSt9vE3m', 'operator'),
  ('admin',    '$2a$11$pBb5Gp4TjzC11ACqwwz/PurV6.rAJ9ph6Z6YGUpTaqZx8yX3R6wTy', 'admin');

  
  
  
-- =====================================================================
--  motiv_report — SEED DATA (02_seed_data.sql)
--  Запускать ПОСЛЕ 01_schema.sql.
--  Имена и коды — из реальной сводки «Мотива» (Сводка_обезличенная.xlsx).
-- =====================================================================
USE motiv_report;
SET NAMES utf8mb4 COLLATE utf8mb4_unicode_ci;
SET time_zone = '+00:00';

SET @now_h := CAST(DATE_FORMAT(UTC_TIMESTAMP(), '%Y-%m-%d %H:00:00') AS DATETIME(3));

-- =====================================================================
-- ЧАСТЬ 1. ПОДГОТОВКА ВРЕМЕННЫХ ТАБЛИЦ
-- =====================================================================

-- 1.1. Параметры БС (20 штук — распределение по 4 регионам)
DROP TEMPORARY TABLE IF EXISTS tmp_bts;
CREATE TEMPORARY TABLE tmp_bts (
  code        VARCHAR(64) PRIMARY KEY,
  ne_code     VARCHAR(16) NOT NULL,
  short_name  VARCHAR(128) NOT NULL,
  region_code VARCHAR(8) NOT NULL,
  tech_center_code VARCHAR(16) NOT NULL,
  address     VARCHAR(255),
  lat         DECIMAL(9,6),
  lon         DECIMAL(9,6),
  site_type   ENUM('macro','micro','indoor','rural') NOT NULL DEFAULT 'macro',
  status      ENUM('active','degraded','down','maintenance','decommissioned') NOT NULL DEFAULT 'active'
);

INSERT INTO tmp_bts VALUES
-- Свердловская (11)
('SVE-EKB-0044', 'УЗ_044', 'Таёжный-044',   'SVE', 'ЕКБ', 'Екатеринбург, ул. Таёжная, 44',        56.8389, 60.6057, 'macro', 'active'),
('SVE-ASB-110',  'УЗ_110', 'Кедровый-110',  'SVE', 'АСБ', 'Асбест, ул. Кедровая, 110',            57.0055, 61.4640, 'macro', 'active'),
('SVE-ASB-053',  'УЗ_053', 'Высокий-053',   'SVE', 'АСБ', 'Асбест, ул. Высокая, 53',             57.0091, 61.4701, 'macro', 'degraded'),
('SVE-ASB-124',  'УЗ_124', 'Приозёрный-124','SVE', 'АСБ', 'Асбест, ул. Приозёрная, 124',         57.0018, 61.4582, 'macro', 'active'),
('SVE-ASB-144',  'УЗ_144', 'Радужный-144',  'SVE', 'АСБ', 'Асбест, ул. Радужная, 144',            57.0072, 61.4665, 'macro', 'active'),
('SVE-ASB-082',  'УЗ_082', 'Заречный-082',  'SVE', 'АСБ', 'Асбест, ул. Заречная, 82',             57.0023, 61.4610, 'macro', 'active'),
('SVE-NTR-091',  'УЗ_091', 'Северный-091',  'SVE', 'НТР', 'Кушва, ул. Северная, 91',              58.2921, 59.7595, 'macro', 'active'),
('SVE-IRB-074',  'УЗ_074', 'Кедровый-074',  'SVE', 'ИРБ', 'Ирбит, ул. Кедровая, 74',              57.6752, 63.0618, 'macro', 'active'),
('SVE-IRB-127',  'УЗ_127', 'Северный-127',  'SVE', 'ИРБ', 'Ирбит, ул. Северная, 127',             57.6701, 63.0502, 'micro', 'active'),
('SVE-KRT-121',  'УЗ_121', 'Береговой-121', 'SVE', 'КРТ', 'Краснотурьинск, ул. Береговая, 121',   59.7695, 60.1862, 'macro', 'active'),
('SVE-KRT-086',  'УЗ_086', 'Сосновый-086',  'SVE', 'КРТ', 'Краснотурьинск, ул. Сосновая, 86',     59.7728, 60.1920, 'macro', 'active'),
-- Курганская (3)
('KGN-KRG-203',  'УЗ_203', 'Янтарный-203',  'KGN', 'КРГ', 'Курган, ул. Янтарная, 203',            55.4408, 65.3388, 'macro', 'active'),
('KGN-KRG-214',  'УЗ_214', 'Приозёрный-214','KGN', 'КРГ', 'Курган, ул. Приозёрная, 214',          55.4425, 65.3295, 'macro', 'active'),
('KGN-KRG-213',  'УЗ_213', 'Степной-213',   'KGN', 'КРГ', 'Курган, ул. Степная, 213',             55.4490, 65.3410, 'macro', 'active'),
-- ХМАО (2)
('KHM-HMN-033',  'УЗ_033', 'Степной-033',   'KHM', 'ХМН', 'Ханты-Мансийск, ул. Степная, 33',      61.0025, 69.0175, 'macro', 'active'),
('KHM-SRG-229',  'УЗ_229', 'Береговой-229', 'KHM', 'СРГ', 'Сургут, ул. Береговая, 229',           61.2518, 73.3965, 'macro', 'active'),
-- ЯНАО (4)
('YNA-SLK-045',  'УЗ_045', 'Восточный-045', 'YNA', 'СЛХ', 'Салехард, ул. Восточная, 45',          66.5320, 66.6015, 'macro', 'active'),
('YNA-NBR-257',  'УЗ_257', 'Янтарный-257',  'YNA', 'НБР', 'Ноябрьск, ул. Янтарная, 257',          63.1975, 75.4470, 'macro', 'active'),
('YNA-SLK-250',  'УЗ_250', 'Приозёрный-250','YNA', 'СЛХ', 'Салехард, ул. Приозёрная, 250',        66.5300, 66.5940, 'macro', 'active'),
('YNA-NUR-251',  'УЗ_251', 'Высокий-251',   'YNA', 'НУРГ','Новый Уренгой, ул. Высокая, 251',     66.0820, 76.6810, 'macro', 'active');

-- 1.2. Параметры не-БС сетевых элементов (5 штук)
DROP TEMPORARY TABLE IF EXISTS tmp_ne;
CREATE TEMPORARY TABLE tmp_ne (
  element_type ENUM('bts','ip_node','optical_node','dwdm_node','rrl_node',
                    'opts_node','sss_node','client_channel','mgmn_node') NOT NULL,
  code         VARCHAR(64) PRIMARY KEY,
  ne_code      VARCHAR(16),
  full_name    VARCHAR(450),
  short_name   VARCHAR(128),
  region_code  VARCHAR(8),  -- NULL = без региона (федеральный)
  tech_center_code VARCHAR(16),
  status       ENUM('active','degraded','down','maintenance','decommissioned') NOT NULL DEFAULT 'active',
  description  TEXT
);

INSERT INTO tmp_ne VALUES
-- Не-БС сетевые элементы (из сводки)
('ip_node',      'NET-UZ-149', 'УЗ_149', 'УЗ_149 — IP-узел «Янтарный-149»',           'Янтарный-149', 'SVE', 'ЕКБ',  'active', 'IP-узел агрегации'),
('optical_node', 'NET-UZ-151', 'УЗ_151', 'УЗ_151 — Оптический узел «Речной-151»',    'Речной-151',   'YNA', 'НУРГ', 'degraded','DWDM-узел'),
('rrl_node',     'NET-UZ-061', 'УЗ_061', 'УЗ_061 — Радиорелейный узел «Речной-061»',  'Речной-061',   'YNA', 'НУРГ', 'active', 'РРЛ Ceragon'),
('sss_node',     'NET-UZ-069', 'УЗ_069', 'УЗ_069 — Узел ССС «Степной-069»',           'Степной-069',  'YNA', 'СЛХ',  'active', 'Спутниковая станция'),
('mgmn_node',    'NET-UZ-152', 'УЗ_152', 'УЗ_152 — Узел МгМн «Таёжный-152»',          'Таёжный-152',  NULL,  NULL,   'active', 'Mediant-2000');

-- 1.3. Добавляем БС в tmp_ne (универсальная таблица)
INSERT INTO tmp_ne (element_type, code, ne_code, full_name, short_name, region_code, tech_center_code, status, description)
SELECT 'bts', code, ne_code,
       CONCAT(ne_code, ' — Базовая станция «', short_name, '»'),
       short_name, region_code, tech_center_code, status,
       CONCAT('БС ', short_name)
FROM tmp_bts;

-- =====================================================================
-- ЧАСТЬ 2. INSERT В network_elements (25 строк)
-- =====================================================================

INSERT INTO network_elements (
  element_type, code, network_element_code, full_name, short_name,
  region_id, tech_center_id, status, description, commissioned_at
)
SELECT
  t.element_type, t.code, t.ne_code, t.full_name, t.short_name,
  r.region_id, tc.tech_center_id, t.status, t.description,
  CASE WHEN t.element_type = 'bts' THEN '2022-01-01 00:00:00' ELSE NULL END
FROM tmp_ne t
LEFT JOIN regions r ON r.code = t.region_code
LEFT JOIN technical_centers tc ON tc.code = t.tech_center_code;

-- Отметим для 3 «проблемных» БС остановки/деградации
UPDATE network_elements
SET status = 'maintenance',
    status_since = TIMESTAMPADD(HOUR, -20, @now_h),
    stop_reason = 'Плановое ТО, сектор А отключён',
    expected_restore_date = TIMESTAMPADD(DAY, 2, @now_h)
WHERE code = 'SVE-ASB-053';

UPDATE network_elements
SET status = 'down',
    status_since = TIMESTAMPADD(HOUR, -6, @now_h),
    stop_reason = 'Авария питания, БС на ДГУ'
WHERE code = 'KGN-KRG-214';

-- =====================================================================
-- ЧАСТЬ 3. INSERT В bts (20 строк, shared PK)
-- =====================================================================

INSERT INTO bts (bts_id, address, lat, lon, site_type)
SELECT ne.id, t.address, t.lat, t.lon, t.site_type
FROM tmp_bts t
JOIN network_elements ne ON ne.code = t.code;

-- =====================================================================
-- ЧАСТЬ 4. INSERT В cells (60 сот — 3 сектора на каждую БС)
-- =====================================================================

INSERT INTO cells (bts_id, local_cell_id, sector)
SELECT b.bts_id, s.local_cell_id, s.sector
FROM bts b
CROSS JOIN (
  SELECT 0 AS local_cell_id, 'A' AS sector
  UNION ALL SELECT 1, 'B'
  UNION ALL SELECT 2, 'C'
) s;

-- =====================================================================
-- ЧАСТЬ 5. INSERT В incidents (20 реальных примеров из сводки)
-- =====================================================================

DROP TEMPORARY TABLE IF EXISTS tmp_incidents;
CREATE TEMPORARY TABLE tmp_incidents (
  pw                   VARCHAR(450),
  ne_code              VARCHAR(64),     -- network_elements.code
  direction_code       VARCHAR(16),
  tech_center_code     VARCHAR(16),
  priority_code        VARCHAR(8),
  network_type_code    VARCHAR(64),
  equipment_type_code  VARCHAR(64),
  alarm_type_code      VARCHAR(64),
  serv_elem            VARCHAR(450),
  numb_rbs             VARCHAR(20),
  descr                VARCHAR(450),
  begin_time           DATETIME,
  end_time             DATETIME,
  akb                  TIME,
  dgu                  TIME,
  lenght               VARCHAR(100),
  prostoy              TIME,
  common_prostoy       VARCHAR(100),
  alarm_who            VARCHAR(450),
  desc_notice          TEXT,
  admin_district       VARCHAR(128),
  region_code          VARCHAR(8),
  severity             ENUM('low','medium','high','critical'),
  category             ENUM('power','transport','hardware','software','planned','client','external','other')
);

INSERT INTO tmp_incidents VALUES
-- 1. NTT-63587 — IP-узел «Янтарный-149» — ВОЛС (из «Основные события»)
('NTT-63587', 'NET-UZ-149', 'ЮЗН', 'ЕКБ', 'А0', 'МСПД', 'CSR BPE', 'ВОЛС',
 'УЗ_149 — IP-узел «Янтарный-149»', NULL, 'Link Down',
 TIMESTAMPADD(HOUR, -48, @now_h), NULL,
 NULL, NULL, '2 days, 00:00:00', '48:00:00', '48:00:00',
 'Отдел МиУСПД; Платонов Д.; Шувалов Д.; Серебряков Р.',
 'Обрыв на расстоянии 4,195 км от Объекта-301. В результате дорожных работ разрушена КК РТК.',
 'г. Екатеринбург', 'SVE', 'critical', 'transport'),

-- 2. NTT-63625 — Оптический узел «Речной-151» — DWDM
('NTT-63625', 'NET-UZ-151', 'ХМЯН', 'НУРГ', 'В', 'DWDM', 'CYAN', 'DWDM',
 'УЗ_151 — Оптический узел «Речной-151»', NULL, 'Link Down',
 TIMESTAMPADD(HOUR, -40, @now_h), NULL,
 NULL, NULL, '1 day, 16:00:00', '40:00:00', '40:00:00',
 'ООО Ямал-Телеком; Отдел SDH; Отдел МиУСПД',
 'Обрыв ВОЛС в зоне ответственности ООО Ямал-Телеком. АВР запланированы на 31.08.',
 'г. Новый Уренгой', 'YNA', 'high', 'transport'),

-- 3. NTT-63587 (второй номер) — БС «Таёжный-044» — ВОЛС блокировка
('NTT-63587-2', 'SVE-EKB-0044', 'ЮЗН', 'ЕКБ', '3', 'GSM LTE', NULL, 'ВОЛС',
 'УЗ_044 — Базовая станция «Таёжный-044»', '7', 'Аварийная блокировка БС по привязке',
 TIMESTAMPADD(HOUR, -48, @now_h), NULL,
 NULL, NULL, '2 days, 00:00:00', '48:00:00', '13 days, 8:11:19',
 'ЦОУ (транспортная сеть); Отдел МиУСПД',
 'Объект-215 — Объект-197, 1802 м. Обрыв на расстоянии 4,195 км. (Серебряков Р.)',
 'г. Екатеринбург', 'SVE', 'critical', 'transport'),

-- 4. NTT-63339 — БС «Восточный-045» — РРЛ
('NTT-63339', 'YNA-SLK-045', 'ХМЯН', 'СЛХ', '1', 'GSM LTE', NULL, 'РРЛ',
 'УЗ_045 — Базовая станция «Восточный-045»', '7', 'Кратковременная блокировка БС',
 TIMESTAMPADD(HOUR, -40, @now_h), TIMESTAMPADD(MINUTE, -2399, @now_h),
 NULL, NULL, '00:01:00', '00:07:00', '00:07:00',
 'Зорин Б.В. (при получении сводки)',
 'Сильный ветер, неблагоприятные погодные условия, деградация сигнала.',
 'г. Салехард', 'YNA', 'medium', 'transport'),

-- 5. NTT-63549 — БС «Высокий-053» — RBS
('NTT-63549', 'SVE-ASB-053', 'СВН', 'АСБ', '3', 'GSM LTE', '5216', 'RBS',
 'УЗ_053 — Базовая станция «Высокий-053»', '1', 'Аппаратная неисправность',
 TIMESTAMPADD(HOUR, -36, @now_h), NULL,
 NULL, NULL, '1 day, 12:00:00', '36:00:00', '36:00:00',
 'Савельеву А.В. (при получении сводки)',
 'Периодически. Дисбаланс по приему (RX Branch Imbalance Fault) — AntennaUnitGroup=1. Сектор A/1. Перезагрузка по питанию RRU. Требуется замена RRU.',
 'г. Асбест', 'SVE', 'high', 'hardware'),

-- 6. NTT-63615 — БС «Заречный-082» — RBS
('NTT-63615', 'SVE-ASB-082', 'СВН', 'АСБ', '3', 'LTE', '5216', 'RBS',
 'УЗ_082 — Базовая станция «Заречный-082»', 'С', 'Блокировка сектора',
 TIMESTAMPADD(HOUR, -20, @now_h), TIMESTAMPADD(HOUR, -8, @now_h),
 NULL, NULL, '12:00:00', '12:00:00', '00:00:00',
 'Данилову В.А. (при получении сводки)',
 'БС LTE. Resource Activation Timeout. После блокировки по питанию. Выполнен рестарт BBN.',
 'г. Асбест', 'SVE', 'medium', 'hardware'),

-- 7. NTT-62067 — БС «Береговой-121» — РРЛ
('NTT-62067', 'SVE-KRT-121', 'СВН', 'КРТ', '3', 'GSM LTE', 'Ubiquiti GigaBeam LR 60/5', 'РРЛ',
 'УЗ_121 — Базовая станция «Береговой-121»', '1', 'Блокировка БС',
 TIMESTAMPADD(HOUR, -44, @now_h), NULL,
 NULL, NULL, '1 day, 20:00:00', '44:00:00', '44:00:00',
 'Горелову А.А. (при получении сводки)',
 'РРЛ "Объект-131", 2201 м. Периодически. Требуется замена радиомодуля.',
 'г. Краснотурьинск', 'SVE', 'high', 'transport'),

-- 8. NTT-63503 — БС «Сосновый-086» — РРЛ
('NTT-63503', 'SVE-KRT-086', 'СВН', 'КРТ', '3', 'GSM LTE', 'WinLink', 'РРЛ',
 'УЗ_086 — Базовая станция «Сосновый-086»', '1', 'Блокировка БС',
 TIMESTAMPADD(HOUR, -28, @now_h), NULL,
 NULL, NULL, '1 day, 4:00:00', '28:00:00', '28:00:00',
 'Горелову А.А. (при получении сводки)',
 'РРЛ "Объект-238", 5834 м. Периодически.',
 'г. Краснотурьинск', 'SVE', 'high', 'transport'),

-- 9. NTT-63469 — БС «Кедровый-074» — РРЛ
('NTT-63469', 'SVE-IRB-074', 'СВН', 'ИРБ', '3', 'GSM LTE', 'FA-IP-20E', 'РРЛ',
 'УЗ_074 — Базовая станция «Кедровый-074»', '1', 'Блокировка БС',
 TIMESTAMPADD(HOUR, -40, @now_h), NULL,
 NULL, NULL, '1 day, 16:00:00', '40:00:00', '40:00:00',
 'Харитонову О.М. (при получении сводки)',
 'РРЛ "Объект-064", 1565 м. Рестарт по питанию BBN.',
 'г. Ирбит', 'SVE', 'high', 'transport'),

-- 10. NTT-56593 — БС «Северный-127» — РРЛ
('NTT-56593', 'SVE-IRB-127', 'СВН', 'ИРБ', '3', 'GSM', 'МИК-РЛ400М', 'РРЛ',
 'УЗ_127 — Базовая станция «Северный-127»', '1', 'Блокировка БС',
 TIMESTAMPADD(HOUR, -30, @now_h), NULL,
 NULL, NULL, '1 day, 6:00:00', '30:00:00', '30:00:00',
 'Харитонову О.М. (при получении сводки)',
 'РРЛ «Объект-036», 9984 м.',
 'г. Ирбит', 'SVE', 'high', 'transport'),

-- 11. NTT-63530 — БС «Приозёрный-250» — RBS
('NTT-63530', 'YNA-SLK-250', 'ХМЯН', 'СЛХ', '3', 'GSM LTE', '6601', 'RBS',
 'УЗ_250 — Базовая станция «Приозёрный-250»', '1', 'Аварийная блокировка БС',
 TIMESTAMPADD(HOUR, -36, @now_h), NULL,
 NULL, NULL, '1 day, 12:00:00', '36:00:00', '36:00:00',
 'Зорин Б.В. (при получении сводки)',
 'Дальняя РРС недоступна…',
 'г. Салехард', 'YNA', 'high', 'hardware'),

-- 12. NTT-63578 — БС «Янтарный-203» — RBS
('NTT-63578', 'KGN-KRG-203', 'ЮЗН', 'КРГ', '2', 'GSM LTE', '6601', 'RBS',
 'УЗ_203 — Базовая станция «Янтарный-203»', '1', 'Внутренняя авария',
 TIMESTAMPADD(HOUR, -20, @now_h), NULL,
 NULL, NULL, '20:00:00', '20:00:00', '20:00:00',
 'Ушаков Е.С. (при получении сводки)',
 'HW Fault RRU-4. Рестарт не помог, нужна замена RRUS12.',
 'г. Курган', 'KGN', 'medium', 'hardware'),

-- 13. NTT-63585 — БС «Приозёрный-214» — РРЛ
('NTT-63585', 'KGN-KRG-214', 'ЮЗН', 'КРГ', '3', 'GSM LTE', 'WinLink-1000 (2,4 ГГц)', 'РРЛ',
 'УЗ_214 — Базовая станция «Приозёрный-214»', '1', 'Аварийная блокировка БС',
 TIMESTAMPADD(HOUR, -22, @now_h), NULL,
 NULL, NULL, '22:00:00', '22:00:00', '22:00:00',
 'Ушаков Е.С. (при получении сводки)',
 'Мокроусово — Чесноково Мокроусовский 7636 м. Недоступен с обеих сторон.',
 'г. Курган', 'KGN', 'high', 'transport'),

-- 14. PW-16915739 — БС «Степной-213» — Плановое
('PW-16915739', 'KGN-KRG-213', 'ЮЗН', 'КРГ', '3', 'GSM LTE', NULL, 'ПР',
 'УЗ_213 — Базовая станция «Степной-213»', '1', 'Плановая остановка БС',
 TIMESTAMPADD(DAY, -2, @now_h), NULL,
 NULL, NULL, '2 days, 00:00:00', '48:00:00', '48:00:00',
 'Уваров Д.Л.',
 'Плановая остановка БС по причине демонтажа транзитной БС.',
 'г. Курган', 'KGN', 'low', 'planned'),

-- 15. NTT-63595 — БС «Кедровый-110» — RBS
('NTT-63595', 'SVE-ASB-110', 'СВН', 'АСБ', '2', 'GSM LTE', '6630', 'RBS',
 'УЗ_110 — Базовая станция «Кедровый-110»', '1', 'Аппаратная неисправность',
 TIMESTAMPADD(HOUR, -32, @now_h), NULL,
 NULL, NULL, '1 day, 8:00:00', '32:00:00', '32:00:00',
 'Савельеву А.В. (при получении сводки)',
 'Превышены пределы КСВ. Service Degraded — FDD=125310-3. RRU-3, Ветка=A.',
 'г. Асбест', 'SVE', 'high', 'hardware'),

-- 16. NTT-63049 — БС «Радужный-144» — RBS
('NTT-63049', 'SVE-ASB-144', 'СВН', 'АСБ', '3', 'GSM LTE', '6630', 'RBS',
 'УЗ_144 — Базовая станция «Радужный-144»', '1', 'Аппаратная неисправность',
 TIMESTAMPADD(DAY, -2, @now_h), NULL,
 NULL, NULL, '2 days, 00:00:00', '48:00:00', '48:00:00',
 'Савельеву А.В. (при получении сводки)',
 'Дисбаланс по приему (RX Branch Imbalance Fault) — AntennaUnitGroup=2. Сектор B/2.',
 'г. Асбест', 'SVE', 'high', 'hardware'),

-- 17. NTT-63484 — БС «Приозёрный-124» — RBS
('NTT-63484', 'SVE-ASB-124', 'СВН', 'АСБ', '1', 'GSM LTE', '6630', 'RBS',
 'УЗ_124 — Базовая станция «Приозёрный-124»', 'С', 'Блокировка сектора',
 TIMESTAMPADD(HOUR, -30, @now_h), NULL,
 NULL, NULL, '1 day, 6:00:00', '30:00:00', '00:00:00',
 'Горелову А.А. (при получении сводки)',
 'Сектор «52» — Link failure. Рестарт не помогает. Требуется выезд на место.',
 'г. Асбест', 'SVE', 'high', 'hardware'),

-- 18. NTT-63635 — Узел МгМн «Таёжный-152» — AIS Alarm
('NTT-63635', 'NET-UZ-152', 'ПФО', NULL, 'В', 'МгМн', 'Mediant-2000', 'МгМн',
 'УЗ_152 — Узел МгМн «Таёжный-152»', NULL, 'AIS Alarm',
 TIMESTAMPADD(HOUR, -14, @now_h), NULL,
 NULL, NULL, '14:00:00', '14:00:00', NULL,
 'ПАО Вымпелком; Отдел коммутации МгМн',
 'Отправлен запрос в ПАО Вымпелком.',
 NULL, NULL, 'high', 'transport'),

-- 19. NTT-63561 — Узел ССС «Степной-069» — ЭПУ
('NTT-63561', 'NET-UZ-069', 'ХМЯН', 'СЛХ', 'В', 'ССС', 'iDirect', 'ССС',
 'УЗ_069 — Узел ССС «Степной-069»', NULL, 'Недоступность оборудования',
 TIMESTAMPADD(DAY, -30, @now_h), NULL,
 NULL, NULL, '30 days, 00:00:00', '720:00:00', '720:00:00',
 'Яшин Р.Х.; Воронов В.В.; Отдел спутниковой связи',
 'Вероятно проблема с ЭП. Назначено на Новоуренгойское отделение.',
 'г. Салехард', 'YNA', 'high', 'hardware'),

-- 20. NTT-63567 — БС «Береговой-229» — ЗОК (зона клиента)
('NTT-63567', 'KHM-SRG-229', 'ХМЯН', 'СРГ', 'В', 'Обращение клиента', 'Оборудование клиента', 'ЗОК',
 'УЗ_229 — Базовая станция «Береговой-229»', '1', 'Недоступность канала',
 TIMESTAMPADD(DAY, -3, @now_h), NULL,
 NULL, NULL, '3 days, 00:00:00', '72:00:00', '72:00:00',
 'ЦОУ',
 'Проблем не обнаружено. Клиент проверяет свою ЗО.',
 'г. Сургут', 'KHM', 'medium', 'external');

INSERT INTO incidents (
  pw, direction_id, tech_center_id, priority_id, network_type_id,
  serv_elem, numb_rbs, equipment_type_id, alarm_type_id, descr,
  begin_time, end_time, akb, dgu, lenght, prostoy, common_prostoy,
  alarm_who, desc_notice, admin_district,
  network_element_id, region_id, severity, category, created_by_user_id
)
SELECT
  t.pw,
  d.direction_id, tc.tech_center_id, p.priority_id, nt.network_type_id,
  t.serv_elem, t.numb_rbs, et.equipment_type_id, at.alarm_type_id, t.descr,
  t.begin_time, t.end_time, t.akb, t.dgu, t.lenght, t.prostoy, t.common_prostoy,
  t.alarm_who, t.desc_notice, t.admin_district,
  ne.id, r.region_id, t.severity, t.category,
  (SELECT user_id FROM users WHERE username='admin' LIMIT 1)
FROM tmp_incidents t
LEFT JOIN directions         d  ON d.code            = t.direction_code
LEFT JOIN technical_centers  tc ON tc.code           = t.tech_center_code
LEFT JOIN incident_priorities p ON p.code            = t.priority_code
LEFT JOIN network_types      nt ON nt.code           = t.network_type_code
LEFT JOIN equipment_types    et ON et.code           = t.equipment_type_code
LEFT JOIN alarm_types        at ON at.code           = t.alarm_type_code
LEFT JOIN network_elements   ne ON ne.code           = t.ne_code
LEFT JOIN regions            r  ON r.code            = t.region_code;

-- =====================================================================
-- ЧАСТЬ 6. ГЕНЕРАЦИЯ ЧАСОВ (192 часа = 8 дней) БЕЗ WITH RECURSIVE
--   CROSS JOIN: 10 × 10 × 2 = 200 номеров, берём первые 192
-- =====================================================================

DROP TEMPORARY TABLE IF EXISTS tmp_hours;
CREATE TEMPORARY TABLE tmp_hours (
  i       INT PRIMARY KEY,
  ts_hour DATETIME(3) NOT NULL,
  hh      TINYINT NOT NULL
);

INSERT INTO tmp_hours (i, ts_hour, hh)
SELECT
  n.n,
  TIMESTAMPADD(HOUR, -n.n, @now_h),
  HOUR(TIMESTAMPADD(HOUR, -n.n, @now_h))
FROM (
  SELECT d1.i + d2.i*10 + d3.i*100 AS n
  FROM
    (SELECT 0 AS i UNION ALL SELECT 1 UNION ALL SELECT 2 UNION ALL SELECT 3 UNION ALL SELECT 4
     UNION ALL SELECT 5 UNION ALL SELECT 6 UNION ALL SELECT 7 UNION ALL SELECT 8 UNION ALL SELECT 9) d1
  CROSS JOIN
    (SELECT 0 AS i UNION ALL SELECT 1 UNION ALL SELECT 2 UNION ALL SELECT 3 UNION ALL SELECT 4
     UNION ALL SELECT 5 UNION ALL SELECT 6 UNION ALL SELECT 7 UNION ALL SELECT 8 UNION ALL SELECT 9) d2
  CROSS JOIN
    (SELECT 0 AS i UNION ALL SELECT 1) d3
) n
WHERE n.n < 192;

-- Метрики
DROP TEMPORARY TABLE IF EXISTS tmp_metrics;
CREATE TEMPORARY TABLE tmp_metrics AS
SELECT metric_id, metric_code FROM metric_catalog;

-- Три «проблемные» соты:
--   @cell_degr  — падение CSSR (26 часов)
--   @cell_spike — всплеск трафика (12 часов)
--   @cell_gap   — пропуск данных (5 часов)

SET @cell_degr  := (SELECT c.cell_id FROM cells c
  JOIN network_elements ne ON ne.id = c.bts_id
  WHERE ne.code='SVE-EKB-0044' AND c.local_cell_id=0);

SET @cell_spike := (SELECT c.cell_id FROM cells c
  JOIN network_elements ne ON ne.id = c.bts_id
  WHERE ne.code='KHM-SRG-229' AND c.local_cell_id=1);

SET @cell_gap   := (SELECT c.cell_id FROM cells c
  JOIN network_elements ne ON ne.id = c.bts_id
  WHERE ne.code='YNA-NUR-251' AND c.local_cell_id=0);

-- =====================================================================
-- ЧАСТЬ 7. ГЕНЕРАЦИЯ PM-СЧЁТЧИКОВ (~57 600 строк)
--   60 сот × 192 часа × 5 метрик = 57 600 (минус 5 часов пропуска = 57 300)
-- =====================================================================

INSERT INTO pm_counters_hourly (cell_id, ts_hour, metric_id, value)
SELECT
  x.cell_id,
  x.ts_hour,
  m.metric_id,
  ROUND(CAST(
    CASE m.metric_code
      WHEN 'rrc_conn_attempt'  THEN x.att
      WHEN 'rrc_conn_success'  THEN x.att * x.succ_rate
      WHEN 'rrc_conn_drop'     THEN x.att * (1 - x.succ_rate) * 0.25
      WHEN 'traffic_dl_bytes'  THEN x.att * 850000 * x.hour_load * x.traffic_mult
      WHEN 'traffic_ul_bytes'  THEN x.att * 850000 * x.hour_load * x.traffic_mult / 9
    END AS DECIMAL(20,4)), 4)
FROM (
  SELECT
    c.cell_id,
    h.i, h.ts_hour, h.hh,
    -- суточный профиль: минимум в 04:00, максимум в 16-20
    0.40 + 0.60 * (0.5 - 0.5 * COS(2 * PI() * (h.hh - 4) / 24)) AS hour_load,
    GREATEST(8, ROUND(
      (40 + (c.cell_id * 7) % 60)
      * (0.40 + 0.60 * (0.5 - 0.5 * COS(2 * PI() * (h.hh - 4) / 24)))
      * (1 + 0.06 * SIN(c.cell_id + h.hh))
      * (0.85 + 0.30 * ((c.cell_id * 37 + h.i * 61) % 100) / 100.0)
    )) AS att,
    0.9905
      - ((c.cell_id % 4) * 0.0012)
      + 0.004 * SIN(h.hh * 1.7 + c.cell_id)
      + 0.006 * (((c.cell_id * 53 + h.i * 29) % 100) / 100.0 - 0.5)
      -- АНОМАЛИЯ A: падение CSSR на @cell_degr за последние 26 часов
      - IF(c.cell_id = @cell_degr AND h.i < 26, 0.075, 0) AS succ_rate,
    -- АНОМАЛИЯ B: x10 трафика на @cell_spike за последние 12 часов
    IF(c.cell_id = @cell_spike AND h.i < 12, 10.0, 1.0) AS traffic_mult
  FROM cells c
  CROSS JOIN tmp_hours h
) x
CROSS JOIN tmp_metrics m
-- АНОМАЛИЯ C: у @cell_gap нет данных за последние 5 часов
WHERE NOT (x.cell_id = @cell_gap AND x.i < 5);


-- =====================================================================
--  motiv_report — ПРОЦЕДУРЫ (03_checks.sql)
--  MariaDB 10.1-совместимые. Без WITH RECURSIVE, без ROW_NUMBER().
--  dedup_hash заполняется через SHA2(CONCAT_WS(...), 256).
--
--  Запускать ПОСЛЕ 01_schema.sql и 02_seed_data.sql.
-- =====================================================================
USE motiv_report;

SET NAMES utf8mb4 COLLATE utf8mb4_unicode_ci;
SET time_zone = '+00:00';

-- Часовые метки — только для демо-вызовов в конце
SET @now_h := CAST(DATE_FORMAT(UTC_TIMESTAMP(), '%Y-%m-%d %H:00:00') AS DATETIME(3));

-- Scratch-таблица для курсора (гарантия работы в 10.1)
DROP TABLE IF EXISTS _sp_tmp_missing;
CREATE TABLE _sp_tmp_missing (
  cell_id INT UNSIGNED NOT NULL,
  ts_hour DATETIME(3)  NOT NULL,
  PRIMARY KEY (cell_id, ts_hour)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

DROP PROCEDURE IF EXISTS sp_refresh_kpi_hourly;
DROP PROCEDURE IF EXISTS sp_detect_anomalies;
DROP PROCEDURE IF EXISTS sp_detect_availability_anomalies;

DELIMITER $$

-- ---------------------------------------------------------------------
-- 1. Пересчёт KPI. Без vendor_id (убран из схемы).
-- ---------------------------------------------------------------------
CREATE PROCEDURE sp_refresh_kpi_hourly(IN p_from DATETIME(3), IN p_to DATETIME(3))
BEGIN
  INSERT INTO kpi_cell_hourly
    (cell_id, ts_hour,
     conn_attempt, conn_success, conn_drop,
     traffic_dl_bytes, traffic_ul_bytes,
     cssr_pct, dcr_pct, computed_at)
  SELECT
    p.cell_id, p.ts_hour,
    SUM(CASE WHEN m.metric_code = 'rrc_conn_attempt'  THEN p.value ELSE 0 END),
    SUM(CASE WHEN m.metric_code = 'rrc_conn_success'  THEN p.value ELSE 0 END),
    SUM(CASE WHEN m.metric_code = 'rrc_conn_drop'     THEN p.value ELSE 0 END),
    SUM(CASE WHEN m.metric_code = 'traffic_dl_bytes'  THEN p.value ELSE 0 END),
    SUM(CASE WHEN m.metric_code = 'traffic_ul_bytes'  THEN p.value ELSE 0 END),
    100 * SUM(CASE WHEN m.metric_code = 'rrc_conn_success' THEN p.value ELSE 0 END)
        / NULLIF(SUM(CASE WHEN m.metric_code = 'rrc_conn_attempt' THEN p.value ELSE 0 END), 0),
    100 * SUM(CASE WHEN m.metric_code = 'rrc_conn_drop' THEN p.value ELSE 0 END)
        / NULLIF(SUM(CASE WHEN m.metric_code = 'rrc_conn_success' THEN p.value ELSE 0 END), 0),
    UTC_TIMESTAMP(3)
  FROM pm_counters_hourly p
  JOIN metric_catalog m ON m.metric_id = p.metric_id
  WHERE p.ts_hour >= p_from AND p.ts_hour < p_to
  GROUP BY p.cell_id, p.ts_hour
  ON DUPLICATE KEY UPDATE
    conn_attempt     = VALUES(conn_attempt),
    conn_success     = VALUES(conn_success),
    conn_drop        = VALUES(conn_drop),
    traffic_dl_bytes = VALUES(traffic_dl_bytes),
    traffic_ul_bytes = VALUES(traffic_ul_bytes),
    cssr_pct         = VALUES(cssr_pct),
    dcr_pct          = VALUES(dcr_pct),
    computed_at      = UTC_TIMESTAMP(3);
END$$

-- ---------------------------------------------------------------------
-- 2. Детектор аномалий. 5 проверок + explain.
--    dedup_hash — SHA2(CONCAT_WS('|', cell_id, ts_from, ts_to, type, kpi)).
-- ---------------------------------------------------------------------
CREATE PROCEDURE sp_detect_anomalies(IN p_hours_back INT)
BEGIN
  DECLARE v_to   DATETIME(3);
  DECLARE v_from DATETIME(3);

  SET v_to   = CAST(DATE_FORMAT(UTC_TIMESTAMP(), '%Y-%m-%d %H:00:00') AS DATETIME(3));
  SET v_from = TIMESTAMPADD(HOUR, -p_hours_back, v_to);

  -- Удаляем только свежие new/explained (работа аналитика не трогается)
  DELETE FROM anomalies
  WHERE ts_from >= v_from
    AND status IN ('new','explained')
    AND anomaly_type IN ('threshold','zscore','missing_data','logical');

  -- 2.1. THRESHOLD CSSR --------------------------------------------
  INSERT IGNORE INTO anomalies
    (cell_id, ts_from, ts_to, anomaly_type, kpi_code, severity,
     observed_value, expected_value, score, dedup_hash)
  SELECT
    k.cell_id, k.ts_hour, TIMESTAMPADD(HOUR, 1, k.ts_hour), 'threshold', 'cssr',
    t.severity, k.cssr_pct, t.min_value, t.min_value - k.cssr_pct,
    SHA2(CONCAT_WS('|',
      COALESCE(k.cell_id, 0),
      k.ts_hour,
      TIMESTAMPADD(HOUR, 1, k.ts_hour),
      'threshold',
      'cssr'
    ), 256)
  FROM kpi_cell_hourly k
  JOIN cells c            ON c.cell_id = k.cell_id
  JOIN network_elements ne ON ne.id    = c.bts_id
  JOIN kpi_thresholds t
       ON t.kpi_code = 'cssr' AND t.enabled = 1
      AND (t.region_id IS NULL OR t.region_id = ne.region_id)
  WHERE k.ts_hour >= v_from
    AND k.cssr_pct IS NOT NULL
    AND k.cssr_pct < t.min_value
    AND t.min_value = (
        SELECT MAX(t2.min_value) FROM kpi_thresholds t2
        WHERE t2.kpi_code = 'cssr' AND t2.enabled = 1
          AND (t2.region_id IS NULL OR t2.region_id = ne.region_id)
          AND k.cssr_pct < t2.min_value
    );

  -- 2.2. THRESHOLD DCR ---------------------------------------------
  INSERT IGNORE INTO anomalies
    (cell_id, ts_from, ts_to, anomaly_type, kpi_code, severity,
     observed_value, expected_value, score, dedup_hash)
  SELECT
    k.cell_id, k.ts_hour, TIMESTAMPADD(HOUR, 1, k.ts_hour), 'threshold', 'dcr',
    t.severity, k.dcr_pct, t.max_value, k.dcr_pct - t.max_value,
    SHA2(CONCAT_WS('|',
      COALESCE(k.cell_id, 0),
      k.ts_hour,
      TIMESTAMPADD(HOUR, 1, k.ts_hour),
      'threshold',
      'dcr'
    ), 256)
  FROM kpi_cell_hourly k
  JOIN cells c            ON c.cell_id = k.cell_id
  JOIN network_elements ne ON ne.id    = c.bts_id
  JOIN kpi_thresholds t
       ON t.kpi_code = 'dcr' AND t.enabled = 1
      AND (t.region_id IS NULL OR t.region_id = ne.region_id)
  WHERE k.ts_hour >= v_from
    AND k.dcr_pct IS NOT NULL
    AND t.max_value IS NOT NULL
    AND k.dcr_pct > t.max_value;

  -- 2.3. ZSCORE -----------------------------------------------------
  INSERT IGNORE INTO anomalies
    (cell_id, ts_from, ts_to, anomaly_type, kpi_code, severity,
     observed_value, expected_value, score, dedup_hash)
  SELECT
    s.cell_id, s.ts_hour, TIMESTAMPADD(HOUR, 1, s.ts_hour), 'zscore', 'traffic_dl',
    IF(ABS(s.z) >= 5, 'critical', 'warning'),
    s.cur_value, s.mu, s.z,
    SHA2(CONCAT_WS('|',
      COALESCE(s.cell_id, 0),
      s.ts_hour,
      TIMESTAMPADD(HOUR, 1, s.ts_hour),
      'zscore',
      'traffic_dl'
    ), 256)
  FROM (
    SELECT
      k.cell_id, k.ts_hour, k.traffic_dl_bytes AS cur_value,
      b.mu, b.sd,
      (k.traffic_dl_bytes - b.mu) / NULLIF(b.sd, 0) AS z
    FROM kpi_cell_hourly k
    JOIN (
      SELECT cell_id, HOUR(ts_hour) AS hh,
             AVG(traffic_dl_bytes) AS mu,
             STDDEV_SAMP(traffic_dl_bytes) AS sd
      FROM kpi_cell_hourly
      WHERE ts_hour >= TIMESTAMPADD(DAY, -7, v_from)
        AND ts_hour <  v_from
        AND traffic_dl_bytes > 0
      GROUP BY cell_id, HOUR(ts_hour)
      HAVING COUNT(*) >= 5
    ) b ON b.cell_id = k.cell_id AND b.hh = HOUR(k.ts_hour)
    WHERE k.ts_hour >= v_from
      AND b.sd > 0
      AND k.traffic_dl_bytes > 0
  ) s
  WHERE ABS(s.z) > 3;

  -- 2.4. MISSING_DATA — курсор (нет ROW_NUMBER в 10.1) -------------
  -- Заполняем scratch-таблицу
  DELETE FROM _sp_tmp_missing;

  INSERT INTO _sp_tmp_missing (cell_id, ts_hour)
  SELECT c.cell_id, TIMESTAMPADD(HOUR, -n.n, v_to) - INTERVAL 1 HOUR
  FROM cells c
  JOIN network_elements ne ON ne.id = c.bts_id
  CROSS JOIN (
    SELECT d1.i + d2.i*10 + d3.i*100 AS n
    FROM
      (SELECT 0 AS i UNION ALL SELECT 1 UNION ALL SELECT 2 UNION ALL SELECT 3 UNION ALL SELECT 4
       UNION ALL SELECT 5 UNION ALL SELECT 6 UNION ALL SELECT 7 UNION ALL SELECT 8 UNION ALL SELECT 9) d1
    CROSS JOIN
      (SELECT 0 AS i UNION ALL SELECT 1 UNION ALL SELECT 2 UNION ALL SELECT 3 UNION ALL SELECT 4
       UNION ALL SELECT 5 UNION ALL SELECT 6 UNION ALL SELECT 7 UNION ALL SELECT 8 UNION ALL SELECT 9) d2
    CROSS JOIN
      (SELECT 0 AS i UNION ALL SELECT 1 UNION ALL SELECT 2 UNION ALL SELECT 3 UNION ALL SELECT 4
       UNION ALL SELECT 5 UNION ALL SELECT 6 UNION ALL SELECT 7 UNION ALL SELECT 8 UNION ALL SELECT 9) d3
  ) n
  LEFT JOIN pm_counters_hourly p
         ON p.cell_id = c.cell_id
        AND p.ts_hour = TIMESTAMPADD(HOUR, -n.n, v_to) - INTERVAL 1 HOUR
  WHERE n.n < p_hours_back
    AND p.cell_id IS NULL
    AND ne.status NOT IN ('decommissioned','maintenance')
    AND TIMESTAMPADD(HOUR, -n.n, v_to) - INTERVAL 1 HOUR >= v_from
    AND EXISTS (
      SELECT 1 FROM pm_counters_hourly ph
      WHERE ph.cell_id = c.cell_id
        AND ph.ts_hour >= TIMESTAMPADD(HOUR, -n.n, v_to) - INTERVAL 1 HOUR - INTERVAL 7 DAY
        AND ph.ts_hour <  TIMESTAMPADD(HOUR, -n.n, v_to) - INTERVAL 1 HOUR
    );

  -- Курсор: группируем последовательные часы в один anomaly
  BEGIN
    DECLARE cur_cell INT;
    DECLARE cur_ts   DATETIME(3);
    DECLARE prev_cell INT DEFAULT NULL;
    DECLARE prev_ts   DATETIME(3) DEFAULT NULL;
    DECLARE grp_from  DATETIME(3) DEFAULT NULL;
    DECLARE done INT DEFAULT 0;
    DECLARE cur CURSOR FOR
      SELECT cell_id, ts_hour FROM _sp_tmp_missing
      ORDER BY cell_id, ts_hour;
    DECLARE CONTINUE HANDLER FOR NOT FOUND SET done = 1;

    OPEN cur;
    read_loop: LOOP
      FETCH cur INTO cur_cell, cur_ts;
      IF done = 1 THEN LEAVE read_loop; END IF;

      IF grp_from IS NULL
         OR prev_cell <> cur_cell
         OR cur_ts <> prev_ts + INTERVAL 1 HOUR THEN
        -- Закрываем предыдущую группу
        IF grp_from IS NOT NULL THEN
          INSERT IGNORE INTO anomalies
            (cell_id, ts_from, ts_to, anomaly_type, kpi_code, severity,
             observed_value, dedup_hash)
          VALUES (
            prev_cell,
            grp_from,
            prev_ts + INTERVAL 1 HOUR,
            'missing_data', NULL, 'warning', 0,
            SHA2(CONCAT_WS('|',
              COALESCE(prev_cell, 0),
              grp_from,
              prev_ts + INTERVAL 1 HOUR,
              'missing_data',
              ''
            ), 256)
          );
        END IF;
        SET grp_from = cur_ts;
      END IF;

      SET prev_cell = cur_cell;
      SET prev_ts   = cur_ts;
    END LOOP;
    CLOSE cur;

    -- Финальная группа
    IF grp_from IS NOT NULL THEN
      INSERT IGNORE INTO anomalies
        (cell_id, ts_from, ts_to, anomaly_type, kpi_code, severity,
         observed_value, dedup_hash)
      VALUES (
        prev_cell,
        grp_from,
        prev_ts + INTERVAL 1 HOUR,
        'missing_data', NULL, 'warning', 0,
        SHA2(CONCAT_WS('|',
          COALESCE(prev_cell, 0),
          grp_from,
          prev_ts + INTERVAL 1 HOUR,
          'missing_data',
          ''
        ), 256)
      );
    END IF;
  END;

  -- 2.5. LOGICAL (success > attempt) -------------------------------
  INSERT IGNORE INTO anomalies
    (cell_id, ts_from, ts_to, anomaly_type, kpi_code, severity,
     observed_value, expected_value, score, dedup_hash)
  SELECT
    k.cell_id, k.ts_hour, TIMESTAMPADD(HOUR, 1, k.ts_hour), 'logical', 'cssr',
    'critical', k.conn_success, k.conn_attempt, k.conn_success - k.conn_attempt,
    SHA2(CONCAT_WS('|',
      COALESCE(k.cell_id, 0),
      k.ts_hour,
      TIMESTAMPADD(HOUR, 1, k.ts_hour),
      'logical',
      'cssr'
    ), 256)
  FROM kpi_cell_hourly k
  WHERE k.ts_hour >= v_from
    AND k.conn_success > k.conn_attempt;

  -- 2.6. EXPLAIN: привязать к incidents ----------------------------
  UPDATE anomalies a
  JOIN cells c ON c.cell_id = a.cell_id
  SET a.explained_by_incident = (
    SELECT MIN(i.incident_id) FROM incidents i
    WHERE i.network_element_id = c.bts_id
      AND i.begin_time <= a.ts_to
      AND (i.end_time IS NULL OR i.end_time >= a.ts_from)
  )
  WHERE a.explained_by_incident IS NULL
    AND a.ts_from >= v_from;

  UPDATE anomalies
  SET status = 'explained'
  WHERE ts_from >= v_from
    AND status = 'new'
    AND explained_by_incident IS NOT NULL
    AND anomaly_type IN ('threshold','zscore');

  SELECT ROW_COUNT() AS processed_rows;
END$$

-- ---------------------------------------------------------------------
-- 3. DETECT AVAILABILITY (traffic-based) — без изменений в логике
-- ---------------------------------------------------------------------
CREATE PROCEDURE sp_detect_availability_anomalies(
    IN p_from DATETIME(3),
    IN p_to   DATETIME(3)
)
BEGIN
  INSERT IGNORE INTO anomalies
    (cell_id, ts_from, ts_to, anomaly_type, kpi_code, severity,
     observed_value, expected_value, score, dedup_hash)
  SELECT
    k.cell_id, p_from, p_to, 'threshold', 'availability',
    t.severity,
    k.availability_pct, t.min_value,
    t.min_value - k.availability_pct,
    SHA2(CONCAT_WS('|',
      COALESCE(k.cell_id, 0),
      p_from, p_to,
      'threshold',
      'availability'
    ), 256)
  FROM (
    SELECT
      cell_id,
      100.0 * SUM(
        CASE WHEN conn_attempt    > 0
              OR traffic_dl_bytes > 0
              OR traffic_ul_bytes > 0
             THEN 1 ELSE 0 END
      ) / COUNT(*) AS availability_pct
    FROM kpi_cell_hourly
    WHERE ts_hour >= p_from AND ts_hour < p_to
    GROUP BY cell_id
  ) k
  JOIN cells c             ON c.cell_id = k.cell_id
  JOIN network_elements ne ON ne.id     = c.bts_id
  JOIN kpi_thresholds t
       ON t.kpi_code = 'availability' AND t.enabled = 1
      AND (t.region_id IS NULL OR t.region_id = ne.region_id)
  WHERE k.availability_pct < t.min_value;

  -- Пометить объяснённые
  UPDATE anomalies a
  JOIN cells c ON c.cell_id = a.cell_id
  SET a.explained_by_incident = (
    SELECT MIN(i.incident_id) FROM incidents i
    WHERE i.network_element_id = c.bts_id
      AND i.begin_time <= a.ts_to
      AND (i.end_time IS NULL OR i.end_time >= a.ts_from)
  )
  WHERE a.kpi_code = 'availability'
    AND a.explained_by_incident IS NULL
    AND a.ts_from = p_from;

  UPDATE anomalies
  SET status = 'explained'
  WHERE kpi_code = 'availability'
    AND ts_from = p_from
    AND status = 'new'
    AND explained_by_incident IS NOT NULL;

  SELECT ROW_COUNT() AS processed_rows;
END$$

DELIMITER ;

-- =====================================================================
-- ЗАПУСК (для демо)
--   sp_refresh_kpi_hourly: последние 192 часа = диапазон сида
--   sp_detect_anomalies: 72 часа — покрывает все 3 «проблемные» соты
-- =====================================================================

CALL sp_refresh_kpi_hourly(
  TIMESTAMPADD(HOUR, -192, @now_h),
  TIMESTAMPADD(HOUR, 1,    @now_h)
);

CALL sp_detect_anomalies(72);

CALL sp_detect_availability_anomalies(
  TIMESTAMPADD(HOUR, -192, @now_h),
  TIMESTAMPADD(HOUR, 1,    @now_h)
);