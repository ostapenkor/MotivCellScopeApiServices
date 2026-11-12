-- =====================================================================
-- ЧАСТЬ 1. СПРАВОЧНИКИ
-- =====================================================================

CREATE TABLE regions (
  region_id     SMALLINT UNSIGNED NOT NULL AUTO_INCREMENT,
  code          VARCHAR(8)        NOT NULL,
  name          VARCHAR(128)      NOT NULL,
  tz_offset_min SMALLINT          NOT NULL DEFAULT 300,
  PRIMARY KEY (region_id),
  UNIQUE KEY uq_regions_code (code)
) ENGINE=InnoDB;

CREATE TABLE vendors (
  vendor_id     SMALLINT UNSIGNED NOT NULL AUTO_INCREMENT,
  code          VARCHAR(32)       NOT NULL,
  name          VARCHAR(128)      NOT NULL,
  rat_supported VARCHAR(64)       NULL,
  PRIMARY KEY (vendor_id),
  UNIQUE KEY uq_vendors_code (code)
) ENGINE=InnoDB;

-- =====================================================================
-- ЧАСТЬ 2. СЕТЬ
-- =====================================================================

CREATE TABLE bts (
  bts_id       INT UNSIGNED      NOT NULL AUTO_INCREMENT,
  region_id    SMALLINT UNSIGNED NOT NULL,
  vendor_id    SMALLINT UNSIGNED NOT NULL,
  code         VARCHAR(64)       NOT NULL,
  address      VARCHAR(255)      NULL,
  lat          DECIMAL(9,6)      NULL,
  lon          DECIMAL(9,6)      NULL,
  site_type    ENUM('macro','micro','indoor','rural') NOT NULL DEFAULT 'macro',
  status       ENUM('active','degraded','down','maintenance','decommissioned')
               NOT NULL DEFAULT 'active',
  status_since DATETIME(3)       NULL,
  PRIMARY KEY (bts_id),
  UNIQUE KEY uq_bts_code (code),
  KEY ix_bts_region (region_id),
  KEY ix_bts_vendor (vendor_id),
  KEY ix_bts_status (status),
  CONSTRAINT fk_bts_region FOREIGN KEY (region_id) REFERENCES regions (region_id),
  CONSTRAINT fk_bts_vendor FOREIGN KEY (vendor_id) REFERENCES vendors (vendor_id)
) ENGINE=InnoDB;

CREATE TABLE cells (
  cell_id       INT UNSIGNED      NOT NULL AUTO_INCREMENT,
  bts_id        INT UNSIGNED      NOT NULL,
  local_cell_id SMALLINT UNSIGNED NOT NULL,
  sector        ENUM('A','B','C','D','O') NOT NULL,
  PRIMARY KEY (cell_id),
  UNIQUE KEY uq_cells_bts_local (bts_id, local_cell_id),
  KEY ix_cells_bts (bts_id),
  CONSTRAINT fk_cells_bts FOREIGN KEY (bts_id) REFERENCES bts (bts_id)
) ENGINE=InnoDB;

-- =====================================================================
-- ЧАСТЬ 3. КАТАЛОГ МЕТРИК (5 штук, без хендоверов)
-- =====================================================================

CREATE TABLE metric_catalog (
  metric_id    SMALLINT UNSIGNED NOT NULL AUTO_INCREMENT,
  metric_code  VARCHAR(64)       NOT NULL,
  metric_name  VARCHAR(128)      NOT NULL,
  metric_kind  ENUM('counter','volume','duration','gauge') NOT NULL DEFAULT 'counter',
  unit         VARCHAR(16)       NULL,
  PRIMARY KEY (metric_id),
  UNIQUE KEY uq_metric_code (metric_code)
) ENGINE=InnoDB;

INSERT INTO metric_catalog (metric_code, metric_name, metric_kind, unit) VALUES
  ('rrc_conn_attempt', 'Попытки установления RRC-соединения',   'counter','count'),
  ('rrc_conn_success', 'Успешные установления RRC-соединения',  'counter','count'),
  ('rrc_conn_drop',    'Аномальные обрывы RRC-соединения',      'counter','count'),
  ('traffic_dl_bytes', 'Трафик данных DL',                       'volume', 'byte'),
  ('traffic_ul_bytes', 'Трафик данных UL',                       'volume', 'byte');

-- =====================================================================
-- ЧАСТЬ 4. СЫРЬЁ (партиционированная таблица, БЕЗ FK)
-- =====================================================================

