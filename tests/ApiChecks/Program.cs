using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using MySqlConnector;
using MotivCellScopeApiServices.Controllers;
using MotivCellScopeApiServices.Models;

var cs = new MySqlConnectionStringBuilder(Environment.GetEnvironmentVariable("API_CHECK_CONNECTION")!) { AllowUserVariables = true }.ConnectionString;
var database = "kolya_api_test_" + Guid.NewGuid().ToString("N");
await using var connection = new MySqlConnection(cs);
await connection.OpenAsync();
async Task Sql(string sql) { using var command = new MySqlCommand(sql, connection); await command.ExecuteNonQueryAsync(); }
if (Environment.GetEnvironmentVariable("API_CHECK_INIT") is string initPath)
{
    var seedDatabase = "kolya_seed_test_" + Guid.NewGuid().ToString("N");
    await Sql($"CREATE DATABASE `{seedDatabase}` CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci");
    try
    {
        await connection.ChangeDatabaseAsync(seedDatabase);
        var delimiter = ";";
        var statement = new StringBuilder();
        foreach (var line in await File.ReadAllLinesAsync(initPath))
        {
            if (line.TrimStart().StartsWith("--") || string.IsNullOrWhiteSpace(line)) continue;
            if (line.StartsWith("DELIMITER ")) { delimiter = line.Substring(10).Trim(); continue; }
            statement.AppendLine(line);
            if (!line.TrimEnd().EndsWith(delimiter)) continue;
            var sql = statement.ToString().TrimEnd();
            await Sql(sql.Substring(0, sql.Length - delimiter.Length));
            statement.Clear();
        }
        if (statement.ToString().Trim().Length != 0) throw new Exception("Unterminated SQL statement");
        async Task<long> Scalar(string sql) { using var cmd = new MySqlCommand(sql,connection); return Convert.ToInt64(await cmd.ExecuteScalarAsync()); }
        foreach (var (table, expected) in new[] { ("regions",4L), ("vendors",3L), ("bts",20L), ("cells",60L), ("metric_catalog",5L), ("incidents",3L), ("pm_counters_hourly",57575L), ("kpi_cell_hourly",11515L) })
            if (await Scalar($"SELECT COUNT(*) FROM {table}") != expected) throw new Exception("Seed count: " + table);
        if (await Scalar("SELECT COUNT(*) FROM information_schema.routines WHERE routine_schema = DATABASE()") != 3) throw new Exception("Missing procedures");
        var count = await Scalar("SELECT COUNT(*) FROM anomalies");
        if (count == 0) throw new Exception("Seed has no anomalies");
        await Sql("CALL sp_refresh_kpi_hourly(TIMESTAMPADD(HOUR,-192,@now_h),TIMESTAMPADD(HOUR,1,@now_h)); CALL sp_detect_anomalies(72); CALL sp_detect_availability_anomalies(TIMESTAMPADD(HOUR,-192,@now_h),TIMESTAMPADD(HOUR,1,@now_h));");
        if (await Scalar("SELECT COUNT(*) FROM anomalies") != count) throw new Exception("Repeated calculation duplicates anomalies");
        Console.WriteLine($"PASS full init.sql: reference data, 20 stations, 60 cells, 57575 PM rows, 11515 KPI rows, 3 procedures, {count} anomalies; repeated calculations stable");
    }
    finally
    {
        await connection.ChangeDatabaseAsync("mysql");
        await Sql($"DROP DATABASE `{seedDatabase}`");
        Console.WriteLine("Removed isolated seed database");
    }
}
await Sql($"CREATE DATABASE `{database}`");
try
{
    await connection.ChangeDatabaseAsync(database);
    await Sql("""
    CREATE TABLE regions(region_id SMALLINT UNSIGNED PRIMARY KEY,code VARCHAR(8),name VARCHAR(128),tz_offset_min SMALLINT);
    CREATE TABLE vendors(vendor_id SMALLINT UNSIGNED PRIMARY KEY,code VARCHAR(32),name VARCHAR(128),rat_supported VARCHAR(64));
    CREATE TABLE bts(bts_id INT UNSIGNED PRIMARY KEY,region_id SMALLINT UNSIGNED,vendor_id SMALLINT UNSIGNED,code VARCHAR(64),address VARCHAR(255),lat DECIMAL(9,6),lon DECIMAL(9,6),site_type VARCHAR(32),status VARCHAR(32),status_since DATETIME);
    CREATE TABLE cells(cell_id INT UNSIGNED PRIMARY KEY,bts_id INT UNSIGNED,local_cell_id SMALLINT UNSIGNED,sector VARCHAR(1));
    CREATE TABLE kpi_cell_hourly(cell_id INT UNSIGNED,ts_hour DATETIME,vendor_id SMALLINT UNSIGNED,conn_attempt BIGINT UNSIGNED,conn_success BIGINT UNSIGNED,conn_drop BIGINT UNSIGNED,traffic_dl_bytes BIGINT UNSIGNED,traffic_ul_bytes BIGINT UNSIGNED,cssr_pct DECIMAL(7,4),dcr_pct DECIMAL(7,4),computed_at DATETIME);
    CREATE TABLE incidents(incident_id BIGINT UNSIGNED PRIMARY KEY,region_id SMALLINT UNSIGNED,bts_id INT UNSIGNED,category VARCHAR(32),severity VARCHAR(32),started_at DATETIME,resolved_at DATETIME,description VARCHAR(500));
    CREATE TABLE anomalies(anomaly_id BIGINT UNSIGNED PRIMARY KEY,cell_id INT UNSIGNED,ts_from DATETIME,ts_to DATETIME,anomaly_type VARCHAR(32),kpi_code VARCHAR(32),severity VARCHAR(32),status VARCHAR(32),explained_by_incident BIGINT UNSIGNED);
    INSERT INTO regions VALUES(1,'SVE','Свердловская область',300),(2,'KGN','Курганская область',300);
    INSERT INTO vendors VALUES(1,'ERICSSON','Ericsson','GSM/LTE');
    INSERT INTO bts VALUES(1,1,1,'SVE-1',NULL,56,60,'macro','active',NULL),(2,2,1,'=FORMULA',NULL,55,61,'macro','down',NULL);
    INSERT INTO cells VALUES(1,1,1,'A'),(2,2,1,'B'),(3,1,2,'C');
    INSERT INTO kpi_cell_hourly VALUES
      (1,'2026-11-01 00:00:00',1,100,90,9,1000000000,0,90,10,'2026-11-01'),
      (1,'2026-11-01 01:00:00',1,900,900,9,2000000000,0,100,1,'2026-11-01'),
      (2,'2026-11-01 00:00:00',1,0,0,0,0,0,NULL,NULL,'2026-11-01'),
      (1,'2026-11-02 00:00:00',1,100,0,99,9999999999,0,0,99,'2026-11-02');
    INSERT INTO incidents VALUES(1,1,1,'power','high','2026-10-31','2026-11-01 00:30:00','Power issue'),(2,2,NULL,'transport','medium','2026-11-01',NULL,'Regional issue');
    INSERT INTO anomalies VALUES
      (1,1,'2026-11-01','2026-11-01 01:00:00','threshold','cssr','warning','new',NULL),
      (2,1,'2026-11-01','2026-11-01 01:00:00','threshold','dcr','critical','explained',1),
      (3,1,'2026-11-01','2026-11-01 01:00:00','threshold','cssr','critical','resolved',NULL),
      (4,1,'2026-11-01','2026-11-01 01:00:00','threshold','cssr','critical','false_positive',NULL);
    """);
    var builder = WebApplication.CreateBuilder();
    builder.Logging.ClearProviders();
    builder.WebHost.UseUrls("http://127.0.0.1:51879");
    builder.Services.AddControllers().AddApplicationPart(typeof(BtsController).Assembly);
    var testCs = new MySqlConnectionStringBuilder(cs) { Database = database }.ConnectionString;
    builder.Services.AddDbContext<MotivReportDbContext>(o => o.UseMySql(testCs, ServerVersion.AutoDetect(testCs)));
    var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes("only-for-isolated-api-checks-12345678901234567890"));
    builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(o =>
        o.TokenValidationParameters = new TokenValidationParameters { ValidateIssuer = false, ValidateAudience = false, IssuerSigningKey = key });
    builder.Services.AddAuthorization();
    await using var app = builder.Build();
    app.Use(async (context, next) => { try { await next(); } catch(Exception ex) { Console.WriteLine(ex); throw; } });
    app.UseAuthentication(); app.UseAuthorization(); app.MapControllers();
    await app.StartAsync();
    using var client = new HttpClient { BaseAddress = new Uri("http://127.0.0.1:51879") };
    const string period = "from=2026-11-01T00:00:00Z&to=2026-11-02T00:00:00Z";
    var routes = new[] { "/api/vendors", "/api/bts", "/api/dashboard/summary?" + period, "/api/incidents?" + period, "/api/reports/export?" + period };
    var checks = 0;
    void Check(bool value, string label) { if (!value) throw new Exception(label); checks++; Console.WriteLine("PASS " + label); }
    foreach (var route in routes) Check((await client.GetAsync(route)).StatusCode == HttpStatusCode.Unauthorized, "JWT required: " + route.Split('?')[0]);
    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(expires: DateTime.UtcNow.AddMinutes(5), signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256))));
    async Task<JsonElement> Json(string route) { var response = await client.GetAsync(route); var content = await response.Content.ReadAsStringAsync(); if (!response.IsSuccessStatusCode) throw new Exception($"{route}: {response.StatusCode} {content}"); return JsonDocument.Parse(content).RootElement.Clone(); }
    var vendors = await Json("/api/vendors");
    Check(vendors.GetArrayLength() == 1 && vendors[0].GetProperty("ratSupported").GetString() == "GSM/LTE", "vendor DTO");
    var summary = await Json("/api/dashboard/summary?" + period);
    Check(summary.GetProperty("avgCssr").GetDecimal() == 99m, "weighted CSSR and exclusive end boundary");
    Check(summary.GetProperty("avgDcr").GetDecimal() > 1.81m && summary.GetProperty("avgDcr").GetDecimal() < 1.82m, "weighted DCR");
    Check(summary.GetProperty("totalTrafficGb").GetDecimal() == 3m && summary.GetProperty("btsProblem").GetInt32() == 1, "traffic and station counts");
    Check(summary.GetProperty("avgAvailability").GetDecimal() > 66.66m && summary.GetProperty("avgAvailability").GetDecimal() < 66.67m, "traffic-based availability");
    Check(summary.GetProperty("anomaliesOpen").GetInt32() == 1 && summary.GetProperty("anomaliesExplained").GetInt32() == 1, "anomaly statuses");
    var map = await Json("/api/bts?regionId=1&" + period);
    Check(map.GetArrayLength() == 1 && map[0].GetProperty("anomalySeverity").GetString() == "critical" && map[0].GetProperty("incidentId").GetUInt64() == 1 && map[0].GetProperty("anomalyCount").GetInt32() == 2, "map filters and representative anomaly");
    var incidents = await Json("/api/incidents?" + period + "&category=power&pageSize=1");
    Check(incidents.GetProperty("total").GetInt32() == 1 && incidents.GetProperty("items")[0].GetProperty("durationMin").GetDouble() == 1470, "incident overlap, category, pagination, duration");
    var regional = await Json("/api/incidents?" + period + "&regionId=2");
    Check(regional.GetProperty("items")[0].GetProperty("btsCode").ValueKind == JsonValueKind.Null, "regional incident without station");
    var csvResponse = await client.GetAsync("/api/reports/export?" + period);
    var csv = await csvResponse.Content.ReadAsStringAsync();
    Check(csvResponse.IsSuccessStatusCode && csvResponse.Content.Headers.ContentType!.MediaType == "text/csv" && csv.Contains("1,\"SVE-1\",\"A\",\"SVE\",99,1.8182,100,3,3"), "CSV executes SQL aggregates and matches KPI");
    Check(csv.Contains("\"'=FORMULA\"") && csv.Contains("3,\"SVE-1\",\"C\",\"SVE\",,,,0,0"), "CSV formula protection and missing-data cells");
    foreach (var route in new[] { "/api/dashboard/summary", "/api/dashboard/summary?from=2026-11-02&to=2026-11-01", "/api/incidents?" + period + "&page=0", "/api/incidents?" + period + "&severity=bad", "/api/bts?from=2026-11-01", "/api/bts?technology=4G", "/api/reports/export?" + period + "&format=xlsx" })
        Check((await client.GetAsync(route)).StatusCode == HttpStatusCode.BadRequest, "validation: " + route);
    var empty = await Json("/api/dashboard/summary?from=2020-01-01&to=2020-01-02&regionId=1");
    Check(empty.GetProperty("avgCssr").ValueKind == JsonValueKind.Null && empty.GetProperty("totalTrafficGb").GetDecimal() == 0, "empty period does not invent KPI");
    await app.StopAsync();
    Console.WriteLine($"{checks} checks passed");
}
finally
{
    await connection.ChangeDatabaseAsync("mysql");
    await Sql($"DROP DATABASE `{database}`");
    Console.WriteLine("Removed isolated test database");
}
