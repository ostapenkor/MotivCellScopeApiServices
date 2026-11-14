using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using Pomelo.EntityFrameworkCore.MySql.Scaffolding.Internal;

namespace MotivCellScopeApiServices.Models;

public partial class MotivReportDbContext : DbContext
{
    public MotivReportDbContext()
    {
    }

    public MotivReportDbContext(DbContextOptions<MotivReportDbContext> options)
        : base(options)
    {
    }

    public virtual DbSet<AlarmType> AlarmTypes { get; set; }

    public virtual DbSet<Anomaly> Anomalies { get; set; }

    public virtual DbSet<AuditLog> AuditLogs { get; set; }

    public virtual DbSet<Bt> Bts { get; set; }

    public virtual DbSet<Cell> Cells { get; set; }

    public virtual DbSet<Direction> Directions { get; set; }

    public virtual DbSet<EquipmentType> EquipmentTypes { get; set; }

    public virtual DbSet<Incident> Incidents { get; set; }

    public virtual DbSet<IncidentPriority> IncidentPriorities { get; set; }

    public virtual DbSet<KpiCellHourly> KpiCellHourlies { get; set; }

    public virtual DbSet<KpiThreshold> KpiThresholds { get; set; }

    public virtual DbSet<MetricCatalog> MetricCatalogs { get; set; }

    public virtual DbSet<NetworkElement> NetworkElements { get; set; }

    public virtual DbSet<NetworkType> NetworkTypes { get; set; }

    public virtual DbSet<PmCountersHourly> PmCountersHourlies { get; set; }

    public virtual DbSet<Region> Regions { get; set; }

    public virtual DbSet<SpTmpMissing> SpTmpMissings { get; set; }

    public virtual DbSet<TechnicalCenter> TechnicalCenters { get; set; }

    public virtual DbSet<User> Users { get; set; }

    public virtual DbSet<VAnomaliesOpen> VAnomaliesOpens { get; set; }

    public virtual DbSet<VBtsFull> VBtsFulls { get; set; }

    public virtual DbSet<VIncidentsFull> VIncidentsFulls { get; set; }

    public virtual DbSet<VKpiThresholdsEffective> VKpiThresholdsEffectives { get; set; }

    public virtual DbSet<VNetworkSnapshot> VNetworkSnapshots { get; set; }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
#warning To protect potentially sensitive information in your connection string, you should move it out of source code. You can avoid scaffolding the connection string by using the Name= syntax to read it from configuration - see https://go.microsoft.com/fwlink/?linkid=2131148. For more guidance on storing connection strings, see https://go.microsoft.com/fwlink/?LinkId=723263.
        => optionsBuilder.UseMySql("server=127.0.0.1;port=3306;database=motiv_report;user=admin;password=LWXxukZhrn65wy7", Microsoft.EntityFrameworkCore.ServerVersion.Parse("13.0.2-mariadb"));

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder
            .UseCollation("utf8mb4_unicode_ci")
            .HasCharSet("utf8mb4");

        modelBuilder.Entity<AlarmType>(entity =>
        {
            entity.HasKey(e => e.AlarmTypeId).HasName("PRIMARY");

            entity.ToTable("alarm_types");

            entity.HasIndex(e => e.Code, "uq_alarm_code").IsUnique();

            entity.Property(e => e.AlarmTypeId)
                .HasColumnType("smallint(5) unsigned")
                .HasColumnName("alarm_type_id");
            entity.Property(e => e.Code)
                .HasMaxLength(64)
                .HasColumnName("code");
            entity.Property(e => e.Name)
                .HasMaxLength(128)
                .HasColumnName("name");
        });

        modelBuilder.Entity<Anomaly>(entity =>
        {
            entity.HasKey(e => e.AnomalyId).HasName("PRIMARY");

            entity.ToTable("anomalies");

            entity.HasIndex(e => new { e.CellId, e.TsFrom }, "ix_an_cell_time");

            entity.HasIndex(e => e.ExplainedByIncident, "ix_an_incident");

            entity.HasIndex(e => new { e.Status, e.Severity }, "ix_an_status");

            entity.HasIndex(e => new { e.AnomalyType, e.TsFrom }, "ix_an_type");

            entity.HasIndex(e => e.DedupHash, "uq_an_dedup").IsUnique();

            entity.Property(e => e.AnomalyId)
                .HasColumnType("bigint(20) unsigned")
                .HasColumnName("anomaly_id");
            entity.Property(e => e.AnomalyType)
                .HasColumnType("enum('threshold','zscore','missing_data','logical','cross_source')")
                .HasColumnName("anomaly_type");
            entity.Property(e => e.CellId)
                .HasColumnType("int(10) unsigned")
                .HasColumnName("cell_id");
            entity.Property(e => e.DedupHash)
                .HasMaxLength(64)
                .IsFixedLength()
                .HasColumnName("dedup_hash");
            entity.Property(e => e.DetectedAt)
                .HasDefaultValueSql("current_timestamp(3)")
                .HasColumnType("datetime(3)")
                .HasColumnName("detected_at");
            entity.Property(e => e.ExpectedValue)
                .HasPrecision(20, 4)
                .HasColumnName("expected_value");
            entity.Property(e => e.ExplainedByIncident)
                .HasColumnType("bigint(20) unsigned")
                .HasColumnName("explained_by_incident");
            entity.Property(e => e.KpiCode)
                .HasMaxLength(32)
                .HasColumnName("kpi_code");
            entity.Property(e => e.ObservedValue)
                .HasPrecision(20, 4)
                .HasColumnName("observed_value");
            entity.Property(e => e.Score)
                .HasPrecision(10, 4)
                .HasColumnName("score");
            entity.Property(e => e.Severity)
                .HasDefaultValueSql("'warning'")
                .HasColumnType("enum('info','warning','critical')")
                .HasColumnName("severity");
            entity.Property(e => e.Status)
                .HasDefaultValueSql("'new'")
                .HasColumnType("enum('new','acknowledged','explained','resolved','false_positive')")
                .HasColumnName("status");
            entity.Property(e => e.TsFrom)
                .HasColumnType("datetime(3)")
                .HasColumnName("ts_from");
            entity.Property(e => e.TsTo)
                .HasColumnType("datetime(3)")
                .HasColumnName("ts_to");

            entity.HasOne(d => d.Cell).WithMany(p => p.Anomalies)
                .HasForeignKey(d => d.CellId)
                .HasConstraintName("fk_an_cell");

            entity.HasOne(d => d.ExplainedByIncidentNavigation).WithMany(p => p.Anomalies)
                .HasForeignKey(d => d.ExplainedByIncident)
                .HasConstraintName("fk_an_incident");
        });