CREATE TABLE pm_counters_hourly (
  vendor_id SMALLINT UNSIGNED NOT NULL,
  cell_id   INT UNSIGNED      NOT NULL,
  ts_hour   DATETIME(3)       NOT NULL,
  metric_id SMALLINT UNSIGNED NOT NULL,
  value     DECIMAL(20,4)     NOT NULL,
  PRIMARY KEY (vendor_id, cell_id, ts_hour, metric_id),
  KEY ix_pm_ts (ts_hour),
  KEY ix_pm_cell_ts (cell_id, ts_hour),
  KEY ix_pm_metric_ts (metric_id, ts_hour),
  CONSTRAINT fk_pm_vendor FOREIGN KEY (vendor_id) REFERENCES vendors (vendor_id),
  CONSTRAINT fk_pm_cell   FOREIGN KEY (cell_id)   REFERENCES cells (cell_id),
  CONSTRAINT fk_pm_metric FOREIGN KEY (metric_id) REFERENCES metric_catalog (metric_id)
) ENGINE=InnoDB;

-- =====================================================================
-- ЧАСТЬ 5. СОБЫТИЯ
-- =====================================================================

CREATE TABLE incidents (
  incident_id  BIGINT UNSIGNED   NOT NULL AUTO_INCREMENT,
  region_id    SMALLINT UNSIGNED NOT NULL,
  bts_id       INT UNSIGNED      NULL,
  category     ENUM('power','transport','hardware','software','planned','external')
               NOT NULL,
  severity     ENUM('low','medium','high','critical') NOT NULL DEFAULT 'medium',
  started_at   DATETIME(3)       NOT NULL,
  resolved_at  DATETIME(3)       NULL,
  description  VARCHAR(500)      NULL,
  PRIMARY KEY (incident_id),
  KEY ix_inc_time (started_at, resolved_at),
  KEY ix_inc_bts (bts_id, started_at),
  CONSTRAINT fk_inc_region FOREIGN KEY (region_id) REFERENCES regions (region_id),
  CONSTRAINT fk_inc_bts    FOREIGN KEY (bts_id)    REFERENCES bts (bts_id),
  CONSTRAINT chk_inc_period CHECK (resolved_at IS NULL OR resolved_at >= started_at)
) ENGINE=InnoDB;

-- =====================================================================
-- ЧАСТЬ 6. ПРОИЗВОДНЫЕ KPI (без хендоверов)
-- =====================================================================

CREATE TABLE kpi_cell_hourly (
  cell_id          INT UNSIGNED      NOT NULL,
  ts_hour          DATETIME(3)       NOT NULL,
  vendor_id        SMALLINT UNSIGNED NOT NULL,
  conn_attempt     BIGINT UNSIGNED   NOT NULL DEFAULT 0,
  conn_success     BIGINT UNSIGNED   NOT NULL DEFAULT 0,
  conn_drop        BIGINT UNSIGNED   NOT NULL DEFAULT 0,
  traffic_dl_bytes BIGINT UNSIGNED   NOT NULL DEFAULT 0,
  traffic_ul_bytes BIGINT UNSIGNED   NOT NULL DEFAULT 0,
  cssr_pct         DECIMAL(7,4)      NULL,
  dcr_pct          DECIMAL(7,4)      NULL,
  computed_at      DATETIME(3)       NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
  PRIMARY KEY (cell_id, ts_hour),
  KEY ix_kpi_ts (ts_hour),
  KEY ix_kpi_vendor (vendor_id, ts_hour),
  CONSTRAINT fk_kpi_cell   FOREIGN KEY (cell_id)   REFERENCES cells (cell_id),
  CONSTRAINT fk_kpi_vendor FOREIGN KEY (vendor_id) REFERENCES vendors (vendor_id)
) ENGINE=InnoDB;

-- =====================================================================
-- ЧАСТЬ 7. АНОМАЛИИ (без details JSON)
-- =====================================================================

CREATE TABLE anomalies (
  anomaly_id     BIGINT UNSIGNED   NOT NULL AUTO_INCREMENT,
  detected_at    DATETIME(3)       NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
  cell_id        INT UNSIGNED      NULL,
  ts_from        DATETIME(3)       NOT NULL,
  ts_to          DATETIME(3)       NOT NULL,
  anomaly_type   ENUM('threshold','zscore','missing_data','logical','cross_source')
                 NOT NULL,
  kpi_code       VARCHAR(32)       NULL,
  severity       ENUM('info','warning','critical') NOT NULL DEFAULT 'warning',
  observed_value DECIMAL(20,4)     NULL,
  expected_value DECIMAL(20,4)     NULL,
  score          DECIMAL(10,4)     NULL,
  explained_by_incident BIGINT UNSIGNED NULL,
  status         ENUM('new','acknowledged','explained','resolved','false_positive')
                 NOT NULL DEFAULT 'new',
  dedup_hash CHAR(64) AS (
    SHA2(CONCAT_WS('|',
      COALESCE(cell_id, 0),
      ts_from, ts_to,
      anomaly_type,
      COALESCE(kpi_code, '')
    ), 256)
  ) PERSISTENT,
  PRIMARY KEY (anomaly_id),
  UNIQUE KEY uq_an_dedup (dedup_hash),
  KEY ix_an_cell_time (cell_id, ts_from),
  KEY ix_an_status (status, severity),
  KEY ix_an_type (anomaly_type, ts_from),
  CONSTRAINT fk_an_cell FOREIGN KEY (cell_id) REFERENCES cells (cell_id),
  CONSTRAINT fk_an_incident FOREIGN KEY (explained_by_incident)
    REFERENCES incidents (incident_id),
  CONSTRAINT chk_an_period CHECK (ts_to >= ts_from)
) ENGINE=InnoDB;

