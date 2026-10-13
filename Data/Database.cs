using Microsoft.Data.Sqlite;

namespace AlSaqr.Accounting.Data;

internal static class Database
{
    private static readonly string DataDirectory =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AlSaqrAccounting");
    public static string DatabasePath => Path.Combine(DataDirectory, "accounting.db");

    public static SqliteConnection Open()
    {
        Directory.CreateDirectory(DataDirectory);
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = DatabasePath,
            ForeignKeys = true,
            Pooling = true,
            DefaultTimeout = 30
        }.ToString());
        connection.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "PRAGMA foreign_keys=ON; PRAGMA journal_mode=WAL; PRAGMA busy_timeout=30000;";
        cmd.ExecuteNonQuery();
        return connection;
    }

    public static void BackupTo(string destinationPath)
    {
        if (string.IsNullOrWhiteSpace(destinationPath)) throw new ArgumentException("مسار النسخة الاحتياطية مطلوب.");
        var fullPath = Path.GetFullPath(destinationPath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        using var source = Open();
        using var destination = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = fullPath, ForeignKeys = true }.ToString());
        destination.Open();
        source.BackupDatabase(destination);
    }

    public static void Initialize()
    {
        using var db = Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = @"
CREATE TABLE IF NOT EXISTS Accounts(
  Code TEXT PRIMARY KEY, Name TEXT NOT NULL, Type TEXT NOT NULL CHECK(Type IN ('Asset','Liability','Equity','Revenue','Expense')),
  IsActive INTEGER NOT NULL DEFAULT 1 CHECK(IsActive IN (0,1)), CreatedAt TEXT NOT NULL
);
CREATE TABLE IF NOT EXISTS Parties(
  Id INTEGER PRIMARY KEY AUTOINCREMENT, Name TEXT NOT NULL, Kind TEXT NOT NULL CHECK(Kind IN ('Customer','Supplier','Both')),
  Phone TEXT, VatNumber TEXT, IsActive INTEGER NOT NULL DEFAULT 1, CreatedAt TEXT NOT NULL
);
CREATE TABLE IF NOT EXISTS Products(
  Id INTEGER PRIMARY KEY AUTOINCREMENT, Code TEXT NOT NULL UNIQUE, Name TEXT NOT NULL, Unit TEXT NOT NULL DEFAULT 'حبة',
  Quantity REAL NOT NULL DEFAULT 0 CHECK(Quantity >= 0), CostHalalas INTEGER NOT NULL DEFAULT 0 CHECK(CostHalalas >= 0),
  PriceHalalas INTEGER NOT NULL DEFAULT 0 CHECK(PriceHalalas >= 0), VatRate REAL NOT NULL DEFAULT 15 CHECK(VatRate >= 0 AND VatRate <= 100),
  IsActive INTEGER NOT NULL DEFAULT 1, CreatedAt TEXT NOT NULL
);
CREATE TABLE IF NOT EXISTS Journals(
  Id INTEGER PRIMARY KEY AUTOINCREMENT, Number TEXT NOT NULL UNIQUE, EntryDate TEXT NOT NULL,
  Memo TEXT NOT NULL, Source TEXT NOT NULL, Status TEXT NOT NULL CHECK(Status='POSTED'), CreatedAt TEXT NOT NULL
);
CREATE TABLE IF NOT EXISTS JournalLines(
  Id INTEGER PRIMARY KEY AUTOINCREMENT, JournalId INTEGER NOT NULL REFERENCES Journals(Id) ON DELETE RESTRICT,
  AccountCode TEXT NOT NULL REFERENCES Accounts(Code) ON DELETE RESTRICT,
  Memo TEXT NOT NULL DEFAULT '', DebitHalalas INTEGER NOT NULL DEFAULT 0 CHECK(DebitHalalas >= 0),
  CreditHalalas INTEGER NOT NULL DEFAULT 0 CHECK(CreditHalalas >= 0),
  CHECK((DebitHalalas > 0 AND CreditHalalas = 0) OR (CreditHalalas > 0 AND DebitHalalas = 0))
);
CREATE INDEX IF NOT EXISTS IX_Journals_EntryDate ON Journals(EntryDate);
CREATE INDEX IF NOT EXISTS IX_JournalLines_AccountCode ON JournalLines(AccountCode);
CREATE TABLE IF NOT EXISTS Invoices(
  Id INTEGER PRIMARY KEY AUTOINCREMENT, Kind TEXT NOT NULL CHECK(Kind IN ('SALE','PURCHASE')),
  Number TEXT NOT NULL, InvoiceDate TEXT NOT NULL, PartyId INTEGER NOT NULL REFERENCES Parties(Id),
  IsCredit INTEGER NOT NULL CHECK(IsCredit IN (0,1)), Status TEXT NOT NULL CHECK(Status='POSTED'),
  SubtotalHalalas INTEGER NOT NULL, VatHalalas INTEGER NOT NULL, TotalHalalas INTEGER NOT NULL,
  JournalId INTEGER NOT NULL REFERENCES Journals(Id), CreatedAt TEXT NOT NULL,
  UNIQUE(Kind, Number)
);
CREATE TABLE IF NOT EXISTS InvoiceLines(
  Id INTEGER PRIMARY KEY AUTOINCREMENT, InvoiceId INTEGER NOT NULL REFERENCES Invoices(Id) ON DELETE RESTRICT,
  ProductId INTEGER NOT NULL REFERENCES Products(Id), Quantity REAL NOT NULL CHECK(Quantity > 0),
  UnitPriceHalalas INTEGER NOT NULL, CostAtPostingHalalas INTEGER NOT NULL,
  VatRate REAL NOT NULL, NetHalalas INTEGER NOT NULL, VatHalalas INTEGER NOT NULL, TotalHalalas INTEGER NOT NULL
);
CREATE INDEX IF NOT EXISTS IX_Invoices_DateKind ON Invoices(InvoiceDate,Kind);
CREATE TABLE IF NOT EXISTS StockMovements(
  Id INTEGER PRIMARY KEY AUTOINCREMENT, MovementDate TEXT NOT NULL,
  ProductId INTEGER NOT NULL REFERENCES Products(Id), Source TEXT NOT NULL, SourceId TEXT NOT NULL,
  QuantityIn REAL NOT NULL DEFAULT 0 CHECK(QuantityIn >= 0), QuantityOut REAL NOT NULL DEFAULT 0 CHECK(QuantityOut >= 0),
  UnitCostHalalas INTEGER NOT NULL DEFAULT 0 CHECK(UnitCostHalalas >= 0), Memo TEXT NOT NULL DEFAULT '', CreatedAt TEXT NOT NULL,
  CHECK((QuantityIn > 0 AND QuantityOut = 0) OR (QuantityOut > 0 AND QuantityIn = 0))
);
CREATE INDEX IF NOT EXISTS IX_StockMovements_DateProduct ON StockMovements(MovementDate,ProductId);
CREATE TABLE IF NOT EXISTS Settlements(
  Id INTEGER PRIMARY KEY AUTOINCREMENT, Kind TEXT NOT NULL CHECK(Kind IN ('RECEIPT','PAYMENT')),
  Number TEXT NOT NULL UNIQUE, SettlementDate TEXT NOT NULL,
  PartyId INTEGER NOT NULL REFERENCES Parties(Id), AmountHalalas INTEGER NOT NULL CHECK(AmountHalalas > 0),
  CashAccountCode TEXT NOT NULL REFERENCES Accounts(Code), JournalId INTEGER NOT NULL REFERENCES Journals(Id),
  Memo TEXT NOT NULL, CreatedAt TEXT NOT NULL
);
CREATE INDEX IF NOT EXISTS IX_Settlements_DateKind ON Settlements(SettlementDate,Kind);
CREATE TABLE IF NOT EXISTS Users(
  Id INTEGER PRIMARY KEY AUTOINCREMENT, UserName TEXT NOT NULL UNIQUE COLLATE NOCASE,
  PasswordSalt TEXT NOT NULL, PasswordHash TEXT NOT NULL,
  Role TEXT NOT NULL CHECK(Role IN ('Admin','Accountant','ReadOnly')),
  IsActive INTEGER NOT NULL DEFAULT 1 CHECK(IsActive IN (0,1)), CreatedAt TEXT NOT NULL
);
CREATE TABLE IF NOT EXISTS AuditLog(
  Id INTEGER PRIMARY KEY AUTOINCREMENT, EventAt TEXT NOT NULL, Actor TEXT NOT NULL,
  Action TEXT NOT NULL, Entity TEXT NOT NULL, EntityId TEXT NOT NULL, Details TEXT NOT NULL
);
";
        cmd.ExecuteNonQuery();

        using var transaction = db.BeginTransaction();
        var accounts = new (string Code, string Name, string Type)[]
        {
            ("1010","الصندوق","Asset"), ("1020","البنك","Asset"), ("1100","العملاء","Asset"),
            ("1200","المخزون","Asset"), ("1300","ضريبة القيمة المضافة - مدخلات","Asset"),
            ("2100","الموردون","Liability"), ("2200","ضريبة القيمة المضافة - مخرجات","Liability"),
            ("3000","رأس المال","Equity"), ("3100","الأرباح المبقاة","Equity"),
            ("4000","إيرادات المبيعات","Revenue"), ("5000","تكلفة البضاعة المباعة","Expense"),
            ("6000","المصروفات التشغيلية","Expense")
        };
        foreach (var account in accounts)
        {
            using var insert = db.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = "INSERT OR IGNORE INTO Accounts(Code,Name,Type,IsActive,CreatedAt) VALUES($code,$name,$type,1,$at)";
            insert.Parameters.AddWithValue("$code", account.Code);
            insert.Parameters.AddWithValue("$name", account.Name);
            insert.Parameters.AddWithValue("$type", account.Type);
            insert.Parameters.AddWithValue("$at", DateTime.UtcNow.ToString("O"));
            insert.ExecuteNonQuery();
        }
        transaction.Commit();
    }
}