        modelBuilder.Entity<AuditLog>(entity =>
        {
            entity.HasKey(e => e.AuditId).HasName("PRIMARY");

            entity.ToTable("audit_log");

            entity.HasIndex(e => e.UserId, "fk_audit_user");

            entity.HasIndex(e => new { e.Action, e.Ts }, "ix_audit_action");

            entity.HasIndex(e => new { e.EntityType, e.EntityId }, "ix_audit_entity");

            entity.HasIndex(e => e.Ts, "ix_audit_ts");

            entity.HasIndex(e => new { e.Username, e.Ts }, "ix_audit_user");

            entity.Property(e => e.AuditId)
                .HasColumnType("bigint(20) unsigned")
                .HasColumnName("audit_id");
            entity.Property(e => e.Action)
                .HasColumnType("enum('login','create_incident','update_incident','delete_incident','detect_anomalies','refresh_kpi')")
                .HasColumnName("action");
            entity.Property(e => e.EntityId)
                .HasColumnType("bigint(20) unsigned")
                .HasColumnName("entity_id");
            entity.Property(e => e.EntityType)
                .HasMaxLength(32)
                .HasColumnName("entity_type");
            entity.Property(e => e.IpAddress)
                .HasMaxLength(45)
                .HasColumnName("ip_address");
            entity.Property(e => e.Role)
                .HasMaxLength(16)
                .HasColumnName("role");
            entity.Property(e => e.Success)
                .IsRequired()
                .HasDefaultValueSql("'1'")
                .HasColumnName("success");
            entity.Property(e => e.Ts)
                .HasDefaultValueSql("current_timestamp(3)")
                .HasColumnType("datetime(3)")
                .HasColumnName("ts");
            entity.Property(e => e.UserId)
                .HasColumnType("int(10) unsigned")
                .HasColumnName("user_id");
            entity.Property(e => e.Username)
                .HasMaxLength(64)
                .HasColumnName("username");

            entity.HasOne(d => d.User).WithMany(p => p.AuditLogs)
                .HasForeignKey(d => d.UserId)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("fk_audit_user");
        });

        modelBuilder.Entity<Bt>(entity =>
        {
            entity.HasKey(e => e.BtsId).HasName("PRIMARY");

            entity.ToTable("bts");

            entity.Property(e => e.BtsId)
                .ValueGeneratedNever()
                .HasColumnType("int(10) unsigned")
                .HasColumnName("bts_id");
            entity.Property(e => e.Address)
                .HasMaxLength(255)
                .HasColumnName("address");
            entity.Property(e => e.Lat)
                .HasPrecision(9, 6)
                .HasColumnName("lat");
            entity.Property(e => e.Lon)
                .HasPrecision(9, 6)
                .HasColumnName("lon");
            entity.Property(e => e.SiteType)
                .HasDefaultValueSql("'macro'")
                .HasColumnType("enum('macro','micro','indoor','rural')")
                .HasColumnName("site_type");

            entity.HasOne(d => d.Bts).WithOne(p => p.Bt)
                .HasForeignKey<Bt>(d => d.BtsId)
                .HasConstraintName("fk_bts_ne");
        });

