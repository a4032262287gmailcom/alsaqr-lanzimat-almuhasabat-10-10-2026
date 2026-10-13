using System.Data;
using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using AlSaqr.Accounting.Data;

namespace AlSaqr.Accounting.Services;

internal sealed record AuthUser(int Id, string UserName, string Role);

internal sealed class AuthService
{
    private const int Iterations = 210_000;
    private const int HashBytes = 32;

    public bool HasUsers()
    {
        using var db = Database.Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = "SELECT EXISTS(SELECT 1 FROM Users)";
        return Convert.ToInt64(cmd.ExecuteScalar()) == 1;
    }

    public void CreateFirstAdmin(string userName, string password)
    {
        userName = (userName ?? "").Trim();
        Validate(userName, password);
        using var db = Database.Open();
        using var tx = db.BeginTransaction();
        using (var check = db.CreateCommand())
        {
            check.Transaction = tx;
            check.CommandText = "SELECT COUNT(*) FROM Users";
            if (Convert.ToInt64(check.ExecuteScalar()) != 0)
                throw new InvalidOperationException("تم إنشاء المدير الأول بالفعل.");
        }
        InsertUser(db, tx, userName, password, "Admin");
        Audit(db, tx, userName, "إنشاء المدير الأول");
        tx.Commit();
    }

    public AuthUser? Authenticate(string userName, string password)
    {
        using var db = Database.Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = "SELECT Id,UserName,PasswordSalt,PasswordHash,Role,IsActive FROM Users WHERE UserName=$name COLLATE NOCASE";
        cmd.Parameters.AddWithValue("$name", (userName ?? "").Trim());
        using var reader = cmd.ExecuteReader();
        if (!reader.Read() || reader.GetInt64(5) != 1) return null;
        var salt = Convert.FromBase64String(reader.GetString(2));
        var storedHash = Convert.FromBase64String(reader.GetString(3));
        var actualHash = Rfc2898DeriveBytes.Pbkdf2(password ?? "", salt, Iterations, HashAlgorithmName.SHA256, HashBytes);
        if (!CryptographicOperations.FixedTimeEquals(storedHash, actualHash)) return null;
        var user = new AuthUser(reader.GetInt32(0), reader.GetString(1), reader.GetString(4));
        reader.Close();
        using var audit = db.CreateCommand();
        audit.CommandText = "INSERT INTO AuditLog(EventAt,Actor,Action,Entity,EntityId,Details) VALUES($at,$actor,'دخول','Session',$id,'تسجيل دخول ناجح')";
        audit.Parameters.AddWithValue("$at", DateTime.UtcNow.ToString("O"));
        audit.Parameters.AddWithValue("$actor", user.UserName);
        audit.Parameters.AddWithValue("$id", user.Id.ToString());
        audit.ExecuteNonQuery();
        return user;
    }

    public DataTable Users()
    {
        using var db = Database.Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = @"SELECT Id AS 'المعرف',UserName AS 'اسم المستخدم',
CASE Role WHEN 'Admin' THEN 'مدير النظام' WHEN 'Accountant' THEN 'محاسب' ELSE 'عرض فقط' END AS 'الدور',
CASE IsActive WHEN 1 THEN 'نشط' ELSE 'موقوف' END AS 'الحالة',CreatedAt AS 'تاريخ الإنشاء'
FROM Users ORDER BY UserName";
        using var reader = cmd.ExecuteReader();
        var table = new DataTable(); table.Load(reader); return table;
    }

    public void CreateUser(string userName, string password, string role)
    {
        userName = (userName ?? "").Trim();
        Validate(userName, password);
        if (!new[] { "Admin", "Accountant", "ReadOnly" }.Contains(role))
            throw new ArgumentException("الدور غير صالح.");
        using var db = Database.Open();
        using var tx = db.BeginTransaction();
        InsertUser(db, tx, userName, password, role);
        Audit(db, tx, userName, "إنشاء مستخدم بالدور " + role);
        tx.Commit();
    }

