using System.Data;
using System.Globalization;
using Microsoft.Data.Sqlite;
using AlSaqr.Accounting.Data;
using AlSaqr.Accounting.Services;

namespace AlSaqr.Accounting.Services;

internal sealed class InventoryAdjustmentService
{
    private static string Now() => DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);
    private static string Date(DateTime date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    public DataTable Register(DateTime from, DateTime to)
    {
        using var db = Database.Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = @"
SELECT a.Number AS 'رقم التسوية',a.AdjustmentDate AS 'التاريخ',p.Code AS 'رمز الصنف',
p.Name AS 'الصنف',ROUND(a.QuantityDelta,3) AS 'فرق الكمية',
ROUND(a.UnitCostHalalas/100.0,2) AS 'تكلفة الوحدة',
ROUND(ABS(a.QuantityDelta)*a.UnitCostHalalas/100.0,2) AS 'قيمة التسوية',
a.Reason AS 'سبب التسوية',a.JournalId AS 'رقم القيد'
FROM InventoryAdjustments a JOIN Products p ON p.Id=a.ProductId
WHERE a.AdjustmentDate >= $from AND a.AdjustmentDate <= $to
ORDER BY a.AdjustmentDate,a.Id";
        cmd.Parameters.AddWithValue("$from", Date(from));
        cmd.Parameters.AddWithValue("$to", Date(to));
        using var reader = cmd.ExecuteReader();
        var table = new DataTable();
        table.Load(reader);
        return table;
    }

    public long AdjustStock(int productId, DateTime date, decimal quantityDelta, string reason)
    {
        if (productId <= 0) throw new ArgumentException("حدد الصنف.");
        if (quantityDelta == 0) throw new ArgumentException("فرق الكمية يجب ألا يساوي صفرًا.");
        if (string.IsNullOrWhiteSpace(reason)) throw new ArgumentException("سبب التسوية مطلوب.");
        using var db = Database.Open();
        using var tx = db.BeginTransaction();

        string code, name;
        decimal quantity;
        long cost;
        using (var product = db.CreateCommand())
        {
            product.Transaction = tx;
            product.CommandText = "SELECT Code,Name,Quantity,CostHalalas,IsActive FROM Products WHERE Id=$id";
            product.Parameters.AddWithValue("$id", productId);
            using var reader = product.ExecuteReader();
            if (!reader.Read() || reader.GetInt64(4) != 1)
                throw new InvalidOperationException("الصنف غير موجود أو موقوف.");
            code = reader.GetString(0);
            name = reader.GetString(1);
            quantity = Convert.ToDecimal(reader.GetDouble(2), CultureInfo.InvariantCulture);
            cost = reader.GetInt64(3);
        }
        if (cost <= 0) throw new InvalidOperationException("لا توجد تكلفة مخزنية مسجلة للصنف تسمح بتسويته محاسبيًا.");
        if (quantityDelta < 0 && Math.Abs(quantityDelta) > quantity + 0.0000001m)
            throw new InvalidOperationException($"الكمية الحالية {quantity:0.###} لا تكفي لتسجيل عجز قدره {Math.Abs(quantityDelta):0.###}.");
        var valueHalalas = AccountingService.ToHalalas(Math.Abs(quantityDelta) * AccountingService.FromHalalas(cost));
        if (valueHalalas <= 0) throw new InvalidOperationException("قيمة التسوية أصغر من الحد المالي المسموح.");
        var amount = AccountingService.FromHalalas(valueHalalas);
        var lines = quantityDelta > 0
            ? new[] { new JournalLineInput("1200", amount, 0, "زيادة مخزون حسب محضر التسوية"), new JournalLineInput("4090", 0, amount, "مكاسب تسوية المخزون") }
            : new[] { new JournalLineInput("5010", amount, 0, "عجز مخزون حسب محضر التسوية"), new JournalLineInput("1200", 0, amount, "تخفيض قيمة المخزون") };

        var number = "IA-" + date.ToString("yyyyMMdd", CultureInfo.InvariantCulture) + "-" + Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
        var journalId = InsertJournal(db, tx, date, $"تسوية مخزون {number} — {name}", "INVENTORY_ADJUSTMENT", lines);

        long adjustmentId;
        using (var cmd = db.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = @"INSERT INTO InventoryAdjustments(Number,AdjustmentDate,ProductId,QuantityDelta,UnitCostHalalas,Reason,JournalId,CreatedAt)
VALUES($number,$date,$product,$delta,$cost,$reason,$journal,$at); SELECT last_insert_rowid()";
            cmd.Parameters.AddWithValue("$number", number);
            cmd.Parameters.AddWithValue("$date", Date(date));
            cmd.Parameters.AddWithValue("$product", productId);
            cmd.Parameters.AddWithValue("$delta", (double)quantityDelta);
            cmd.Parameters.AddWithValue("$cost", cost);
            cmd.Parameters.AddWithValue("$reason", reason.Trim());
            cmd.Parameters.AddWithValue("$journal", journalId);
            cmd.Parameters.AddWithValue("$at", Now());
            adjustmentId = Convert.ToInt64(cmd.ExecuteScalar(), CultureInfo.InvariantCulture);
        }

        using (var update = db.CreateCommand())
        {
            update.Transaction = tx;
            update.CommandText = "UPDATE Products SET Quantity=Quantity+$delta WHERE Id=$id AND IsActive=1 AND Quantity+$delta >= -0.0000001";
            update.Parameters.AddWithValue("$delta", (double)quantityDelta);
            update.Parameters.AddWithValue("$id", productId);
            if (update.ExecuteNonQuery() != 1)
                throw new InvalidOperationException("تغيير الرصيد غير صالح؛ تم إلغاء التسوية كاملة.");
        }

        using (var movement = db.CreateCommand())
        {
            movement.Transaction = tx;
            movement.CommandText = @"INSERT INTO StockMovements(MovementDate,ProductId,Source,SourceId,QuantityIn,QuantityOut,UnitCostHalalas,Memo,CreatedAt)
VALUES($date,$product,'ADJUSTMENT',$source,$in,$out,$cost,$memo,$at)";
            movement.Parameters.AddWithValue("$date", Date(date));
            movement.Parameters.AddWithValue("$product", productId);
            movement.Parameters.AddWithValue("$source", adjustmentId.ToString(CultureInfo.InvariantCulture));
            movement.Parameters.AddWithValue("$in", quantityDelta > 0 ? (double)quantityDelta : 0d);
            movement.Parameters.AddWithValue("$out", quantityDelta < 0 ? (double)Math.Abs(quantityDelta) : 0d);
            movement.Parameters.AddWithValue("$cost", cost);
            movement.Parameters.AddWithValue("$memo", $"{number}: {reason.Trim()}");
            movement.Parameters.AddWithValue("$at", Now());
            movement.ExecuteNonQuery();
        }

        Audit(db, tx, adjustmentId, number, code, name, quantityDelta, amount, reason.Trim());
        tx.Commit();
        return adjustmentId;
    }

    private static long InsertJournal(SqliteConnection db, SqliteTransaction tx, DateTime date, string memo, string source, IReadOnlyList<JournalLineInput> lines)
    {
        var debit = lines.Sum(x => AccountingService.ToHalalas(x.Debit));
        var credit = lines.Sum(x => AccountingService.ToHalalas(x.Credit));
        if (lines.Count < 2 || debit <= 0 || debit != credit)
            throw new InvalidOperationException("قيد تسوية المخزون غير متوازن.");
        using (var cmd = db.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "INSERT INTO Journals(Number,EntryDate,Memo,Source,Status,CreatedAt) VALUES($number,$date,$memo,$source,'POSTED',$at)";
            cmd.Parameters.AddWithValue("$number", "JV-" + date.ToString("yyyyMMdd", CultureInfo.InvariantCulture) + "-" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant());
            cmd.Parameters.AddWithValue("$date", Date(date));
            cmd.Parameters.AddWithValue("$memo", memo);
            cmd.Parameters.AddWithValue("$source", source);
            cmd.Parameters.AddWithValue("$at", Now());
            cmd.ExecuteNonQuery();
        }
        long journalId;
        using (var cmd = db.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "SELECT last_insert_rowid()";
            journalId = Convert.ToInt64(cmd.ExecuteScalar(), CultureInfo.InvariantCulture);
        }
        foreach (var line in lines)
        {
            using var account = db.CreateCommand();
            account.Transaction = tx;
            account.CommandText = "SELECT IsActive FROM Accounts WHERE Code=$code";
            account.Parameters.AddWithValue("$code", line.AccountCode);
            if (Convert.ToInt64(account.ExecuteScalar() ?? 0, CultureInfo.InvariantCulture) != 1)
                throw new InvalidOperationException($"حساب التسوية {line.AccountCode} غير موجود أو موقوف.");
            using var detail = db.CreateCommand();
            detail.Transaction = tx;
            detail.CommandText = "INSERT INTO JournalLines(JournalId,AccountCode,PartyId,Memo,DebitHalalas,CreditHalalas) VALUES($journal,$account,NULL,$memo,$debit,$credit)";
            detail.Parameters.AddWithValue("$journal", journalId);
            detail.Parameters.AddWithValue("$account", line.AccountCode);
            detail.Parameters.AddWithValue("$memo", line.Memo);
            detail.Parameters.AddWithValue("$debit", AccountingService.ToHalalas(line.Debit));
            detail.Parameters.AddWithValue("$credit", AccountingService.ToHalalas(line.Credit));
            detail.ExecuteNonQuery();
        }
        return journalId;
    }

    private static void Audit(SqliteConnection db, SqliteTransaction tx, long id, string number, string code, string name, decimal delta, decimal amount, string reason)
    {
        using var cmd = db.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "INSERT INTO AuditLog(EventAt,Actor,Action,Entity,EntityId,Details) VALUES($at,$actor,'تسوية مخزون','InventoryAdjustment',$id,$details)";
        cmd.Parameters.AddWithValue("$at", Now());
        cmd.Parameters.AddWithValue("$actor", Environment.UserName);
        cmd.Parameters.AddWithValue("$id", id.ToString(CultureInfo.InvariantCulture));
        cmd.Parameters.AddWithValue("$details", $"{number}; الصنف={code} - {name}; فرق الكمية={delta:0.###}; القيمة={amount:N2}; السبب={reason}");
        cmd.ExecuteNonQuery();
    }
}