-- =====================================================================
-- ЧАСТЬ 8. БЕЗОПАСНОСТЬ (без ролей)
-- =====================================================================

CREATE TABLE users (
  user_id       INT UNSIGNED NOT NULL AUTO_INCREMENT,
  username      VARCHAR(64)  NOT NULL,
  password_hash VARCHAR(255) NOT NULL,
  created_at    DATETIME(3)  NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
  PRIMARY KEY (user_id),
  UNIQUE KEY uq_users_username (username)
) ENGINE=InnoDB;


-- =====================================================================
-- ЧАСТЬ 9. ПОРОГИ KPI (3 порога)
-- =====================================================================
-- availability: traffic-based (см. sp_detect_availability_anomalies)
--   available_hour = (conn_attempt>0) OR (traffic_dl>0) OR (traffic_ul>0)
--   availability_pct = 100 * available_hours / COUNT(*)

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
  CONSTRAINT fk_thr_region FOREIGN KEY (region_id) REFERENCES regions (region_id),
  CONSTRAINT chk_thr_bounds CHECK (min_value IS NOT NULL OR max_value IS NOT NULL)
) ENGINE=InnoDB;

INSERT INTO kpi_thresholds (kpi_code, region_id, min_value, max_value, direction, severity) VALUES
  ('cssr',         NULL, 95.0, NULL, 'lower_is_worse',  'warning'),
  ('dcr',          NULL, NULL, 3.0,  'higher_is_worse', 'warning'),
  ('availability', NULL, 99.0, NULL, 'lower_is_worse',  'critical');

-- =====================================================================
-- ЧАСТЬ 10. ПРЕДСТАВЛЕНИЯ
-- =====================================================================

CREATE OR REPLACE VIEW v_anomalies_open AS
SELECT a.anomaly_id, a.detected_at, a.cell_id, a.ts_from, a.ts_to,
       a.anomaly_type, a.kpi_code, a.severity,
       a.observed_value, a.expected_value, a.score,
       a.explained_by_incident, a.status,
       c.bts_id, b.code AS bts_code, b.region_id, r.code AS region_code
FROM anomalies a
LEFT JOIN cells   c ON c.cell_id   = a.cell_id
LEFT JOIN bts     b ON b.bts_id    = c.bts_id
LEFT JOIN regions r ON r.region_id = b.region_id
WHERE a.status IN ('new','acknowledged')
  AND a.explained_by_incident IS NULL;

CREATE OR REPLACE VIEW v_network_snapshot AS
SELECT r.region_id, r.code AS region_code, r.name AS region_name,
       COUNT(DISTINCT b.bts_id)                                  AS bts_total,
       SUM(b.status = 'active')                                  AS bts_active,
       SUM(b.status IN ('down','degraded'))                      AS bts_problem,
       COUNT(DISTINCT c.cell_id)                                 AS cell_total
FROM regions r
LEFT JOIN bts   b ON b.region_id = r.region_id
LEFT JOIN cells c ON c.bts_id    = b.bts_id
GROUP BY r.region_id, r.code, r.name;

CREATE OR REPLACE VIEW v_kpi_thresholds_effective AS
SELECT t.* FROM kpi_thresholds t
JOIN (
  SELECT kpi_code, region_id, MAX(region_id IS NOT NULL) AS has_region
  FROM kpi_thresholds WHERE enabled = 1
  GROUP BY kpi_code, region_id
) pref
  ON pref.kpi_code = t.kpi_code
 AND (pref.region_id = t.region_id OR (pref.has_region = 0 AND t.region_id IS NULL))
WHERE t.enabled = 1;

-- =====================================================================
-- ЧАСТЬ 11. СИД СПРАВОЧНИКОВ + 1 ПОЛЬЗОВАТЕЛЬ
-- =====================================================================

INSERT INTO regions (code, name, tz_offset_min) VALUES
  ('SVE', 'Свердловская область', 300), 
  ('KGN', 'Курганская область', 300), 
  ('KHM', 'Ханты-Мансийский автономный округ — Югра', 300), 
  ('YNA', 'Ямало-Ненецкий автономный округ', 300);

INSERT INTO vendors (code, name, rat_supported) VALUES
  ('ERICSSON', 'Ericsson', 'GSM/LTE'),
  ('HUAWEI',   'Huawei',   'GSM/LTE/NR'),
  ('IRTEYA',   'Иртея',    'LTE');