        modelBuilder.Entity<Cell>(entity =>
        {
            entity.HasKey(e => e.CellId).HasName("PRIMARY");

            entity.ToTable("cells");

            entity.HasIndex(e => e.BtsId, "ix_cells_bts");

            entity.HasIndex(e => new { e.BtsId, e.LocalCellId }, "uq_cells_bts_local").IsUnique();

            entity.Property(e => e.CellId)
                .HasColumnType("int(10) unsigned")
                .HasColumnName("cell_id");
            entity.Property(e => e.BtsId)
                .HasColumnType("int(10) unsigned")
                .HasColumnName("bts_id");
            entity.Property(e => e.LocalCellId)
                .HasColumnType("smallint(5) unsigned")
                .HasColumnName("local_cell_id");
            entity.Property(e => e.Sector)
                .HasColumnType("enum('A','B','C','D','O')")
                .HasColumnName("sector");

            entity.HasOne(d => d.Bts).WithMany(p => p.Cells)
                .HasForeignKey(d => d.BtsId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("fk_cells_bts");
        });

        modelBuilder.Entity<Direction>(entity =>
        {
            entity.HasKey(e => e.DirectionId).HasName("PRIMARY");

            entity.ToTable("directions");

            entity.HasIndex(e => e.Code, "uq_directions_code").IsUnique();

            entity.Property(e => e.DirectionId)
                .HasColumnType("smallint(5) unsigned")
                .HasColumnName("direction_id");
            entity.Property(e => e.Code)
                .HasMaxLength(16)
                .HasColumnName("code");
            entity.Property(e => e.Kind)
                .HasDefaultValueSql("'direction'")
                .HasColumnType("enum('direction','federal_district')")
                .HasColumnName("kind");
            entity.Property(e => e.Name)
                .HasMaxLength(128)
                .HasColumnName("name");
        });

        modelBuilder.Entity<EquipmentType>(entity =>
        {
            entity.HasKey(e => e.EquipmentTypeId).HasName("PRIMARY");

            entity.ToTable("equipment_types");

            entity.HasIndex(e => e.Code, "uq_equip_code").IsUnique();

            entity.Property(e => e.EquipmentTypeId)
                .HasColumnType("smallint(5) unsigned")
                .HasColumnName("equipment_type_id");
            entity.Property(e => e.Code)
                .HasMaxLength(64)
                .HasColumnName("code");
            entity.Property(e => e.Name)
                .HasMaxLength(128)
                .HasColumnName("name");
        });

        modelBuilder.Entity<Incident>(entity =>
        {
            entity.HasKey(e => e.IncidentId).HasName("PRIMARY");

            entity.ToTable("incidents");

            entity.HasIndex(e => e.EquipmentTypeId, "fk_inc_equipment");

            entity.HasIndex(e => e.NetworkTypeId, "fk_inc_network_type");

            entity.HasIndex(e => e.PriorityId, "fk_inc_priority");

            entity.HasIndex(e => e.AlarmTypeId, "ix_inc_alarm");

            entity.HasIndex(e => new { e.Category, e.Severity }, "ix_inc_category");

            entity.HasIndex(e => e.CreatedByUserId, "ix_inc_created_by");

            entity.HasIndex(e => e.DirectionId, "ix_inc_direction");

            entity.HasIndex(e => new { e.NetworkElementId, e.BeginTime }, "ix_inc_ne");

            entity.HasIndex(e => e.RegionId, "ix_inc_region");

            entity.HasIndex(e => e.TechCenterId, "ix_inc_tech_center");

            entity.HasIndex(e => new { e.BeginTime, e.EndTime }, "ix_inc_time");

            entity.Property(e => e.IncidentId)
                .HasColumnType("bigint(20) unsigned")
                .HasColumnName("incident_id");
            entity.Property(e => e.AdminDistrict)
                .HasMaxLength(128)
                .HasColumnName("admin_district");
            entity.Property(e => e.Akb)
                .HasColumnType("time")
                .HasColumnName("akb");
            entity.Property(e => e.AlarmTypeId)
                .HasColumnType("smallint(5) unsigned")
                .HasColumnName("alarm_type_id");
            entity.Property(e => e.AlarmWho)
                .HasMaxLength(450)
                .HasColumnName("alarm_who");
            entity.Property(e => e.BeginTime)
                .HasColumnType("datetime")
                .HasColumnName("begin_time");
            entity.Property(e => e.Category)
                .HasDefaultValueSql("'other'")
                .HasColumnType("enum('power','transport','hardware','software','planned','client','external','other')")
                .HasColumnName("category");
            entity.Property(e => e.CommonProstoy)
                .HasMaxLength(100)
                .HasColumnName("common_prostoy");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("current_timestamp(3)")
                .HasColumnType("datetime(3)")
                .HasColumnName("created_at");
            entity.Property(e => e.CreatedByUserId)
                .HasColumnType("int(10) unsigned")
                .HasColumnName("created_by_user_id");
            entity.Property(e => e.DescNotice)
                .HasColumnType("text")
                .HasColumnName("desc_notice");
            entity.Property(e => e.Descr)
                .HasMaxLength(450)
                .HasColumnName("descr");
            entity.Property(e => e.Dgu)
                .HasColumnType("time")
                .HasColumnName("dgu");
            entity.Property(e => e.DirectionId)
                .HasColumnType("smallint(5) unsigned")
                .HasColumnName("direction_id");
            entity.Property(e => e.EndTime)
                .HasColumnType("datetime")
                .HasColumnName("end_time");
            entity.Property(e => e.EquipmentTypeId)
                .HasColumnType("smallint(5) unsigned")
                .HasColumnName("equipment_type_id");
            entity.Property(e => e.Lenght)
                .HasMaxLength(100)
                .HasColumnName("lenght");
            entity.Property(e => e.NetworkElementId)
                .HasColumnType("int(10) unsigned")
                .HasColumnName("network_element_id");
            entity.Property(e => e.NetworkTypeId)
                .HasColumnType("smallint(5) unsigned")
                .HasColumnName("network_type_id");
            entity.Property(e => e.NumbRbs)
                .HasMaxLength(20)
                .HasColumnName("numb_rbs");
            entity.Property(e => e.PriorityId)
                .HasColumnType("smallint(5) unsigned")
                .HasColumnName("priority_id");
            entity.Property(e => e.Prostoy)
                .HasColumnType("time")
                .HasColumnName("prostoy");
            entity.Property(e => e.Pw)
                .HasMaxLength(450)
                .HasColumnName("pw");
            entity.Property(e => e.RegionId)
                .HasColumnType("smallint(5) unsigned")
                .HasColumnName("region_id");
            entity.Property(e => e.ServElem)
                .HasMaxLength(450)
                .HasColumnName("serv_elem");
            entity.Property(e => e.Severity)
                .HasDefaultValueSql("'medium'")
                .HasColumnType("enum('low','medium','high','critical')")
                .HasColumnName("severity");
            entity.Property(e => e.TechCenterId)
                .HasColumnType("smallint(5) unsigned")
                .HasColumnName("tech_center_id");
            entity.Property(e => e.UpdatedAt)
                .ValueGeneratedOnAddOrUpdate()
                .HasDefaultValueSql("current_timestamp(3)")
                .HasColumnType("datetime(3)")
                .HasColumnName("updated_at");

            entity.HasOne(d => d.AlarmType).WithMany(p => p.Incidents)
                .HasForeignKey(d => d.AlarmTypeId)
                .HasConstraintName("fk_inc_alarm");

            entity.HasOne(d => d.CreatedByUser).WithMany(p => p.Incidents)
                .HasForeignKey(d => d.CreatedByUserId)
                .HasConstraintName("fk_inc_user");

            entity.HasOne(d => d.Direction).WithMany(p => p.Incidents)
                .HasForeignKey(d => d.DirectionId)
                .HasConstraintName("fk_inc_direction");

            entity.HasOne(d => d.EquipmentType).WithMany(p => p.Incidents)
                .HasForeignKey(d => d.EquipmentTypeId)
                .HasConstraintName("fk_inc_equipment");

            entity.HasOne(d => d.NetworkElement).WithMany(p => p.Incidents)
                .HasForeignKey(d => d.NetworkElementId)
                .HasConstraintName("fk_inc_ne");

            entity.HasOne(d => d.NetworkType).WithMany(p => p.Incidents)
                .HasForeignKey(d => d.NetworkTypeId)
                .HasConstraintName("fk_inc_network_type");

            entity.HasOne(d => d.Priority).WithMany(p => p.Incidents)
                .HasForeignKey(d => d.PriorityId)
                .HasConstraintName("fk_inc_priority");

            entity.HasOne(d => d.Region).WithMany(p => p.Incidents)
                .HasForeignKey(d => d.RegionId)
                .HasConstraintName("fk_inc_region");

            entity.HasOne(d => d.TechCenter).WithMany(p => p.Incidents)
                .HasForeignKey(d => d.TechCenterId)
                .HasConstraintName("fk_inc_tech_center");
        });

        modelBuilder.Entity<IncidentPriority>(entity =>
        {
            entity.HasKey(e => e.PriorityId).HasName("PRIMARY");

            entity.ToTable("incident_priorities");

            entity.HasIndex(e => e.Code, "uq_priority_code").IsUnique();

            entity.Property(e => e.PriorityId)
                .HasColumnType("smallint(5) unsigned")
                .HasColumnName("priority_id");
            entity.Property(e => e.Code)
                .HasMaxLength(8)
                .HasColumnName("code");
            entity.Property(e => e.Name)
                .HasMaxLength(64)
                .HasColumnName("name");
            entity.Property(e => e.SortOrder)
                .HasColumnType("tinyint(4)")
                .HasColumnName("sort_order");
        });

        modelBuilder.Entity<KpiCellHourly>(entity =>
        {
            entity.HasKey(e => new { e.CellId, e.TsHour })
                .HasName("PRIMARY")
                .HasAnnotation("MySql:IndexPrefixLength", new[] { 0, 0 });

            entity.ToTable("kpi_cell_hourly");

            entity.HasIndex(e => e.TsHour, "ix_kpi_ts");

            entity.Property(e => e.CellId)
                .HasColumnType("int(10) unsigned")
                .HasColumnName("cell_id");
            entity.Property(e => e.TsHour)
                .HasColumnType("datetime(3)")
                .HasColumnName("ts_hour");
            entity.Property(e => e.ComputedAt)
                .HasDefaultValueSql("current_timestamp(3)")
                .HasColumnType("datetime(3)")
                .HasColumnName("computed_at");
            entity.Property(e => e.ConnAttempt)
                .HasColumnType("bigint(20) unsigned")
                .HasColumnName("conn_attempt");
            entity.Property(e => e.ConnDrop)
                .HasColumnType("bigint(20) unsigned")
                .HasColumnName("conn_drop");
            entity.Property(e => e.ConnSuccess)
                .HasColumnType("bigint(20) unsigned")
                .HasColumnName("conn_success");
            entity.Property(e => e.CssrPct)
                .HasPrecision(7, 4)
                .HasColumnName("cssr_pct");
            entity.Property(e => e.DcrPct)
                .HasPrecision(7, 4)
                .HasColumnName("dcr_pct");
            entity.Property(e => e.TrafficDlBytes)
                .HasColumnType("bigint(20) unsigned")
                .HasColumnName("traffic_dl_bytes");
            entity.Property(e => e.TrafficUlBytes)
                .HasColumnType("bigint(20) unsigned")
                .HasColumnName("traffic_ul_bytes");

            entity.HasOne(d => d.Cell).WithMany(p => p.KpiCellHourlies)
                .HasForeignKey(d => d.CellId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("fk_kpi_cell");
        });

        modelBuilder.Entity<KpiThreshold>(entity =>
        {
            entity.HasKey(e => e.ThresholdId).HasName("PRIMARY");

            entity.ToTable("kpi_thresholds");

            entity.HasIndex(e => e.RegionId, "fk_thr_region");

            entity.HasIndex(e => new { e.KpiCode, e.RegionId }, "uq_thr").IsUnique();

            entity.Property(e => e.ThresholdId)
                .HasColumnType("bigint(20) unsigned")
                .HasColumnName("threshold_id");
            entity.Property(e => e.Direction)
                .HasDefaultValueSql("'lower_is_worse'")
                .HasColumnType("enum('lower_is_worse','higher_is_worse')")
                .HasColumnName("direction");
            entity.Property(e => e.Enabled)
                .IsRequired()
                .HasDefaultValueSql("'1'")
                .HasColumnName("enabled");
            entity.Property(e => e.KpiCode)
                .HasMaxLength(32)
                .HasColumnName("kpi_code");
            entity.Property(e => e.MaxValue)
                .HasPrecision(12, 4)
                .HasColumnName("max_value");
            entity.Property(e => e.MinValue)
                .HasPrecision(12, 4)
                .HasColumnName("min_value");
            entity.Property(e => e.RegionId)
                .HasColumnType("smallint(5) unsigned")
                .HasColumnName("region_id");
            entity.Property(e => e.Severity)
                .HasDefaultValueSql("'warning'")
                .HasColumnType("enum('info','warning','critical')")
                .HasColumnName("severity");

            entity.HasOne(d => d.Region).WithMany(p => p.KpiThresholds)
                .HasForeignKey(d => d.RegionId)
                .HasConstraintName("fk_thr_region");
        });

        modelBuilder.Entity<MetricCatalog>(entity =>
        {
            entity.HasKey(e => e.MetricId).HasName("PRIMARY");

            entity.ToTable("metric_catalog");

            entity.HasIndex(e => e.MetricCode, "uq_metric_code").IsUnique();

            entity.Property(e => e.MetricId)
                .HasColumnType("smallint(5) unsigned")
                .HasColumnName("metric_id");
            entity.Property(e => e.MetricCode)
                .HasMaxLength(64)
                .HasColumnName("metric_code");
            entity.Property(e => e.MetricKind)
                .HasDefaultValueSql("'counter'")
                .HasColumnType("enum('counter','volume','duration','gauge')")
                .HasColumnName("metric_kind");
            entity.Property(e => e.MetricName)
                .HasMaxLength(128)
                .HasColumnName("metric_name");
            entity.Property(e => e.Unit)
                .HasMaxLength(16)
                .HasColumnName("unit");
        });

        modelBuilder.Entity<NetworkElement>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");

            entity.ToTable("network_elements");

            entity.HasIndex(e => e.RegionId, "ix_ne_region");

            entity.HasIndex(e => e.TechCenterId, "ix_ne_tech_center");

            entity.HasIndex(e => new { e.ElementType, e.Status }, "ix_ne_type_status");

            entity.HasIndex(e => e.Code, "uq_ne_code").IsUnique();

            entity.HasIndex(e => e.NetworkElementCode, "uq_ne_network_code").IsUnique();

            entity.Property(e => e.Id)
                .HasColumnType("int(10) unsigned")
                .HasColumnName("id");
            entity.Property(e => e.ActualRestoreDate)
                .HasColumnType("datetime")
                .HasColumnName("actual_restore_date");
            entity.Property(e => e.Code)
                .HasMaxLength(64)
                .HasColumnName("code");
            entity.Property(e => e.CommissionedAt)
                .HasColumnType("datetime")
                .HasColumnName("commissioned_at");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("current_timestamp(3)")
                .HasColumnType("datetime(3)")
                .HasColumnName("created_at");
            entity.Property(e => e.DecommissionedAt)
                .HasColumnType("datetime")
                .HasColumnName("decommissioned_at");
            entity.Property(e => e.Description)
                .HasColumnType("text")
                .HasColumnName("description");
            entity.Property(e => e.ElementType)
                .HasColumnType("enum('bts','ip_node','optical_node','dwdm_node','rrl_node','opts_node','sss_node','client_channel','mgmn_node')")
                .HasColumnName("element_type");
            entity.Property(e => e.ExpectedRestoreDate)
                .HasColumnType("datetime")
                .HasColumnName("expected_restore_date");
            entity.Property(e => e.FullName)
                .HasMaxLength(450)
                .HasColumnName("full_name");
            entity.Property(e => e.NetworkElementCode)
                .HasMaxLength(16)
                .HasColumnName("network_element_code");
            entity.Property(e => e.RegionId)
                .HasColumnType("smallint(5) unsigned")
                .HasColumnName("region_id");
            entity.Property(e => e.ShortName)
                .HasMaxLength(128)
                .HasColumnName("short_name");
            entity.Property(e => e.Status)
                .HasDefaultValueSql("'active'")
                .HasColumnType("enum('active','degraded','down','maintenance','decommissioned')")
                .HasColumnName("status");
            entity.Property(e => e.StatusSince)
                .HasColumnType("datetime(3)")
                .HasColumnName("status_since");
            entity.Property(e => e.StopReason)
                .HasMaxLength(500)
                .HasColumnName("stop_reason");
            entity.Property(e => e.TechCenterId)
                .HasColumnType("smallint(5) unsigned")
                .HasColumnName("tech_center_id");

            entity.HasOne(d => d.Region).WithMany(p => p.NetworkElements)
                .HasForeignKey(d => d.RegionId)
                .HasConstraintName("fk_ne_region");

            entity.HasOne(d => d.TechCenter).WithMany(p => p.NetworkElements)
                .HasForeignKey(d => d.TechCenterId)
                .HasConstraintName("fk_ne_tech_center");
        });

