using MotivCellScopeApiServices.Contracts;
using MySqlConnector;

namespace MotivCellScopeApiServices.Services;
public static class JournalBootstrap
{
    public static async Task Seed(IConfiguration config)
    {
        // compose restart не ждёт healthcheck зависимостей, в отличие от up.
        // Повторяем только ошибки подключения, не ошибки SQL или паролей.
        for (int attempt = 0;; attempt++)
        {
            try
            {
                await SeedOnce(config);
                return;
            }
            catch (MySqlException error)when (attempt < 14 && error.Number is 0 or 1042 or 2002 or 2003)
            {
                await Task.Delay(TimeSpan.FromSeconds(2));
            }
        }
    }

    private static async Task SeedOnce(IConfiguration config)
    {
        if (!config.GetValue<bool>("AccidentJournal:SeedDemo"))
            return;
        string password = config["DemoPassword"] ?? throw new InvalidOperationException("Для демо задайте DemoPassword.");
        if (password.Length < 8 || System.Text.Encoding.UTF8.GetByteCount(password) > 72)
            throw new InvalidOperationException("DemoPassword: от 8 символов, до 72 байт UTF-8.");
        await using var db = new MySqlConnection(config.GetConnectionString("Journal"));
        await db.OpenAsync();
        // Один bootstrap за раз; блокировка освобождается при закрытии соединения.
        using var gate = new MySqlCommand("SELECT GET_LOCK('motiv_journal_seed',30)", db);
        if (Convert.ToInt32(await gate.ExecuteScalarAsync()) != 1)
            throw new InvalidOperationException("Не удалось получить блокировку демо-инициализации.");
        await using var tx = await db.BeginTransactionAsync();
        using var count = new MySqlCommand("SELECT COUNT(*) FROM users", db, tx);
        bool fresh = Convert.ToInt32(await count.ExecuteScalarAsync()) == 0;
        foreach (var(name, role)in new[]
        {
            ("admin", "admin"),
            ("duty", "duty"),
            ("engineer", "engineer")
        }

        )
        {
            using var existing = new MySqlCommand("SELECT user_id FROM users WHERE username=@name", db, tx);
            existing.Parameters.AddWithValue("@name", name);
            var value = await existing.ExecuteScalarAsync();
            if (value != null)
                continue; // Никогда не перезаписываем пароль или права существующего пользователя.
            using var insert = new MySqlCommand("INSERT INTO users(username,password_hash) VALUES(@name,@hash)", db, tx);
            insert.Parameters.AddWithValue("@name", name);
            insert.Parameters.AddWithValue("@hash", BCrypt.Net.BCrypt.HashPassword(password, workFactor: 11));
            await insert.ExecuteNonQueryAsync();
            using var access = new MySqlCommand("INSERT INTO journal_user_access(user_id,role) VALUES(@id,@role)", db, tx);
            access.Parameters.AddWithValue("@id", insert.LastInsertedId);
            access.Parameters.AddWithValue("@role", role);
            await access.ExecuteNonQueryAsync();
        }

        if (fresh)
        {
            using var seed = new MySqlCommand("""
                INSERT INTO accident_reports(PW,NAPR,TEHC,KATEG,NETW_TYPE,SERV_ELEM,NUMB_RBS,EQIP_TYPE,ALARM_TYPE,DESCR,BEGIN_TIME,END_TIME,AKB,DGU,LENGHT,PROSTOY,COMMON_PROSTOY,ALARM_WHO,DESC_NOTICE,created_by)
                SELECT '', 'СВН','ЕКБ',1,'LTE','БС ЕКБ-001 · ул. Малышева, 51',3,'Резервное питание','ЭЛ','Отключение основного электропитания',UTC_TIMESTAMP()-INTERVAL 4 HOUR,'0000-00-00 00:00:00','01:30:00','02:00:00','04:00:00','00:20:00','01:00:00','Дежурный инженер','Ожидаем восстановление сети',user_id FROM users WHERE username='admin';
                INSERT INTO accident_reports(NAPR,TEHC,KATEG,NETW_TYPE,SERV_ELEM,NUMB_RBS,ALARM_TYPE,DESCR,BEGIN_TIME,END_TIME,LENGHT,PROSTOY,COMMON_PROSTOY,created_by)
                SELECT 'ХМЯН','СРГ',2,'LTE/GSM','БС СРГ-014 · Сургут',2,'РРС','Восстановлен радиорелейный пролёт',UTC_TIMESTAMP()-INTERVAL 1 DAY,UTC_TIMESTAMP()-INTERVAL 22 HOUR,'02:00:00','01:15:00','02:30:00',user_id FROM users WHERE username='admin';
                INSERT INTO accident_reports(NAPR,TEHC,KATEG,NETW_TYPE,SERV_ELEM,NUMB_RBS,ALARM_TYPE,DESCR,BEGIN_TIME,LENGHT,PROSTOY,created_by)
                SELECT 'ЮЗН','КРГ',3,'GSM','Сектор B · БС КРГ-008',-1,'RBS','Деградация одного сектора',UTC_TIMESTAMP()-INTERVAL 1 HOUR,'01:00:00','00:10:00',user_id FROM users WHERE username='admin';
                """, db, tx);
            await seed.ExecuteNonQueryAsync();
        }

        await tx.CommitAsync();
    }
}