-- =====================================================================
--  ⚠️  ВНИМАНИЕ: BCrypt-хэш ниже — ЗАГЛУШКА, вход работать НЕ БУДЕТ.
--      Замените перед первым запуском. Сгенерировать:
--
--      dotnet add package BCrypt.Net-Next
--      BCrypt.Net.BCrypt.HashPassword("admin", workFactor: 11)
--
--      Или онлайн: https://bcrypt-generator.com/ (cost=11, prefix=$2a$)
--
--      После замены: логин admin, пароль — тот, что вы захэшировали.
-- =====================================================================

INSERT INTO users (username, password_hash) VALUES
  ('admin', '$2a$11$V8p6Sr1cbEpal0mRCxsCvuUu/gYEN07XWnGM6CSC/DSzefArkfYnO');
  
  
  
-- =====================================================================
-- Тестовые данные для текущей схемы: 20 БС, 60 сот, 192 часа, 5 метрик.
-- Таблицы тарифов, радиопараметров, CDR и истории в этой схеме отсутствуют.
-- Справочники и пороги уже заполнены выше; повторные вставки не нужны.
-- Все метки времени UTC. Скрипт предназначен для новой пустой базы.
-- =====================================================================
SET @now_h := CAST(DATE_FORMAT(UTC_TIMESTAMP(), '%Y-%m-%d %H:00:00') AS DATETIME(3));

INSERT INTO bts (region_id, vendor_id, code, address, lat, lon, site_type, status, status_since) VALUES
  (1, 1, 'SVE-EKB-0001', 'Екатеринбург, ул. Малышева, 51',      56.838011, 60.597465, 'macro', 'active', NULL),
  (1, 1, 'SVE-EKB-0002', 'Екатеринбург, ул. Ленина, 24',        56.835500, 60.612800, 'macro', 'active', NULL),
  (1, 1, 'SVE-EKB-0007', 'Екатеринбург, ул. Радищева, 33',      56.830100, 60.605900, 'macro', 'active', NULL),
  (1, 2, 'SVE-EKB-0011', 'Екатеринбург, ул. Щербакова, 4',      56.800300, 60.630400, 'macro', 'active', NULL),
  (1, 2, 'SVE-EKB-0015', 'Екатеринбург, Академгородок, 12',     56.845000, 60.545000, 'macro', 'active', NULL),
  (1, 3, 'SVE-EKB-0101', 'Екатеринбург, ул. Мира, 19',          56.851000, 60.612000, 'macro', 'maintenance', NULL),
  (1, 1, 'SVE-NTZ-0021', 'Нижний Тагил, пр. Мира, 40',          57.910000, 59.980000, 'macro', 'active', NULL),
  (1, 1, 'SVE-NTZ-0022', 'Нижний Тагил, ул. Победы, 8',         57.905000, 59.960000, 'macro', 'degraded', NULL),
  (2, 1, 'KGN-KRG-0001', 'Курган, ул. Гоголя, 55',              55.440000, 65.340000, 'macro', 'active', NULL),
  (2, 1, 'KGN-KRG-0002', 'Курган, пр. Машиностроителей, 15',    55.428000, 65.310000, 'macro', 'active', NULL),
  (2, 2, 'KGN-KRG-0005', 'Курган, ул. Коли Мяготина, 100',      55.435000, 65.290000, 'micro', 'active', NULL),
  (2, 1, 'KGN-SHF-0010', 'Шадринск, ул. Свердлова, 3',          56.088000, 63.635000, 'rural', 'active', NULL),
  (3, 1, 'KHM-SRG-0001', 'Сургут, ул. Энергетиков, 12',         61.254000, 73.396000, 'macro', 'active', NULL),
  (3, 1, 'KHM-SRG-0002', 'Сургут, пр. Ленина, 44',              61.246000, 73.410000, 'macro', 'active', NULL),
  (3, 2, 'KHM-SRG-0003', 'Сургут, ул. Профсоюзов, 61',          61.262000, 73.428000, 'macro', 'active', NULL),
  (3, 1, 'KHM-NVK-0008', 'Нефтеюганск, Аура, Нефтеюганское ш. 1',61.099000, 72.603000, 'macro', 'active', NULL),
  (4, 1, 'YNA-NUR-0001', 'Новый Уренгой, ул. Мира, 5',          66.083000, 76.680000, 'macro', 'active', NULL),
  (4, 1, 'YNA-NUR-0002', 'Новый Уренгой, ул. Индустриальная, 21',66.076000, 76.700000, 'macro', 'active', NULL),
  (4, 2, 'YNA-NOY-0004', 'Ноябрьск, ул. Республики, 30',        63.199000, 75.450000, 'macro', 'active', NULL),
  (4, 1, 'YNA-SLK-0006', 'Салехард, ул. Чубынина, 14',          66.529000, 66.613000, 'rural', 'active', NULL);

-- ---------------------------------------------------------------------
-- 5. СОТЫ: 3 сектора на каждую БС (60 сот)
-- ---------------------------------------------------------------------
INSERT INTO cells (bts_id, local_cell_id, sector)
SELECT b.bts_id, s.local_cell_id, s.sector
FROM bts b
CROSS JOIN (SELECT 0 AS local_cell_id, 'A' AS sector
            UNION ALL SELECT 1, 'B'
            UNION ALL SELECT 2, 'C') s;

