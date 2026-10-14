using System.Data;
using System.Globalization;
using Microsoft.Data.Sqlite;
using AlSaqr.Accounting.Data;

namespace AlSaqr.Accounting.Services;

internal sealed record CashierProduct(
    int Id, string Code, string Name, string Unit, decimal Quantity,
    decimal Cost, decimal Price, decimal VatRate, string Barcode);

internal sealed record CashierCustomer(int Id, string Name);

internal sealed record CashierSessionSummary(
    int Id, string CashierName, DateTimeOffset OpenedAtUtc, DateTimeOffset? ClosedAtUtc,
    decimal OpeningCash, decimal CashSales, decimal ExpectedCash,
    decimal? CountedCash, decimal? Difference, int InvoiceCount, bool IsOpen);

internal sealed record CashReceiptLine(
    string Code, string Name, string Unit, decimal Quantity, decimal UnitPrice,
    decimal Net, decimal Vat, decimal Total);

internal sealed record CashReceipt(
    long Id, string Number, DateTime InvoiceDate, string CustomerName, string PaymentMethod,
    decimal Subtotal, decimal Discount, decimal DiscountPercent, decimal Vat, decimal Total,
    decimal AmountTendered, decimal Change, IReadOnlyList<CashReceiptLine> Lines);

internal sealed class CashierService
{
    private readonly AccountingService _accounting = new();

