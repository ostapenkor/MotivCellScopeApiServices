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

    public virtual DbSet<Anomaly> Anomalies { get; set; }

    public virtual DbSet<Bt> Bts { get; set; }

    public virtual DbSet<Cell> Cells { get; set; }

    public virtual DbSet<Incident> Incidents { get; set; }

    public virtual DbSet<KpiCellHourly> KpiCellHourlies { get; set; }

    public virtual DbSet<KpiThreshold> KpiThresholds { get; set; }

    public virtual DbSet<MetricCatalog> MetricCatalogs { get; set; }

    public virtual DbSet<PmCountersHourly> PmCountersHourlies { get; set; }

    public virtual DbSet<Region> Regions { get; set; }

    public virtual DbSet<User> Users { get; set; }

    public virtual DbSet<VAnomaliesOpen> VAnomaliesOpens { get; set; }

    public virtual DbSet<VKpiThresholdsEffective> VKpiThresholdsEffectives { get; set; }

    public virtual DbSet<VNetworkSnapshot> VNetworkSnapshots { get; set; }

    public virtual DbSet<Vendor> Vendors { get; set; }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) { }
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder
            .UseCollation("utf8mb4_unicode_ci")
            .HasCharSet("utf8mb4");

        modelBuilder.Entity<Anomaly>(entity =>
        {
            entity.HasKey(e => e.AnomalyId).HasName("PRIMARY");

            entity.ToTable("anomalies");

            entity.HasIndex(e => e.ExplainedByIncident, "fk_an_incident");

            entity.HasIndex(e => new { e.CellId, e.TsFrom }, "ix_an_cell_time");

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
                .HasComputedColumnSql("sha2(concat_ws('|',coalesce(`cell_id`,0),`ts_from`,`ts_to`,`anomaly_type`,coalesce(`kpi_code`,'')),256)", true)
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

        modelBuilder.Entity<Bt>(entity =>
        {
            entity.HasKey(e => e.BtsId).HasName("PRIMARY");

            entity.ToTable("bts");

            entity.HasIndex(e => e.RegionId, "ix_bts_region");

            entity.HasIndex(e => e.Status, "ix_bts_status");

            entity.HasIndex(e => e.VendorId, "ix_bts_vendor");

            entity.HasIndex(e => e.Code, "uq_bts_code").IsUnique();

            entity.Property(e => e.BtsId)
                .HasColumnType("int(10) unsigned")
                .HasColumnName("bts_id");
            entity.Property(e => e.Address)
                .HasMaxLength(255)
                .HasColumnName("address");
            entity.Property(e => e.Code)
                .HasMaxLength(64)
                .HasColumnName("code");
            entity.Property(e => e.Lat)
                .HasPrecision(9, 6)
                .HasColumnName("lat");
            entity.Property(e => e.Lon)
                .HasPrecision(9, 6)
                .HasColumnName("lon");
            entity.Property(e => e.RegionId)
                .HasColumnType("smallint(5) unsigned")
                .HasColumnName("region_id");
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
            entity.Property(e => e.VendorId)
                .HasColumnType("smallint(5) unsigned")
                .HasColumnName("vendor_id");

            entity.HasOne(d => d.Region).WithMany(p => p.Bts)
                .HasForeignKey(d => d.RegionId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("fk_bts_region");

            entity.HasOne(d => d.Vendor).WithMany(p => p.Bts)
                .HasForeignKey(d => d.VendorId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("fk_bts_vendor");
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

        modelBuilder.Entity<Incident>(entity =>
        {
            entity.HasKey(e => e.IncidentId).HasName("PRIMARY");

            entity.ToTable("incidents");

            entity.HasIndex(e => e.RegionId, "fk_inc_region");

            entity.HasIndex(e => new { e.BtsId, e.StartedAt }, "ix_inc_bts");

            entity.HasIndex(e => new { e.StartedAt, e.ResolvedAt }, "ix_inc_time");

            entity.Property(e => e.IncidentId)
                .HasColumnType("bigint(20) unsigned")
                .HasColumnName("incident_id");
            entity.Property(e => e.BtsId)
                .HasColumnType("int(10) unsigned")
                .HasColumnName("bts_id");
            entity.Property(e => e.Category)
                .HasColumnType("enum('power','transport','hardware','software','planned','external')")
                .HasColumnName("category");
            entity.Property(e => e.Description)
                .HasMaxLength(500)
                .HasColumnName("description");
            entity.Property(e => e.RegionId)
                .HasColumnType("smallint(5) unsigned")
                .HasColumnName("region_id");
            entity.Property(e => e.ResolvedAt)
                .HasColumnType("datetime(3)")
                .HasColumnName("resolved_at");
            entity.Property(e => e.Severity)
                .HasDefaultValueSql("'medium'")
                .HasColumnType("enum('low','medium','high','critical')")
                .HasColumnName("severity");
            entity.Property(e => e.StartedAt)
                .HasColumnType("datetime(3)")
                .HasColumnName("started_at");

            entity.HasOne(d => d.Bts).WithMany(p => p.Incidents)
                .HasForeignKey(d => d.BtsId)
                .HasConstraintName("fk_inc_bts");

            entity.HasOne(d => d.Region).WithMany(p => p.Incidents)
                .HasForeignKey(d => d.RegionId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("fk_inc_region");
        });

        modelBuilder.Entity<KpiCellHourly>(entity =>
        {
            entity.HasKey(e => new { e.CellId, e.TsHour })
                .HasName("PRIMARY")
                .HasAnnotation("MySql:IndexPrefixLength", new[] { 0, 0 });

            entity.ToTable("kpi_cell_hourly");

            entity.HasIndex(e => e.TsHour, "ix_kpi_ts");

            entity.HasIndex(e => new { e.VendorId, e.TsHour }, "ix_kpi_vendor");

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
            entity.Property(e => e.VendorId)
                .HasColumnType("smallint(5) unsigned")
                .HasColumnName("vendor_id");

            entity.HasOne(d => d.Cell).WithMany(p => p.KpiCellHourlies)
                .HasForeignKey(d => d.CellId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("fk_kpi_cell");

            entity.HasOne(d => d.Vendor).WithMany(p => p.KpiCellHourlies)
                .HasForeignKey(d => d.VendorId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("fk_kpi_vendor");
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

        modelBuilder.Entity<PmCountersHourly>(entity =>
        {
            entity.HasKey(e => new { e.VendorId, e.CellId, e.TsHour, e.MetricId })
                .HasName("PRIMARY")
                .HasAnnotation("MySql:IndexPrefixLength", new[] { 0, 0, 0, 0 });

            entity.ToTable("pm_counters_hourly");

            entity.HasIndex(e => new { e.CellId, e.TsHour }, "ix_pm_cell_ts");

            entity.HasIndex(e => new { e.MetricId, e.TsHour }, "ix_pm_metric_ts");

            entity.HasIndex(e => e.TsHour, "ix_pm_ts");

            entity.Property(e => e.VendorId)
                .HasColumnType("smallint(5) unsigned")
                .HasColumnName("vendor_id");
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

            entity.HasOne(d => d.Vendor).WithMany(p => p.PmCountersHourlies)
                .HasForeignKey(d => d.VendorId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("fk_pm_vendor");
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
            entity.Property(e => e.ExplainedByIncident)
                .HasColumnType("bigint(20) unsigned")
                .HasColumnName("explained_by_incident");
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

        modelBuilder.Entity<VKpiThresholdsEffective>(entity =>
        {
            entity
                .HasNoKey()
                .ToView("v_kpi_thresholds_effective");

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

        modelBuilder.Entity<Vendor>(entity =>
        {
            entity.HasKey(e => e.VendorId).HasName("PRIMARY");

            entity.ToTable("vendors");

            entity.HasIndex(e => e.Code, "uq_vendors_code").IsUnique();

            entity.Property(e => e.VendorId)
                .HasColumnType("smallint(5) unsigned")
                .HasColumnName("vendor_id");
            entity.Property(e => e.Code)
                .HasMaxLength(32)
                .HasColumnName("code");
            entity.Property(e => e.Name)
                .HasMaxLength(128)
                .HasColumnName("name");
            entity.Property(e => e.RatSupported)
                .HasMaxLength(64)
                .HasColumnName("rat_supported");
        });

        OnModelCreatingPartial(modelBuilder);
    }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
}