UPDATE bts b SET b.status = 'maintenance', b.status_since = TIMESTAMPADD(HOUR, -20, @now_h) WHERE b.code = 'SVE-EKB-0101';
UPDATE bts b SET b.status = 'active',      b.status_since = TIMESTAMPADD(HOUR, -2,  @now_h) WHERE b.code = 'KGN-KRG-0002';
UPDATE bts b SET b.status = 'degraded',    b.status_since = TIMESTAMPADD(HOUR, -30, @now_h) WHERE b.code = 'SVE-NTZ-0022';

INSERT INTO incidents (region_id, bts_id, category, severity, started_at, resolved_at, description)
SELECT b.region_id, b.bts_id, 'transport', 'high',
       TIMESTAMPADD(HOUR, -26, @now_h), NULL,
       'Деградация транспортного канала: рост потерь пакетов, следствие — падение CSSR'
FROM bts b WHERE b.code = 'SVE-EKB-0007';

INSERT INTO incidents (region_id, bts_id, category, severity, started_at, resolved_at, description)
SELECT b.region_id, b.bts_id, 'power', 'critical',
       TIMESTAMPADD(HOUR, -8, @now_h), TIMESTAMPADD(HOUR, -2, @now_h),
       'Отключение электроэнергии, БС на ДГУ, часть секторов недоступна'
FROM bts b WHERE b.code = 'KGN-KRG-0002';

INSERT INTO incidents (region_id, bts_id, category, severity, started_at, resolved_at, description)
SELECT b.region_id, b.bts_id, 'hardware', 'medium',
       TIMESTAMPADD(HOUR, -30, @now_h), NULL,
       'Деградация РРЛ, снижение ёмкости до 40%'
FROM bts b WHERE b.code = 'SVE-NTZ-0022';

CREATE TEMPORARY TABLE tmp_hours (i INT PRIMARY KEY, ts_hour DATETIME(3), hh TINYINT);
INSERT INTO tmp_hours (i, ts_hour, hh)
WITH RECURSIVE seq(i) AS (SELECT 0 UNION ALL SELECT i + 1 FROM seq WHERE i < 191)
SELECT s.i, TIMESTAMPADD(HOUR, -s.i, @now_h), HOUR(TIMESTAMPADD(HOUR, -s.i, @now_h)) FROM seq s;

CREATE TEMPORARY TABLE tmp_metrics (metric_id SMALLINT UNSIGNED PRIMARY KEY);
INSERT INTO tmp_metrics SELECT metric_id FROM metric_catalog;

-- «Проблемные» соты (для зашитых аномалий)
SET @cell_degr := (SELECT c.cell_id FROM cells c JOIN bts b ON b.bts_id=c.bts_id
                   WHERE b.code='SVE-EKB-0007' AND c.local_cell_id=1);
SET @cell_spike := (SELECT c.cell_id FROM cells c JOIN bts b ON b.bts_id=c.bts_id
                    WHERE b.code='KHM-SRG-0003' AND c.local_cell_id=2);
SET @cell_gap   := (SELECT c.cell_id FROM cells c JOIN bts b ON b.bts_id=c.bts_id
                    WHERE b.code='YNA-NUR-0002' AND c.local_cell_id=0);

-- Расчётная база: нагрузка по часам суток + «шум» + зашитые аномалии
INSERT INTO pm_counters_hourly (vendor_id, cell_id, ts_hour, metric_id, value)
SELECT
  x.vendor_id,
  x.cell_id,
  x.ts_hour,
  m.metric_id,
  ROUND(CAST(
    CASE m.metric_id
      WHEN 1 THEN x.att
      WHEN 2 THEN x.att * x.succ_rate
      WHEN 3 THEN x.att * (1 - x.succ_rate) * 0.25
      WHEN 4 THEN x.att * 850000 * x.hour_load * x.traffic_mult
      WHEN 5 THEN x.att * 850000 * x.hour_load * x.traffic_mult / 9
    END AS DECIMAL(20,4)), 4)
