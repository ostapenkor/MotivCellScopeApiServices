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

        await tx.CommitAsync();
        if (fresh)
        {
            using var find = new MySqlCommand("SELECT user_id FROM users WHERE username='admin'", db);
            int actor = Convert.ToInt32(await find.ExecuteScalarAsync());
            var store = new MotivCellScopeApiServices.Data.AccidentStore(config);
            await store.Save(0, new AccidentInput { Direction = "СВН", Center = "ЕКБ", Category = 1, NetworkType = "LTE", ServiceElement = "Демонстрационная БС 001", StationCount = 3, AlarmType = "ЭЛ", Description = "Учебный пример: отключение питания", BeginTime = DateTimeOffset.UtcNow.AddHours(-4), BatterySeconds = 5400, GeneratorSeconds = 7200, DowntimeSeconds = 1200, Notes = "Демо; можно удалить администратору." }, actor, default);
        }
    }
}
