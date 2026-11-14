using System.Reflection;
using MotivCellScopeApiServices.Contracts;
using MySqlConnector;

namespace MotivCellScopeApiServices.Services;
public static class JournalSchema
{
    // DDL MariaDB не транзакционный. Каждый шаг повторяем; маркер ставим последним.
    // Старую таблицу оставляем как резервную копию, не удаляем данные и тома Docker.
    public static async Task Ensure(IConfiguration config)
    {
        for (int attempt = 0;; attempt++)
        {
            try
            {
                await EnsureOnce(config);
                return;
            }
            catch (MySqlException e)when (attempt < 14 && e.Number is 0 or 1042 or 2002 or 2003)
            {
                await Task.Delay(2000);
            }
        }
    }

    private static async Task EnsureOnce(IConfiguration config)
    {
        await using var db = new MySqlConnection(config.GetConnectionString("Journal"));
        await db.OpenAsync();
        using var gate = new MySqlCommand("SELECT GET_LOCK('motiv_journal_schema',30)", db);
        if (Convert.ToInt32(await gate.ExecuteScalarAsync()) != 1)
            throw new InvalidOperationException("Схема занята другой миграцией.");
        await using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("MotivCellScopeApiServices.JournalSchema.sql")!;
        using var reader = new StreamReader(stream);
        string sql = await reader.ReadToEndAsync();
        using (var schema = new MySqlCommand(sql, db))
            await schema.ExecuteNonQueryAsync();
        foreach (var(table, values)in new[]
        {
            ("journal_directions", JournalOptions.Directions),
            ("journal_centers", JournalOptions.Centers),
            ("journal_network_types", new[] { "GSM", "LTE" }),
            ("journal_alarm_types", JournalOptions.AlarmTypes)
        }

        )
        {
            foreach (var code in values)
            {
                using var seed = new MySqlCommand($"INSERT IGNORE INTO {table}(code,label) VALUES(@code,@code)", db);
                seed.Parameters.AddWithValue("@code", code);
                await seed.ExecuteNonQueryAsync();
            }
        }

        using (var version = new MySqlCommand("CREATE TABLE IF NOT EXISTS journal_schema_versions(version INT PRIMARY KEY,applied_at DATETIME NOT NULL)", db))
            await version.ExecuteNonQueryAsync();
        using var applied = new MySqlCommand("SELECT COUNT(*) FROM journal_schema_versions WHERE version=2", db);
        if (Convert.ToInt32(await applied.ExecuteScalarAsync()) != 0)
            return;
        using var old = new MySqlCommand("SELECT COUNT(*) FROM information_schema.tables WHERE table_schema=DATABASE() AND table_name='accident_reports' AND table_type='BASE TABLE'", db);
        if (Convert.ToInt32(await old.ExecuteScalarAsync()) != 0)
        {
            await using var tx = await db.BeginTransactionAsync();
            const string migration = """
                INSERT IGNORE INTO journal_directions(code,label) SELECT DISTINCT NAPR,NAPR FROM accident_reports;
                INSERT IGNORE INTO journal_centers(code,label) SELECT DISTINCT TEHC,TEHC FROM accident_reports WHERE TEHC<>'';
                INSERT IGNORE INTO journal_alarm_types(code,label) SELECT DISTINCT ALARM_TYPE,ALARM_TYPE FROM accident_reports;
                INSERT IGNORE INTO journal_network_types(code,label) SELECT DISTINCT NETW_TYPE,NETW_TYPE FROM accident_reports WHERE NETW_TYPE NOT IN('LTE/GSM','GSM/LTE');
                INSERT IGNORE INTO journal_service_elements(label,label_hash) SELECT DISTINCT SERV_ELEM,UNHEX(SHA2(SERV_ELEM,256)) FROM accident_reports;
                INSERT IGNORE INTO journal_tickets(label,label_hash) SELECT DISTINCT PW,UNHEX(SHA2(PW,256)) FROM accident_reports WHERE PW<>'';
                INSERT IGNORE INTO journal_equipment_models(label,label_hash) SELECT DISTINCT EQIP_TYPE,UNHEX(SHA2(EQIP_TYPE,256)) FROM accident_reports WHERE EQIP_TYPE<>'';
                INSERT INTO journal_accidents(id,direction_id,center_id,category_id,service_element_id,ticket_id,alarm_type_id,description,begin_at,end_at,station_count,is_sector,battery_seconds,generator_seconds,downtime_seconds,notified,notes,created_by,version)
                SELECT o.id,d.id,c.id,k.id,s.id,t.id,al.id,o.DESCR,o.BEGIN_TIME,NULLIF(o.END_TIME,'0000-00-00 00:00:00'),NULLIF(o.NUMB_RBS,-1),o.NUMB_RBS=-1,TIME_TO_SEC(o.AKB),TIME_TO_SEC(o.DGU),TIME_TO_SEC(o.PROSTOY),o.ALARM_WHO,o.DESC_NOTICE,o.created_by,o.version
                FROM accident_reports o JOIN journal_directions d ON BINARY d.code=BINARY o.NAPR LEFT JOIN journal_centers c ON BINARY c.code=BINARY o.TEHC
                JOIN journal_categories k ON k.code=CAST(o.KATEG AS CHAR) JOIN journal_service_elements s ON s.label_hash=UNHEX(SHA2(o.SERV_ELEM,256))
                LEFT JOIN journal_tickets t ON t.label_hash=UNHEX(SHA2(o.PW,256)) JOIN journal_alarm_types al ON BINARY al.code=BINARY o.ALARM_TYPE
                WHERE NOT EXISTS(SELECT 1 FROM journal_accidents a WHERE a.id=o.id);
                INSERT IGNORE INTO journal_accident_networks(accident_id,network_type_id)
                SELECT o.id,n.id FROM accident_reports o JOIN journal_network_types n ON n.code=o.NETW_TYPE OR (o.NETW_TYPE IN('LTE/GSM','GSM/LTE') AND n.code IN('LTE','GSM'));
                INSERT IGNORE INTO journal_accident_equipment(accident_id,equipment_model_id)
                SELECT o.id,e.id FROM accident_reports o JOIN journal_equipment_models e ON e.label_hash=UNHEX(SHA2(o.EQIP_TYPE,256)) WHERE o.EQIP_TYPE<>'';
                INSERT INTO journal_schema_versions VALUES(2,UTC_TIMESTAMP());
                """;
            using var migrate = new MySqlCommand(migration, db, tx);
            await migrate.ExecuteNonQueryAsync();
            await tx.CommitAsync();
        }
        else
        {
            using var mark = new MySqlCommand("INSERT INTO journal_schema_versions VALUES(2,UTC_TIMESTAMP())", db);
            await mark.ExecuteNonQueryAsync();
        }
    }
}