FROM (
  SELECT
    c.cell_id,
    b.vendor_id,
    h.i, h.ts_hour, h.hh,
    -- суточный профиль: минимум в 04:00, максимум около 16-20
    0.40 + 0.60 * (0.5 - 0.5 * COS(2 * PI() * (h.hh - 4) / 24))                       AS hour_load,
    -- базовая интенсивность на соту (детерминированно зависит от cell_id)
    GREATEST(8, ROUND((40 + (c.cell_id * 7) % 60)
             * (0.40 + 0.60 * (0.5 - 0.5 * COS(2 * PI() * (h.hh - 4) / 24)))
             * (1 + 0.06 * SIN(c.cell_id + h.hh))
             -- день ото дня нагрузка «дышит»: без этого разброс по дням = 0
             * (0.85 + 0.30 * ((c.cell_id * 37 + h.i * 61) % 100) / 100.0)))             AS att,
    -- базовая доля успеха ~97.5–98.5%
    0.9905 - ((c.cell_id % 4) * 0.0012)
      -- небольшое суточное «дыхание» качества, чтобы ряд был не константой
      + 0.004 * SIN(h.hh * 1.7 + c.cell_id)
      -- суточный разброс доли успеха (для z-score нужен sd > 0)
      + 0.006 * (((c.cell_id * 53 + h.i * 29) % 100) / 100.0 - 0.5)
      -- АНОМАЛИЯ A: падение CSSR и рост обрывов на @cell_degr за последние 26 часов
      - IF(c.cell_id = @cell_degr AND h.i < 26, 0.075, 0)
      -- АНОМАЛИЯ B: всплеск трафика на @cell_spike (без аварии)
      AS succ_rate,
    -- АНОМАЛИЯ B: множитель трафика x6 на @cell_spike за последние 12 часов
    IF(c.cell_id = @cell_spike AND h.i < 12, 10.0, 1.0)                                  AS traffic_mult
  FROM cells c
  JOIN bts b ON b.bts_id = c.bts_id
  CROSS JOIN tmp_hours h
) x
CROSS JOIN tmp_metrics m
-- АНОМАЛИЯ C: у @cell_gap нет данных за последние 5 часов (пропуск)
WHERE NOT (x.cell_id = @cell_gap AND x.i < 5);


DROP TEMPORARY TABLE tmp_hours, tmp_metrics;

-- =====================================================================
--  motiv_report — ПРОЦЕДУРЫ (03_checks.sql)
--  Запускать ПОСЛЕ 01_schema.sql и 02_seed.sql.
--
--  Три процедуры:
--    sp_refresh_kpi_hourly(from, to)             — 5 метрик → 2 KPI
--    sp_detect_anomalies(hours_back)             — threshold CSSR/DCR, zscore,
--                                                   missing_data, logical + explained
--    sp_detect_availability_anomalies(from, to)  — traffic-based availability
--
--  Все идемпотентны (INSERT IGNORE + dedup_hash).
--  Никаких хендоверов, work_plans, session_data, details JSON.
--  Пороги: kpi_thresholds (без valid_to, без vendor_id — упрощены в MVP).
-- =====================================================================


DROP PROCEDURE IF EXISTS sp_refresh_kpi_hourly;
DROP PROCEDURE IF EXISTS sp_detect_anomalies;
DROP PROCEDURE IF EXISTS sp_detect_availability_anomalies;