    public void SetActive(int id, bool active, string actor)
    {
        using var db = Database.Open();
        using var tx = db.BeginTransaction();
        string role;
        long currentActive;
        using (var check = db.CreateCommand())
        {
            check.Transaction = tx;
            check.CommandText = "SELECT Role,IsActive FROM Users WHERE Id=$id";
            check.Parameters.AddWithValue("$id", id);
            using var reader = check.ExecuteReader();
            if (!reader.Read()) throw new InvalidOperationException("المستخدم غير موجود.");
            role = reader.GetString(0);
            currentActive = reader.GetInt64(1);
        }
        if (!active && role == "Admin" && currentActive == 1)
        {
            using var count = db.CreateCommand();
            count.Transaction = tx;
            count.CommandText = "SELECT COUNT(*) FROM Users WHERE Role='Admin' AND IsActive=1";
            if (Convert.ToInt64(count.ExecuteScalar()) <= 1)
                throw new InvalidOperationException("لا يمكن إيقاف آخر مدير نشط في النظام.");
        }
        using (var cmd = db.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "UPDATE Users SET IsActive=$active WHERE Id=$id";
            cmd.Parameters.AddWithValue("$active", active ? 1 : 0);
            cmd.Parameters.AddWithValue("$id", id);
            if (cmd.ExecuteNonQuery() != 1) throw new InvalidOperationException("المستخدم غير موجود.");
        }
        using (var audit = db.CreateCommand())
        {
            audit.Transaction = tx;
            audit.CommandText = "INSERT INTO AuditLog(EventAt,Actor,Action,Entity,EntityId,Details) VALUES($at,$actor,$action,'User',$id,$details)";
            audit.Parameters.AddWithValue("$at", DateTime.UtcNow.ToString("O"));
            audit.Parameters.AddWithValue("$actor", actor);
            audit.Parameters.AddWithValue("$action", active ? "تفعيل" : "إيقاف");
            audit.Parameters.AddWithValue("$id", id.ToString());
            audit.Parameters.AddWithValue("$details", "تغيير حالة حساب المستخدم");
            audit.ExecuteNonQuery();
        }
        tx.Commit();
    }

    private static void InsertUser(SqliteConnection db, SqliteTransaction tx, string userName, string password, string role)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, HashBytes);
        using var cmd = db.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "INSERT INTO Users(UserName,PasswordSalt,PasswordHash,Role,IsActive,CreatedAt) VALUES($name,$salt,$hash,$role,1,$at)";
        cmd.Parameters.AddWithValue("$name", userName);
        cmd.Parameters.AddWithValue("$salt", Convert.ToBase64String(salt));
        cmd.Parameters.AddWithValue("$hash", Convert.ToBase64String(hash));
        cmd.Parameters.AddWithValue("$role", role);
        cmd.Parameters.AddWithValue("$at", DateTime.UtcNow.ToString("O"));
        try { cmd.ExecuteNonQuery(); }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 19) { throw new InvalidOperationException("اسم المستخدم موجود مسبقًا.", ex); }
    }

    private static void Validate(string userName, string password)
    {
        if (userName.Length is < 3 or > 80) throw new ArgumentException("اسم المستخدم يجب أن يكون بين 3 و80 حرفًا.");
        if (string.IsNullOrWhiteSpace(password) || password.Length < 10)
            throw new ArgumentException("كلمة المرور مطلوبة ويجب ألا تقل عن 10 أحرف.");
    }

    private static void Audit(SqliteConnection db, SqliteTransaction tx, string actor, string details)
    {
        using var cmd = db.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "INSERT INTO AuditLog(EventAt,Actor,Action,Entity,EntityId,Details) VALUES($at,$actor,'إنشاء','User','1',$details)";
        cmd.Parameters.AddWithValue("$at", DateTime.UtcNow.ToString("O"));
        cmd.Parameters.AddWithValue("$actor", actor);
        cmd.Parameters.AddWithValue("$details", details);
        cmd.ExecuteNonQuery();
    }
}