    private static string Now() => DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);
    private static string Date(DateTime value) => value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    public List<CashierCustomer> Customers()
    {
        using var db = Database.Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = @"
SELECT Id,Name FROM Parties
WHERE IsActive=1 AND Kind IN ('Customer','Both')
ORDER BY CASE WHEN Name='عميل نقدي - الكاشير' THEN 0 ELSE 1 END, Name";
        using var reader = cmd.ExecuteReader();
        var result = new List<CashierCustomer>();
        while (reader.Read())
            result.Add(new CashierCustomer(reader.GetInt32(0), reader.GetString(1)));
        return result;
    }

    public int EnsureCashCustomer()
    {
        using var db = Database.Open();
        using var tx = db.BeginTransaction();
        using (var find = db.CreateCommand())
        {
            find.Transaction = tx;
            find.CommandText = "SELECT Id FROM Parties WHERE Name='عميل نقدي - الكاشير' AND Kind IN ('Customer','Both') AND IsActive=1 ORDER BY Id LIMIT 1";
            var found = find.ExecuteScalar();
            if (found is not null && found != DBNull.Value)
            {
                tx.Commit();
                return Convert.ToInt32(found, CultureInfo.InvariantCulture);
            }
        }

        using var insert = db.CreateCommand();
        insert.Transaction = tx;
        insert.CommandText = "INSERT INTO Parties(Name,Kind,Phone,VatNumber,IsActive,CreatedAt) VALUES('عميل نقدي - الكاشير','Customer',NULL,NULL,1,$at)";
        insert.Parameters.AddWithValue("$at", Now());
        insert.ExecuteNonQuery();
        int id;
        using (var lastId = db.CreateCommand())
        {
            lastId.Transaction = tx;
            lastId.CommandText = "SELECT last_insert_rowid()";
            id = Convert.ToInt32(lastId.ExecuteScalar(), CultureInfo.InvariantCulture);
        }
        using (var audit = db.CreateCommand())
        {
            audit.Transaction = tx;
            audit.CommandText = "INSERT INTO AuditLog(EventAt,Actor,Action,Entity,EntityId,Details) VALUES($at,'system','إنشاء','Party',$id,'إنشاء عميل نقدي افتراضي للكاشير')";
            audit.Parameters.AddWithValue("$at", Now());
            audit.Parameters.AddWithValue("$id", id.ToString(CultureInfo.InvariantCulture));
            audit.ExecuteNonQuery();
        }
        tx.Commit();
        return id;
    }

    public List<CashierProduct> SearchProducts(string query)
    {
        using var db = Database.Open();
        using var cmd = db.CreateCommand();
        var term = (query ?? string.Empty).Trim();
        cmd.CommandText = @"
SELECT Id,Code,Name,Unit,Quantity,CostHalalas,PriceHalalas,VatRate,COALESCE(Barcode,'')
FROM Products
WHERE IsActive=1 AND (
  $term='' OR Code LIKE $like COLLATE NOCASE OR Name LIKE $like COLLATE NOCASE
  OR Barcode LIKE $like COLLATE NOCASE)
ORDER BY CASE WHEN Code=$term COLLATE NOCASE THEN 0
              WHEN Barcode=$term COLLATE NOCASE THEN 0 ELSE 1 END, Code
LIMIT 150";
        cmd.Parameters.AddWithValue("$term", term);
        cmd.Parameters.AddWithValue("$like", "%" + term + "%");
        using var reader = cmd.ExecuteReader();
        var result = new List<CashierProduct>();
        while (reader.Read()) result.Add(ReadProduct(reader));
        return result;
    }

    public CashierProduct? FindProductByScan(string scan)
    {
        var value = (scan ?? string.Empty).Trim();
        if (value.Length == 0) return null;
        using var db = Database.Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = @"
SELECT Id,Code,Name,Unit,Quantity,CostHalalas,PriceHalalas,VatRate,COALESCE(Barcode,'')
FROM Products
WHERE IsActive=1 AND (Barcode=$scan COLLATE NOCASE OR Code=$scan COLLATE NOCASE)
ORDER BY CASE WHEN Barcode=$scan COLLATE NOCASE THEN 0 ELSE 1 END
LIMIT 1";
        cmd.Parameters.AddWithValue("$scan", value);
        using var reader = cmd.ExecuteReader();
        return reader.Read() ? ReadProduct(reader) : null;
    }

    private static CashierProduct ReadProduct(SqliteDataReader reader) => new(
        reader.GetInt32(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
        Convert.ToDecimal(reader.GetDouble(4), CultureInfo.InvariantCulture),
        AccountingService.FromHalalas(reader.GetInt64(5)),
        AccountingService.FromHalalas(reader.GetInt64(6)),
        Convert.ToDecimal(reader.GetDouble(7), CultureInfo.InvariantCulture),
        reader.GetString(8));

    public string GetBarcode(int productId)
    {
        using var db = Database.Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = "SELECT COALESCE(Barcode,'') FROM Products WHERE Id=$id";
        cmd.Parameters.AddWithValue("$id", productId);
        return Convert.ToString(cmd.ExecuteScalar(), CultureInfo.InvariantCulture) ?? string.Empty;
    }

    public void SetBarcode(int productId, string barcode, string actor)
    {
        if (productId <= 0) throw new ArgumentException("حدد الصنف أولًا.");
        barcode = (barcode ?? string.Empty).Trim();
        if (barcode.Length > 80) throw new ArgumentException("الباركود لا يجوز أن يتجاوز 80 حرفًا.");
        using var db = Database.Open();
        using var tx = db.BeginTransaction();
        using var cmd = db.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "UPDATE Products SET Barcode=$barcode WHERE Id=$id";
        cmd.Parameters.AddWithValue("$barcode", string.IsNullOrWhiteSpace(barcode) ? DBNull.Value : barcode);
        cmd.Parameters.AddWithValue("$id", productId);
        try
        {
            if (cmd.ExecuteNonQuery() != 1) throw new InvalidOperationException("الصنف غير موجود.");
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 19)
        {
            throw new InvalidOperationException("هذا الباركود مسجل لصنف آخر.", ex);
        }
        using var audit = db.CreateCommand();
        audit.Transaction = tx;
        audit.CommandText = "INSERT INTO AuditLog(EventAt,Actor,Action,Entity,EntityId,Details) VALUES($at,$actor,'تحديث باركود','Product',$id,$details)";
        audit.Parameters.AddWithValue("$at", Now());
        audit.Parameters.AddWithValue("$actor", string.IsNullOrWhiteSpace(actor) ? "system" : actor);
        audit.Parameters.AddWithValue("$id", productId.ToString(CultureInfo.InvariantCulture));
        audit.Parameters.AddWithValue("$details", string.IsNullOrWhiteSpace(barcode) ? "إزالة الباركود" : "تغيير الباركود إلى " + barcode);
        audit.ExecuteNonQuery();
        tx.Commit();
    }

    public CashierSessionSummary? GetActiveSession(string cashierName)
    {
        if (string.IsNullOrWhiteSpace(cashierName)) throw new ArgumentException("اسم الكاشير مطلوب.");
        using var db = Database.Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = "SELECT Id FROM CashierSessions WHERE CashierName=$name AND Status='OPEN' ORDER BY Id DESC LIMIT 1";
        cmd.Parameters.AddWithValue("$name", cashierName.Trim());
        var value = cmd.ExecuteScalar();
        return value is null || value == DBNull.Value ? null : GetSessionSummary(Convert.ToInt32(value, CultureInfo.InvariantCulture));
    }

    public CashierSessionSummary OpenSession(string cashierName, decimal openingCash, string memo = "")
    {
        cashierName = (cashierName ?? string.Empty).Trim();
        if (cashierName.Length == 0) throw new ArgumentException("اسم الكاشير مطلوب.");
        if (openingCash < 0m) throw new ArgumentException("رصيد بداية الوردية لا يمكن أن يكون سالبًا.");
        var openingHalalas = AccountingService.ToHalalas(openingCash);
        using var db = Database.Open();
        using var tx = db.BeginTransaction();
        using (var existing = db.CreateCommand())
        {
            existing.Transaction = tx;
            existing.CommandText = "SELECT COUNT(*) FROM CashierSessions WHERE CashierName=$name AND Status='OPEN'";
            existing.Parameters.AddWithValue("$name", cashierName);
            if (Convert.ToInt32(existing.ExecuteScalar(), CultureInfo.InvariantCulture) > 0)
                throw new InvalidOperationException("لدى هذا المستخدم وردية مفتوحة بالفعل. أغلقها قبل فتح وردية جديدة.");
        }
        int id;
        using (var insert = db.CreateCommand())
        {
            insert.Transaction = tx;
            insert.CommandText = @"INSERT INTO CashierSessions(CashierName,OpenedAt,OpeningCashHalalas,Status,Memo)
VALUES($cashier,$at,$opening,'OPEN',$memo); SELECT last_insert_rowid()";
            insert.Parameters.AddWithValue("$cashier", cashierName);
            insert.Parameters.AddWithValue("$at", Now());
            insert.Parameters.AddWithValue("$opening", openingHalalas);
            insert.Parameters.AddWithValue("$memo", (memo ?? string.Empty).Trim());
            id = Convert.ToInt32(insert.ExecuteScalar(), CultureInfo.InvariantCulture);
        }
        using (var audit = db.CreateCommand())
        {
            audit.Transaction = tx;
            audit.CommandText = "INSERT INTO AuditLog(EventAt,Actor,Action,Entity,EntityId,Details) VALUES($at,$actor,'فتح وردية','CashierSession',$id,$details)";
            audit.Parameters.AddWithValue("$at", Now());
            audit.Parameters.AddWithValue("$actor", cashierName);
            audit.Parameters.AddWithValue("$id", id.ToString(CultureInfo.InvariantCulture));
            audit.Parameters.AddWithValue("$details", "رصيد بداية الوردية: " + openingHalalas.ToString(CultureInfo.InvariantCulture) + " هللة");
            audit.ExecuteNonQuery();
        }
        tx.Commit();
        return GetSessionSummary(id);
    }

    public CashierSessionSummary GetSessionSummary(int sessionId)
    {
        using var db = Database.Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = @"
SELECT s.Id,s.CashierName,s.OpenedAt,s.ClosedAt,s.OpeningCashHalalas,
       COALESCE(SUM(CASE WHEN i.PaymentMethod='CASH' AND i.Kind='SALE' AND i.Status='POSTED' THEN i.TotalHalalas ELSE 0 END),0) AS CashSales,
       s.ExpectedCashHalalas,s.CountedCashHalalas,s.DifferenceHalalas,s.Status,
       COUNT(CASE WHEN i.Kind='SALE' AND i.Status='POSTED' THEN 1 END) AS InvoiceCount
FROM CashierSessions s LEFT JOIN Invoices i ON i.CashierSessionId=s.Id
WHERE s.Id=$id
GROUP BY s.Id";
        cmd.Parameters.AddWithValue("$id", sessionId);
        using var reader = cmd.ExecuteReader();
        if (!reader.Read()) throw new InvalidOperationException("الوردية غير موجودة.");
        var openingHalalas = reader.GetInt64(4);
        var salesHalalas = reader.GetInt64(5);
        var storedExpected = reader.IsDBNull(6) ? (long?)null : reader.GetInt64(6);
        var counted = reader.IsDBNull(7) ? (decimal?)null : AccountingService.FromHalalas(reader.GetInt64(7));
        var difference = reader.IsDBNull(8) ? (decimal?)null : AccountingService.FromHalalas(reader.GetInt64(8));
        var opened = DateTimeOffset.Parse(reader.GetString(2), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
        DateTimeOffset? closed = reader.IsDBNull(3) ? null : DateTimeOffset.Parse(reader.GetString(3), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
        var isOpen = reader.GetString(9) == "OPEN";
        var expectedHalalas = storedExpected ?? checked(openingHalalas + salesHalalas);
        return new CashierSessionSummary(
            reader.GetInt32(0), reader.GetString(1), opened, closed,
            AccountingService.FromHalalas(openingHalalas), AccountingService.FromHalalas(salesHalalas),
            AccountingService.FromHalalas(expectedHalalas), counted, difference, reader.GetInt32(10), isOpen);
    }

    public CashierSessionSummary CloseSession(int sessionId, string cashierName, decimal countedCash, string memo = "")
    {
        cashierName = (cashierName ?? string.Empty).Trim();
        if (sessionId <= 0 || cashierName.Length == 0) throw new ArgumentException("بيانات الوردية غير مكتملة.");
        if (countedCash < 0m) throw new ArgumentException("المبلغ المعدود لا يمكن أن يكون سالبًا.");
        var countedHalalas = AccountingService.ToHalalas(countedCash);
        using var db = Database.Open();
        using var tx = db.BeginTransaction();
        long openingHalalas, salesHalalas;
        int invoiceCount;
        using (var query = db.CreateCommand())
        {
            query.Transaction = tx;
            query.CommandText = @"
SELECT s.OpeningCashHalalas,
       COALESCE(SUM(CASE WHEN i.PaymentMethod='CASH' AND i.Kind='SALE' AND i.Status='POSTED' THEN i.TotalHalalas ELSE 0 END),0),
       COUNT(CASE WHEN i.Kind='SALE' AND i.Status='POSTED' THEN 1 END)
FROM CashierSessions s LEFT JOIN Invoices i ON i.CashierSessionId=s.Id
WHERE s.Id=$id AND s.CashierName=$cashier AND s.Status='OPEN'
GROUP BY s.Id";
            query.Parameters.AddWithValue("$id", sessionId);
            query.Parameters.AddWithValue("$cashier", cashierName);
            using var reader = query.ExecuteReader();
            if (!reader.Read()) throw new InvalidOperationException("الوردية ليست مفتوحة لهذا المستخدم أو سبق إغلاقها.");
            openingHalalas = reader.GetInt64(0);
            salesHalalas = reader.GetInt64(1);
            invoiceCount = reader.GetInt32(2);
        }
        var expectedHalalas = checked(openingHalalas + salesHalalas);
        var differenceHalalas = countedHalalas - expectedHalalas;
        using (var update = db.CreateCommand())
        {
            update.Transaction = tx;
            update.CommandText = @"UPDATE CashierSessions
SET ClosedAt=$at,ExpectedCashHalalas=$expected,CountedCashHalalas=$counted,DifferenceHalalas=$difference,Status='CLOSED',Memo=CASE WHEN $memo='' THEN Memo ELSE Memo || char(10) || $memo END
WHERE Id=$id AND CashierName=$cashier AND Status='OPEN'";
            update.Parameters.AddWithValue("$at", Now());
            update.Parameters.AddWithValue("$expected", expectedHalalas);
            update.Parameters.AddWithValue("$counted", countedHalalas);
            update.Parameters.AddWithValue("$difference", differenceHalalas);
            update.Parameters.AddWithValue("$memo", (memo ?? string.Empty).Trim());
            update.Parameters.AddWithValue("$id", sessionId);
            update.Parameters.AddWithValue("$cashier", cashierName);
            if (update.ExecuteNonQuery() != 1) throw new InvalidOperationException("تعذر إغلاق الوردية؛ ربما أغلقت في جلسة أخرى.");
        }
        using (var audit = db.CreateCommand())
        {
            audit.Transaction = tx;
            audit.CommandText = "INSERT INTO AuditLog(EventAt,Actor,Action,Entity,EntityId,Details) VALUES($at,$actor,'إغلاق وردية','CashierSession',$id,$details)";
            audit.Parameters.AddWithValue("$at", Now());
            audit.Parameters.AddWithValue("$actor", cashierName);
            audit.Parameters.AddWithValue("$id", sessionId.ToString(CultureInfo.InvariantCulture));
            audit.Parameters.AddWithValue("$details", $"الفواتير: {invoiceCount}؛ المتوقع من البيع النقدي مع الافتتاح: {expectedHalalas} هللة؛ المعدود: {countedHalalas} هللة؛ الفرق: {differenceHalalas} هللة");
            audit.ExecuteNonQuery();
        }
        tx.Commit();
        return GetSessionSummary(sessionId);
    }

    public DataTable SessionRegister(DateTime? from = null, DateTime? to = null)
    {
        if (from.HasValue && to.HasValue && from.Value.Date > to.Value.Date)
            throw new ArgumentException("تاريخ البداية يجب ألا يتجاوز تاريخ النهاية.");

        using var db = Database.Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = @"
SELECT s.Id AS 'رقم الوردية',
       s.CashierName AS 'الكاشير',
       s.OpenedAt AS 'وقت الفتح',
       s.ClosedAt AS 'وقت الإغلاق',
       ROUND(s.OpeningCashHalalas/100.0,2) AS 'رصيد الافتتاح',
       ROUND(COALESCE(SUM(CASE WHEN i.PaymentMethod='CASH' AND i.Kind='SALE' AND i.Status='POSTED'
                               THEN i.TotalHalalas ELSE 0 END),0)/100.0,2) AS 'المبيعات النقدية',
       ROUND(COALESCE(s.ExpectedCashHalalas,
          s.OpeningCashHalalas + COALESCE(SUM(CASE WHEN i.PaymentMethod='CASH' AND i.Kind='SALE' AND i.Status='POSTED'
                                                   THEN i.TotalHalalas ELSE 0 END),0))/100.0,2) AS 'النقد المتوقع',
       CASE WHEN s.CountedCashHalalas IS NULL THEN NULL ELSE ROUND(s.CountedCashHalalas/100.0,2) END AS 'النقد المعدود',
       CASE WHEN s.DifferenceHalalas IS NULL THEN NULL ELSE ROUND(s.DifferenceHalalas/100.0,2) END AS 'الفرق',
       COUNT(CASE WHEN i.Kind='SALE' AND i.Status='POSTED' THEN 1 END) AS 'عدد الفواتير',
       CASE s.Status WHEN 'OPEN' THEN 'مفتوحة' ELSE 'مغلقة' END AS 'الحالة',
       s.Memo AS 'ملاحظات'
FROM CashierSessions s
LEFT JOIN Invoices i ON i.CashierSessionId=s.Id
WHERE ($from IS NULL OR substr(s.OpenedAt,1,10)>=$from)
  AND ($to IS NULL OR substr(s.OpenedAt,1,10)<=$to)
GROUP BY s.Id
ORDER BY s.OpenedAt DESC,s.Id DESC";
        cmd.Parameters.AddWithValue("$from", from.HasValue ? Date(from.Value) : DBNull.Value);
        cmd.Parameters.AddWithValue("$to", to.HasValue ? Date(to.Value) : DBNull.Value);

        using var reader = cmd.ExecuteReader();
        var table = new DataTable();
        for (var i = 0; i < reader.FieldCount; i++)
            table.Columns.Add(reader.GetName(i), typeof(object));
        while (reader.Read())
        {
            var values = new object[reader.FieldCount];
            reader.GetValues(values);
            table.Rows.Add(values);
        }
        return table;
    }

    public long Checkout(
        DateTime invoiceDate, int customerId, IReadOnlyList<InvoiceLineInput> lines,
        decimal discountPercent, string paymentMethod, decimal amountTendered, string actor, int cashierSessionId)
    {
        if (cashierSessionId <= 0) throw new InvalidOperationException("افتح وردية الكاشير قبل اعتماد البيع.");
        if (lines is null || lines.Count == 0) throw new InvalidOperationException("السلة فارغة. أضف صنفًا قبل إتمام البيع.");
        if (paymentMethod is not ("CASH" or "BANK" or "CREDIT"))
            throw new ArgumentException("اختر النقدي أو الشبكة أو الآجل.");
        if (paymentMethod == "CREDIT")
        {
            if (customerId == EnsureCashCustomer())
                throw new InvalidOperationException("اختر عميلًا مسجلًا للبيع الآجل.");
        }

        // AccountingService posts the invoice, double-entry journal, tax, cost of sales,
        // stock movements, and audit record in one SQLite transaction.
        var id = _accounting.CreateInvoice(
            "SALE", invoiceDate, customerId, paymentMethod == "CREDIT", lines,
            discountPercent, paymentMethod, amountTendered, paymentMethod == "BANK" ? "1020" : "1010", cashierSessionId);

        using var db = Database.Open();
        using var audit = db.CreateCommand();
        audit.CommandText = @"
UPDATE AuditLog SET Actor=$actor
WHERE Id=(SELECT MAX(Id) FROM AuditLog WHERE Entity='Invoice' AND EntityId=$id AND Action='ترحيل')";
        audit.Parameters.AddWithValue("$actor", string.IsNullOrWhiteSpace(actor) ? "system" : actor);
        audit.Parameters.AddWithValue("$id", id.ToString(CultureInfo.InvariantCulture));
        audit.ExecuteNonQuery();
        return id;
    }

    public CashReceipt GetReceipt(long invoiceId)
    {
        using var db = Database.Open();
        string number, customer, paymentMethod, date;
        long subtotal, discount, vat, total, tendered, change;
        decimal discountPercent;
        using (var cmd = db.CreateCommand())
        {
            cmd.CommandText = @"
SELECT i.Number,i.InvoiceDate,p.Name,i.PaymentMethod,i.SubtotalHalalas,
       i.DiscountHalalas,i.DiscountPercent,i.VatHalalas,i.TotalHalalas,
       i.AmountTenderedHalalas,i.ChangeHalalas,i.Id
FROM Invoices i JOIN Parties p ON p.Id=i.PartyId
WHERE i.Id=$id AND i.Kind='SALE' AND i.Status='POSTED'";
            cmd.Parameters.AddWithValue("$id", invoiceId);
            using var reader = cmd.ExecuteReader();
            if (!reader.Read()) throw new InvalidOperationException("الفاتورة غير موجودة أو ليست فاتورة بيع مرحلة.");
            number = reader.GetString(0);
            date = reader.GetString(1);
            customer = reader.GetString(2);
            paymentMethod = reader.GetString(3);
            subtotal = reader.GetInt64(4);
            discount = reader.GetInt64(5);
            discountPercent = Convert.ToDecimal(reader.GetDouble(6), CultureInfo.InvariantCulture);
            vat = reader.GetInt64(7);
            total = reader.GetInt64(8);
            tendered = reader.GetInt64(9);
            change = reader.GetInt64(10);
        }

        var lines = new List<CashReceiptLine>();
        using (var cmd = db.CreateCommand())
        {
            cmd.CommandText = @"
SELECT p.Code,p.Name,p.Unit,il.Quantity,il.UnitPriceHalalas,
       il.NetHalalas,il.VatHalalas,il.TotalHalalas
FROM InvoiceLines il JOIN Products p ON p.Id=il.ProductId
WHERE il.InvoiceId=$id ORDER BY il.Id";
            cmd.Parameters.AddWithValue("$id", invoiceId);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                lines.Add(new CashReceiptLine(
                    reader.GetString(0), reader.GetString(1), reader.GetString(2),
                    Convert.ToDecimal(reader.GetDouble(3), CultureInfo.InvariantCulture),
                    AccountingService.FromHalalas(reader.GetInt64(4)),
                    AccountingService.FromHalalas(reader.GetInt64(5)),
                    AccountingService.FromHalalas(reader.GetInt64(6)),
                    AccountingService.FromHalalas(reader.GetInt64(7))));
            }
        }

        return new CashReceipt(invoiceId, number,
            DateTime.ParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture),
            customer, paymentMethod, AccountingService.FromHalalas(subtotal),
            AccountingService.FromHalalas(discount), discountPercent,
            AccountingService.FromHalalas(vat), AccountingService.FromHalalas(total),
            AccountingService.FromHalalas(tendered), AccountingService.FromHalalas(change), lines);
    }
}