        modelBuilder.Entity<NetworkType>(entity =>
        {
            entity.HasKey(e => e.NetworkTypeId).HasName("PRIMARY");

            entity.ToTable("network_types");

            entity.HasIndex(e => e.Code, "uq_netw_code").IsUnique();

            entity.Property(e => e.NetworkTypeId)
                .HasColumnType("smallint(5) unsigned")
                .HasColumnName("network_type_id");
            entity.Property(e => e.Code)
                .HasMaxLength(64)
                .HasColumnName("code");
            entity.Property(e => e.Name)
                .HasMaxLength(128)
                .HasColumnName("name");
        });

        modelBuilder.Entity<PmCountersHourly>(entity =>
        {
            entity.HasKey(e => new { e.CellId, e.TsHour, e.MetricId })
                .HasName("PRIMARY")
                .HasAnnotation("MySql:IndexPrefixLength", new[] { 0, 0, 0 });

            entity.ToTable("pm_counters_hourly");

            entity.HasIndex(e => new { e.MetricId, e.TsHour }, "ix_pm_metric_ts");

            entity.HasIndex(e => e.TsHour, "ix_pm_ts");

            entity.Property(e => e.CellId)
                .HasColumnType("int(10) unsigned")
                .HasColumnName("cell_id");
            entity.Property(e => e.TsHour)
                .HasColumnType("datetime(3)")
                .HasColumnName("ts_hour");
            entity.Property(e => e.MetricId)
                .HasColumnType("smallint(5) unsigned")
                .HasColumnName("metric_id");
            entity.Property(e => e.Value)
                .HasPrecision(20, 4)
                .HasColumnName("value");

            entity.HasOne(d => d.Cell).WithMany(p => p.PmCountersHourlies)
                .HasForeignKey(d => d.CellId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("fk_pm_cell");

            entity.HasOne(d => d.Metric).WithMany(p => p.PmCountersHourlies)
                .HasForeignKey(d => d.MetricId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("fk_pm_metric");
        });

        modelBuilder.Entity<Region>(entity =>
        {
            entity.HasKey(e => e.RegionId).HasName("PRIMARY");

            entity.ToTable("regions");

            entity.HasIndex(e => e.Code, "uq_regions_code").IsUnique();

            entity.Property(e => e.RegionId)
                .HasColumnType("smallint(5) unsigned")
                .HasColumnName("region_id");
            entity.Property(e => e.Code)
                .HasMaxLength(8)
                .HasColumnName("code");
            entity.Property(e => e.Name)
                .HasMaxLength(128)
                .HasColumnName("name");
            entity.Property(e => e.TzOffsetMin)
                .HasDefaultValueSql("'300'")
                .HasColumnType("smallint(6)")
                .HasColumnName("tz_offset_min");
        });

        modelBuilder.Entity<SpTmpMissing>(entity =>
        {
            entity.HasKey(e => new { e.CellId, e.TsHour })
                .HasName("PRIMARY")
                .HasAnnotation("MySql:IndexPrefixLength", new[] { 0, 0 });

            entity.ToTable("_sp_tmp_missing");

            entity.Property(e => e.CellId)
                .HasColumnType("int(10) unsigned")
                .HasColumnName("cell_id");
            entity.Property(e => e.TsHour)
                .HasColumnType("datetime(3)")
                .HasColumnName("ts_hour");
        });

        modelBuilder.Entity<TechnicalCenter>(entity =>
        {
            entity.HasKey(e => e.TechCenterId).HasName("PRIMARY");

            entity.ToTable("technical_centers");

            entity.HasIndex(e => e.DirectionId, "ix_tc_direction");

            entity.HasIndex(e => e.Code, "uq_tc_code").IsUnique();

            entity.Property(e => e.TechCenterId)
                .HasColumnType("smallint(5) unsigned")
                .HasColumnName("tech_center_id");
            entity.Property(e => e.Code)
                .HasMaxLength(16)
                .HasColumnName("code");
            entity.Property(e => e.DirectionId)
                .HasColumnType("smallint(5) unsigned")
                .HasColumnName("direction_id");
            entity.Property(e => e.Name)
                .HasMaxLength(128)
                .HasColumnName("name");

            entity.HasOne(d => d.Direction).WithMany(p => p.TechnicalCenters)
                .HasForeignKey(d => d.DirectionId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("fk_tc_direction");
        });

        modelBuilder.Entity<User>(entity =>
        {
            entity.HasKey(e => e.UserId).HasName("PRIMARY");

            entity.ToTable("users");

            entity.HasIndex(e => e.Username, "uq_users_username").IsUnique();

            entity.Property(e => e.UserId)
                .HasColumnType("int(10) unsigned")
                .HasColumnName("user_id");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("current_timestamp(3)")
                .HasColumnType("datetime(3)")
                .HasColumnName("created_at");
            entity.Property(e => e.PasswordHash)
                .HasMaxLength(255)
                .HasColumnName("password_hash");
            entity.Property(e => e.Role)
                .HasDefaultValueSql("'engineer'")
                .HasColumnType("enum('engineer','operator','admin')")
                .HasColumnName("role");
            entity.Property(e => e.Username)
                .HasMaxLength(64)
                .HasColumnName("username");
        });

        modelBuilder.Entity<VAnomaliesOpen>(entity =>
        {
            entity
                .HasNoKey()
                .ToView("v_anomalies_open");

            entity.Property(e => e.AnomalyId)
                .HasColumnType("bigint(20) unsigned")
                .HasColumnName("anomaly_id");
            entity.Property(e => e.AnomalyType)
                .HasColumnType("enum('threshold','zscore','missing_data','logical','cross_source')")
                .HasColumnName("anomaly_type");
            entity.Property(e => e.BtsCode)
                .HasMaxLength(64)
                .HasColumnName("bts_code");
            entity.Property(e => e.BtsFullName)
                .HasMaxLength(450)
                .HasColumnName("bts_full_name");
            entity.Property(e => e.BtsId)
                .HasColumnType("int(10) unsigned")
                .HasColumnName("bts_id");
            entity.Property(e => e.CellId)
                .HasColumnType("int(10) unsigned")
                .HasColumnName("cell_id");
            entity.Property(e => e.DetectedAt)
                .HasDefaultValueSql("current_timestamp(3)")
                .HasColumnType("datetime(3)")
                .HasColumnName("detected_at");
            entity.Property(e => e.ExpectedValue)
                .HasPrecision(20, 4)
                .HasColumnName("expected_value");
            entity.Property(e => e.KpiCode)
                .HasMaxLength(32)
                .HasColumnName("kpi_code");
            entity.Property(e => e.ObservedValue)
                .HasPrecision(20, 4)
                .HasColumnName("observed_value");
            entity.Property(e => e.RegionCode)
                .HasMaxLength(8)
                .HasColumnName("region_code");
            entity.Property(e => e.RegionId)
                .HasColumnType("smallint(5) unsigned")
                .HasColumnName("region_id");
            entity.Property(e => e.Score)
                .HasPrecision(10, 4)
                .HasColumnName("score");
            entity.Property(e => e.Severity)
                .HasDefaultValueSql("'warning'")
                .HasColumnType("enum('info','warning','critical')")
                .HasColumnName("severity");
            entity.Property(e => e.Status)
                .HasDefaultValueSql("'new'")
                .HasColumnType("enum('new','acknowledged','explained','resolved','false_positive')")
                .HasColumnName("status");
            entity.Property(e => e.TsFrom)
                .HasColumnType("datetime(3)")
                .HasColumnName("ts_from");
            entity.Property(e => e.TsTo)
                .HasColumnType("datetime(3)")
                .HasColumnName("ts_to");
        });

        modelBuilder.Entity<VBtsFull>(entity =>
        {
            entity
                .HasNoKey()
                .ToView("v_bts_full");

            entity.Property(e => e.ActualRestoreDate)
                .HasColumnType("datetime")
                .HasColumnName("actual_restore_date");
            entity.Property(e => e.Address)
                .HasMaxLength(255)
                .HasColumnName("address");
            entity.Property(e => e.BtsId)
                .HasColumnType("int(10) unsigned")
                .HasColumnName("bts_id");
            entity.Property(e => e.Code)
                .HasMaxLength(64)
                .HasColumnName("code");
            entity.Property(e => e.CommissionedAt)
                .HasColumnType("datetime")
                .HasColumnName("commissioned_at");
            entity.Property(e => e.DecommissionedAt)
                .HasColumnType("datetime")
                .HasColumnName("decommissioned_at");
            entity.Property(e => e.DirectionCode)
                .HasMaxLength(16)
                .HasColumnName("direction_code");
            entity.Property(e => e.ExpectedRestoreDate)
                .HasColumnType("datetime")
                .HasColumnName("expected_restore_date");
            entity.Property(e => e.FullName)
                .HasMaxLength(450)
                .HasColumnName("full_name");
            entity.Property(e => e.Lat)
                .HasPrecision(9, 6)
                .HasColumnName("lat");
            entity.Property(e => e.Lon)
                .HasPrecision(9, 6)
                .HasColumnName("lon");
            entity.Property(e => e.NetworkElementCode)
                .HasMaxLength(16)
                .HasColumnName("network_element_code");
            entity.Property(e => e.RegionCode)
                .HasMaxLength(8)
                .HasColumnName("region_code");
            entity.Property(e => e.RegionId)
                .HasColumnType("smallint(5) unsigned")
                .HasColumnName("region_id");
            entity.Property(e => e.RegionName)
                .HasMaxLength(128)
                .HasColumnName("region_name");
            entity.Property(e => e.ShortName)
                .HasMaxLength(128)
                .HasColumnName("short_name");
            entity.Property(e => e.SiteType)
                .HasDefaultValueSql("'macro'")
                .HasColumnType("enum('macro','micro','indoor','rural')")
                .HasColumnName("site_type");
            entity.Property(e => e.Status)
                .HasDefaultValueSql("'active'")
                .HasColumnType("enum('active','degraded','down','maintenance','decommissioned')")
                .HasColumnName("status");
            entity.Property(e => e.StatusSince)
                .HasColumnType("datetime(3)")
                .HasColumnName("status_since");
            entity.Property(e => e.StopReason)
                .HasMaxLength(500)
                .HasColumnName("stop_reason");
            entity.Property(e => e.TechCenterCode)
                .HasMaxLength(16)
                .HasColumnName("tech_center_code");
            entity.Property(e => e.TechCenterId)
                .HasColumnType("smallint(5) unsigned")
                .HasColumnName("tech_center_id");
            entity.Property(e => e.TechCenterName)
                .HasMaxLength(128)
                .HasColumnName("tech_center_name");
        });

        modelBuilder.Entity<VIncidentsFull>(entity =>
        {
            entity
                .HasNoKey()
                .ToView("v_incidents_full");

            entity.Property(e => e.AdminDistrict)
                .HasMaxLength(128)
                .HasColumnName("admin_district");
            entity.Property(e => e.Akb)
                .HasColumnType("time")
                .HasColumnName("akb");
            entity.Property(e => e.AlarmTypeCode)
                .HasMaxLength(64)
                .HasColumnName("alarm_type_code");
            entity.Property(e => e.AlarmWho)
                .HasMaxLength(450)
                .HasColumnName("alarm_who");
            entity.Property(e => e.BeginTime)
                .HasColumnType("datetime")
                .HasColumnName("begin_time");
            entity.Property(e => e.Category)
                .HasDefaultValueSql("'other'")
                .HasColumnType("enum('power','transport','hardware','software','planned','client','external','other')")
                .HasColumnName("category");
            entity.Property(e => e.CommonProstoy)
                .HasMaxLength(100)
                .HasColumnName("common_prostoy");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("current_timestamp(3)")
                .HasColumnType("datetime(3)")
                .HasColumnName("created_at");
            entity.Property(e => e.CreatedByUserId)
                .HasColumnType("int(10) unsigned")
                .HasColumnName("created_by_user_id");
            entity.Property(e => e.DescNotice)
                .HasColumnType("text")
                .HasColumnName("desc_notice");
            entity.Property(e => e.Descr)
                .HasMaxLength(450)
                .HasColumnName("descr");
            entity.Property(e => e.Dgu)
                .HasColumnType("time")
                .HasColumnName("dgu");
            entity.Property(e => e.DirectionCode)
                .HasMaxLength(16)
                .HasColumnName("direction_code");
            entity.Property(e => e.EndTime)
                .HasColumnType("datetime")
                .HasColumnName("end_time");
            entity.Property(e => e.EquipmentTypeCode)
                .HasMaxLength(64)
                .HasColumnName("equipment_type_code");
            entity.Property(e => e.IncidentId)
                .HasColumnType("bigint(20) unsigned")
                .HasColumnName("incident_id");
            entity.Property(e => e.Lenght)
                .HasMaxLength(100)
                .HasColumnName("lenght");
            entity.Property(e => e.NetworkElementName)
                .HasMaxLength(450)
                .HasColumnName("network_element_name");
            entity.Property(e => e.NetworkElementType)
                .HasColumnType("enum('bts','ip_node','optical_node','dwdm_node','rrl_node','opts_node','sss_node','client_channel','mgmn_node')")
                .HasColumnName("network_element_type");
            entity.Property(e => e.NetworkTypeCode)
                .HasMaxLength(64)
                .HasColumnName("network_type_code");
            entity.Property(e => e.NumbRbs)
                .HasMaxLength(20)
                .HasColumnName("numb_rbs");
            entity.Property(e => e.PriorityCode)
                .HasMaxLength(8)
                .HasColumnName("priority_code");
            entity.Property(e => e.Prostoy)
                .HasColumnType("time")
                .HasColumnName("prostoy");
            entity.Property(e => e.Pw)
                .HasMaxLength(450)
                .HasColumnName("pw");
            entity.Property(e => e.RegionId)
                .HasColumnType("smallint(5) unsigned")
                .HasColumnName("region_id");
            entity.Property(e => e.ServElem)
                .HasMaxLength(450)
                .HasColumnName("serv_elem");
            entity.Property(e => e.Severity)
                .HasDefaultValueSql("'medium'")
                .HasColumnType("enum('low','medium','high','critical')")
                .HasColumnName("severity");
            entity.Property(e => e.TechCenterCode)
                .HasMaxLength(16)
                .HasColumnName("tech_center_code");
            entity.Property(e => e.UpdatedAt)
                .HasDefaultValueSql("current_timestamp(3)")
                .HasColumnType("datetime(3)")
                .HasColumnName("updated_at");
        });

        modelBuilder.Entity<VKpiThresholdsEffective>(entity =>
        {
            entity
                .HasNoKey()
                .ToView("v_kpi_thresholds_effective");

            entity.Property(e => e.Direction)
                .HasMaxLength(15)
                .HasDefaultValueSql("''")
                .HasColumnName("direction");
            entity.Property(e => e.Enabled)
                .HasColumnType("tinyint(4)")
                .HasColumnName("enabled");
            entity.Property(e => e.KpiCode)
                .HasMaxLength(32)
                .HasDefaultValueSql("''")
                .HasColumnName("kpi_code");
            entity.Property(e => e.MaxValue)
                .HasPrecision(12, 4)
                .HasColumnName("max_value");
            entity.Property(e => e.MinValue)
                .HasPrecision(12, 4)
                .HasColumnName("min_value");
            entity.Property(e => e.RegionId)
                .HasColumnType("smallint(5) unsigned")
                .HasColumnName("region_id");
            entity.Property(e => e.Severity)
                .HasMaxLength(8)
                .HasDefaultValueSql("''")
                .HasColumnName("severity");
            entity.Property(e => e.ThresholdId)
                .HasColumnType("bigint(20) unsigned")
                .HasColumnName("threshold_id");
        });

        modelBuilder.Entity<VNetworkSnapshot>(entity =>
        {
            entity
                .HasNoKey()
                .ToView("v_network_snapshot");

            entity.Property(e => e.BtsActive)
                .HasPrecision(23)
                .HasColumnName("bts_active");
            entity.Property(e => e.BtsDecommissioned)
                .HasPrecision(23)
                .HasColumnName("bts_decommissioned");
            entity.Property(e => e.BtsMaintenance)
                .HasPrecision(23)
                .HasColumnName("bts_maintenance");
            entity.Property(e => e.BtsProblem)
                .HasPrecision(23)
                .HasColumnName("bts_problem");
            entity.Property(e => e.BtsTotal)
                .HasColumnType("bigint(21)")
                .HasColumnName("bts_total");
            entity.Property(e => e.CellTotal)
                .HasColumnType("bigint(21)")
                .HasColumnName("cell_total");
            entity.Property(e => e.RegionCode)
                .HasMaxLength(8)
                .HasColumnName("region_code");
            entity.Property(e => e.RegionId)
                .HasColumnType("smallint(5) unsigned")
                .HasColumnName("region_id");
            entity.Property(e => e.RegionName)
                .HasMaxLength(128)
                .HasColumnName("region_name");
        });

        OnModelCreatingPartial(modelBuilder);
    }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
}
