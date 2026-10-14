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

    public static LocalSqlServerSettings Defaults() => new(@".\SQLEXPRESS", "GtsDb2026");

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
       c.column_id AS [ترتيب العمود], c.name AS [العمود],
       ty.name AS [النوع], c.max_length AS [الطول بالبايت],
       CASE WHEN c.is_nullable=1 THEN N'نعم' ELSE N'لا' END AS [يقبل NULL],
       CASE WHEN c.is_identity=1 THEN N'نعم' ELSE N'لا' END AS [Identity]
FROM sys.tables t
JOIN sys.schemas s ON s.schema_id=t.schema_id
JOIN sys.columns c ON c.object_id=t.object_id
JOIN sys.types ty ON ty.user_type_id=c.user_type_id
WHERE t.is_ms_shipped=0
ORDER BY s.name,t.name,c.column_id";
            using var reader = command.ExecuteReader();
            tables.Load(reader);
        }
        return new LocalSqlServerReport(server, database, version, login, tables);
    }

    public static DataTable ReadTablePreview(LocalSqlServerSettings settings, string schema, string table, int maxRows = 200)
    {
        Validate(settings);
        schema = (schema ?? string.Empty).Trim();
        table = (table ?? string.Empty).Trim();
        if (schema.Length == 0 || table.Length == 0)
            throw new ArgumentException("اختر المخطط والجدول من القائمة أولًا.");
        if (maxRows is < 1 or > 2000)
            throw new ArgumentOutOfRangeException(nameof(maxRows), "عدد الصفوف يجب أن يكون بين 1 و2000.");

        using var connection = Open(settings);
        connection.Open();

        // Verify the object is an actual user table in the selected database before using its identifiers.
        using (var exists = connection.CreateCommand())
        {
            exists.CommandText = @"
SELECT COUNT(1)
FROM sys.tables t
JOIN sys.schemas s ON s.schema_id=t.schema_id
WHERE t.is_ms_shipped=0 AND s.name=@schema AND t.name=@table";
            exists.Parameters.AddWithValue("@schema", schema);
            exists.Parameters.AddWithValue("@table", table);
            if (Convert.ToInt32(exists.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture) != 1)
                throw new InvalidOperationException("الجدول المحدد غير موجود ضمن جداول المستخدم في قاعدة البيانات الحالية.");
        }

        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT TOP ({maxRows.ToString(System.Globalization.CultureInfo.InvariantCulture)}) * FROM {QuoteIdentifier(schema)}.{QuoteIdentifier(table)}";
        using var reader = command.ExecuteReader();
        var result = new DataTable();
        result.Load(reader);
        return result;
    }

    private static string QuoteIdentifier(string value) => "[" + value.Replace("]", "]]") + "]";

    private static void Validate(LocalSqlServerSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.Server)) throw new ArgumentException("اسم خادم SQL Server مطلوب.");
        if (string.IsNullOrWhiteSpace(settings.Database)) throw new ArgumentException("اسم قاعدة البيانات مطلوب.");
    }
}