-- ---------------------------------------------------------------------
-- 1. Пересчёт KPI за окно [p_from; p_to].
--    Единственное отличие от старой версии — JOIN metric_catalog
--    вместо хардкода metric_id. И без ho_*, hosr_pct.
-- ---------------------------------------------------------------------
DELIMITER $$
CREATE PROCEDURE sp_refresh_kpi_hourly(IN p_from DATETIME(3), IN p_to DATETIME(3))
BEGIN
  INSERT INTO kpi_cell_hourly
    (cell_id, ts_hour, vendor_id,
     conn_attempt, conn_success, conn_drop,
     traffic_dl_bytes, traffic_ul_bytes,
     cssr_pct, dcr_pct, computed_at)
  SELECT
    p.cell_id, p.ts_hour, p.vendor_id,
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
  GROUP BY p.cell_id, p.ts_hour, p.vendor_id
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
-- 2. Автопоиск аномалий за последние p_hours_back часов.
--    Проверки:
--      2.1. threshold CSSR (CSSR < min_value)
--      2.2. threshold DCR  (DCR > max_value)
--      2.3. zscore         (|z| > 3 по часу суток, 7-дневная история)
--      2.4. missing_data   (группирует серии, только для сот с историей)
--      2.5. logical        (conn_success > conn_attempt)
--      2.6. explain        (привязка к incidents → status='explained')
--
--    INSERT IGNORE — не падаем при повторе (dedup_hash UNIQUE).
--    Статусы acknowledged/resolved/false_positive не трогаем —
--    они уже «заняли» dedup_hash, IGNORE их пропустит.
-- ---------------------------------------------------------------------
CREATE PROCEDURE sp_detect_anomalies(IN p_hours_back INT)
BEGIN
  DECLARE v_to   DATETIME(3);
  DECLARE v_from DATETIME(3);

  SET v_to   = CAST(DATE_FORMAT(UTC_TIMESTAMP(), '%Y-%m-%d %H:00:00') AS DATETIME(3));
  SET v_from = TIMESTAMPADD(HOUR, -p_hours_back, v_to);

  -- Удаляем только свежие new/explained за окно.
  -- acknowledged/resolved/false_positive не трогаем — это работа аналитика.
  DELETE FROM anomalies
  WHERE ts_from >= v_from 
    AND status IN ('new','explained')
    AND anomaly_type IN ('threshold','zscore','missing_data','logical');

  -- 2.1. THRESHOLD CSSR (берём самый строгий подходящий порог) ------
  INSERT IGNORE INTO anomalies 
    (cell_id, ts_from, ts_to, anomaly_type, kpi_code, severity,
     observed_value, expected_value, score)
  SELECT k.cell_id, k.ts_hour, TIMESTAMPADD(HOUR, 1, k.ts_hour), 'threshold',
         'cssr', t.severity, k.cssr_pct, t.min_value, t.min_value - k.cssr_pct
  FROM kpi_cell_hourly k
  JOIN cells c ON c.cell_id = k.cell_id
  JOIN bts   b ON b.bts_id  = c.bts_id
  JOIN kpi_thresholds t
       ON t.kpi_code = 'cssr' AND t.enabled = 1
      AND (t.region_id IS NULL OR t.region_id = b.region_id)
  WHERE k.ts_hour >= v_from 
    AND k.cssr_pct IS NOT NULL 
    AND k.cssr_pct < t.min_value
    AND t.min_value = (
        SELECT MAX(t2.min_value) FROM kpi_thresholds t2
        WHERE t2.kpi_code = 'cssr' AND t2.enabled = 1
          AND (t2.region_id IS NULL OR t2.region_id = b.region_id)
          AND k.cssr_pct < t2.min_value
    );

  -- 2.2. THRESHOLD DCR ---------------------------------------------
  INSERT IGNORE INTO anomalies 
    (cell_id, ts_from, ts_to, anomaly_type, kpi_code, severity,
     observed_value, expected_value, score)
  SELECT k.cell_id, k.ts_hour, TIMESTAMPADD(HOUR, 1, k.ts_hour), 'threshold',
         'dcr', t.severity, k.dcr_pct, t.max_value, k.dcr_pct - t.max_value
  FROM kpi_cell_hourly k
  JOIN cells c ON c.cell_id = k.cell_id
  JOIN bts   b ON b.bts_id  = c.bts_id
  JOIN kpi_thresholds t
       ON t.kpi_code = 'dcr' AND t.enabled = 1
      AND (t.region_id IS NULL OR t.region_id = b.region_id)
  WHERE k.ts_hour >= v_from 
    AND k.dcr_pct IS NOT NULL 
    AND t.max_value IS NOT NULL 
    AND k.dcr_pct > t.max_value;

  -- 2.3. ZSCORE (по часу суток, 7-дневная история) -----------------
  INSERT IGNORE INTO anomalies 
    (cell_id, ts_from, ts_to, anomaly_type, kpi_code, severity,
     observed_value, expected_value, score)
  SELECT s.cell_id, s.ts_hour, TIMESTAMPADD(HOUR, 1, s.ts_hour), 'zscore', 'traffic_dl',
         IF(ABS(s.z) >= 5, 'critical', 'warning'),
         s.cur_value, s.mu, s.z
  FROM (
    SELECT k.cell_id, k.ts_hour, k.traffic_dl_bytes AS cur_value,
           b.mu, b.sd,
           (k.traffic_dl_bytes - b.mu) / NULLIF(b.sd, 0) AS z
    FROM kpi_cell_hourly k
    JOIN (
      SELECT cell_id, HOUR(ts_hour) AS hh, 
             AVG(traffic_dl_bytes) AS mu,
             STDDEV_SAMP(traffic_dl_bytes) AS sd
      FROM kpi_cell_hourly
      WHERE ts_hour >= TIMESTAMPADD(DAY, -7, v_from) 
        AND ts_hour < v_from
        AND traffic_dl_bytes > 0
      GROUP BY cell_id, HOUR(ts_hour)
      HAVING COUNT(*) >= 5
    ) b ON b.cell_id = k.cell_id AND b.hh = HOUR(k.ts_hour)
    WHERE k.ts_hour >= v_from 
      AND b.sd > 0
      AND k.traffic_dl_bytes > 0
  ) s
  WHERE ABS(s.z) > 3;

  -- 2.4. MISSING_DATA (с группировкой серий и защитой от «новых сот»)
  INSERT IGNORE INTO anomalies 
    (cell_id, ts_from, ts_to, anomaly_type, kpi_code, severity,
     observed_value)
  WITH RECURSIVE hours(i) AS (
    SELECT 0 UNION ALL SELECT i + 1 FROM hours WHERE i < p_hours_back - 1
  ),
  grid AS (
    SELECT TIMESTAMPADD(HOUR, -i, v_to) - INTERVAL 1 HOUR AS ts_hour FROM hours
  ),
  cell_hours AS (
    SELECT c.cell_id, g.ts_hour
    FROM cells c
    JOIN bts b ON b.bts_id = c.bts_id
    CROSS JOIN grid g
    WHERE b.status NOT IN ('decommissioned','maintenance')
  ),
  missing AS (
    SELECT ch.cell_id, ch.ts_hour
    FROM cell_hours ch
    LEFT JOIN pm_counters_hourly p 
           ON p.cell_id = ch.cell_id AND p.ts_hour = ch.ts_hour
    WHERE p.cell_id IS NULL
      -- защита от «новых сот»: были данные за 7 дней до этого часа
      AND EXISTS (
        SELECT 1 FROM pm_counters_hourly ph
        WHERE ph.cell_id = ch.cell_id
          AND ph.ts_hour >= ch.ts_hour - INTERVAL 7 DAY
          AND ph.ts_hour <  ch.ts_hour
      )
  ),
  grouped AS (
    SELECT cell_id, ts_hour,
           ts_hour - INTERVAL ROW_NUMBER() OVER (
             PARTITION BY cell_id ORDER BY ts_hour
           ) HOUR AS grp
    FROM missing
  )
  SELECT cell_id, MIN(ts_hour), MAX(ts_hour) + INTERVAL 1 HOUR,
         'missing_data', NULL, 'warning', 0
  FROM grouped
  GROUP BY cell_id, grp;

  -- 2.5. LOGICAL (success > attempt) -------------------------------
  INSERT IGNORE INTO anomalies 
    (cell_id, ts_from, ts_to, anomaly_type, kpi_code, severity,
     observed_value, expected_value, score)
  SELECT k.cell_id, k.ts_hour, TIMESTAMPADD(HOUR, 1, k.ts_hour), 'logical', 'cssr',
         'critical', k.conn_success, k.conn_attempt, 
         k.conn_success - k.conn_attempt
  FROM kpi_cell_hourly k
  WHERE k.ts_hour >= v_from 
    AND k.conn_success > k.conn_attempt;

  -- 2.6. EXPLAIN: привязать к incidents ----------------------------
  UPDATE anomalies a
  JOIN cells c ON c.cell_id = a.cell_id
  JOIN bts   b ON b.bts_id  = c.bts_id
  SET a.explained_by_incident = (
    SELECT MIN(i.incident_id) FROM incidents i
    WHERE i.bts_id = b.bts_id
      AND i.started_at <= a.ts_to
      AND (i.resolved_at IS NULL OR i.resolved_at >= a.ts_from)
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
-- 3. DETECT AVAILABILITY (traffic-based)
--    Формула:
--      available_hour = (conn_attempt>0 OR traffic_dl>0 OR traffic_ul>0)
--      availability_pct = 100 * available_hours / COUNT(*)
--
--    Считается за ПРОИЗВОЛЬНЫЙ период [p_from, p_to] — это агрегат,
--    не почасовое значение. Порог: kpi_thresholds (availability, 99%).
-- ---------------------------------------------------------------------
CREATE PROCEDURE sp_detect_availability_anomalies(
    IN p_from DATETIME(3), 
    IN p_to   DATETIME(3)
)
BEGIN
  INSERT IGNORE INTO anomalies 
    (cell_id, ts_from, ts_to, anomaly_type, kpi_code, severity,
     observed_value, expected_value, score)
  SELECT 
    k.cell_id, p_from, p_to, 'threshold', 'availability',
    t.severity,
    k.availability_pct, t.min_value,
    t.min_value - k.availability_pct
  FROM (
    SELECT 
      cell_id,
      100.0 * SUM(
        CASE WHEN conn_attempt     > 0 
              OR traffic_dl_bytes  > 0 
              OR traffic_ul_bytes  > 0 
             THEN 1 ELSE 0 END
      ) / COUNT(*) AS availability_pct
    FROM kpi_cell_hourly
    WHERE ts_hour >= p_from AND ts_hour < p_to
    GROUP BY cell_id
  ) k
  JOIN cells c ON c.cell_id = k.cell_id
  JOIN bts   b ON b.bts_id  = c.bts_id
  JOIN kpi_thresholds t
       ON t.kpi_code = 'availability' AND t.enabled = 1
      AND (t.region_id IS NULL OR t.region_id = b.region_id)
  WHERE k.availability_pct < t.min_value;

  -- Пометить объяснённые инцидентами
  UPDATE anomalies a
  JOIN cells c ON c.cell_id = a.cell_id
  JOIN bts   b ON b.bts_id  = c.bts_id
  SET a.explained_by_incident = (
    SELECT MIN(i.incident_id) FROM incidents i
    WHERE i.bts_id = b.bts_id
      AND i.started_at <= a.ts_to
      AND (i.resolved_at IS NULL OR i.resolved_at >= a.ts_from)
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



-- Первоначальный расчёт после загрузки сырья и создания процедур.
CALL sp_refresh_kpi_hourly(TIMESTAMPADD(HOUR, -192, @now_h), TIMESTAMPADD(HOUR, 1, @now_h));
CALL sp_detect_anomalies(72);
CALL sp_detect_availability_anomalies(TIMESTAMPADD(HOUR, -192, @now_h), TIMESTAMPADD(HOUR, 1, @now_h));
