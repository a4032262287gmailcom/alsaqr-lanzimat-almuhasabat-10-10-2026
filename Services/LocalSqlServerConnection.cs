using System.Data;
using System.Text.Json;
using Microsoft.Data.SqlClient;

namespace AlSaqr.Accounting.Services;

internal sealed record LocalSqlServerSettings(string Server, string Database);

internal sealed record LocalSqlServerReport(string Server, string Database, string Version, string Login, DataTable Tables);

internal static class LocalSqlServerConnection
{
    private static readonly string Folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AlSaqrAccounting");
    private static readonly string FilePath = Path.Combine(Folder, "sqlserver-settings.json");

    public static LocalSqlServerSettings Defaults() => new(@" .\SQLEXPRESS".Trim(), "GtsDb2026");

    public static LocalSqlServerSettings Load()
    {
        if (!File.Exists(FilePath)) return Defaults();
        try { return JsonSerializer.Deserialize<LocalSqlServerSettings>(File.ReadAllText(FilePath)) ?? Defaults(); }
        catch (JsonException) { throw new InvalidOperationException("ملف إعداد اتصال SQL Server غير صالح؛ أعد حفظ الإعدادات من شاشة الاتصال."); }
    }

    public static void Save(LocalSqlServerSettings settings)
    {
        Validate(settings);
        Directory.CreateDirectory(Folder);
        var temp = FilePath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(settings));
        File.Move(temp, FilePath, true);
    }

    public static SqlConnection Open(LocalSqlServerSettings settings)
    {
        Validate(settings);
        var builder = new SqlConnectionStringBuilder
        {
            DataSource = settings.Server.Trim(),
            InitialCatalog = settings.Database.Trim(),
            IntegratedSecurity = true,
            MultipleActiveResultSets = true,
            ConnectTimeout = 5,
            Encrypt = true,
            TrustServerCertificate = true,
            ApplicationName = "AlSaqrAccountingDesktop"
        };
        return new SqlConnection(builder.ConnectionString);
    }

    public static LocalSqlServerReport Test(LocalSqlServerSettings settings)
    {
        using var connection = Open(settings);
        connection.Open();
        string server, database, version, login;
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT CONVERT(nvarchar(256),SERVERPROPERTY('ServerName')), DB_NAME(), CONVERT(nvarchar(128),SERVERPROPERTY('ProductVersion')), SUSER_SNAME()";
            using var reader = command.ExecuteReader();
            if (!reader.Read()) throw new InvalidOperationException("تعذر قراءة معلومات خادم SQL Server.");
            server = reader.IsDBNull(0) ? settings.Server : reader.GetString(0);
            database = reader.IsDBNull(1) ? settings.Database : reader.GetString(1);
            version = reader.IsDBNull(2) ? "غير معروف" : reader.GetString(2);
            login = reader.IsDBNull(3) ? "غير معروف" : reader.GetString(3);
        }
        var tables = new DataTable();
        using (var command = connection.CreateCommand())
        {
            command.CommandText = @"
SELECT s.name AS [المخطط], t.name AS [الجدول],
       (SELECT COUNT(*) FROM sys.columns c WHERE c.object_id=t.object_id) AS [عدد الأعمدة]
FROM sys.tables t JOIN sys.schemas s ON s.schema_id=t.schema_id
WHERE t.is_ms_shipped=0 ORDER BY s.name,t.name";
            using var reader = command.ExecuteReader();
            tables.Load(reader);
        }
        return new LocalSqlServerReport(server, database, version, login, tables);
    }

    private static void Validate(LocalSqlServerSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.Server)) throw new ArgumentException("اسم خادم SQL Server مطلوب.");
        if (string.IsNullOrWhiteSpace(settings.Database)) throw new ArgumentException("اسم قاعدة البيانات مطلوب.");
    }
}
