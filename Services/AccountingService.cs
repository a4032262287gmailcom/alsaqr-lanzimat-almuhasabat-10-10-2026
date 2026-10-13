using Microsoft.Data.Sqlite;
using AlSaqr.Accounting.Data;

namespace AlSaqr.Accounting.Services;

public sealed record InvoiceLineInput(int ProductId, decimal Quantity, decimal UnitPrice, decimal VatRate);

public static class AccountingService
{
    private static string Now => DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

    public static void SaveAccount(string code, string name, string type, bool active)
    {
        if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("رمز الحساب واسم الحساب مطلوبان.");
        if (!new[] { "Asset", "Liability", "Equity", "Revenue", "Expense" }.Contains(type))
            throw new ArgumentException("نوع الحساب غير صحيح.");

        using var db = Database.Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = @"INSERT INTO Accounts(Code,Name,Type,IsActive,CreatedAt)
VALUES($code,$name,$type,$active,$at)
ON CONFLICT(Code) DO UPDATE SET Name=excluded.Name,Type=excluded.Type,IsActive=excluded.IsActive";
        cmd.Parameters.AddWithValue("$code", code.Trim());
        cmd.Parameters.AddWithValue("$name", name.Trim());
        cmd.Parameters.AddWithValue("$type", type);
        cmd.Parameters.AddWithValue("$active", active ? 1 : 0);
        cmd.Parameters.AddWithValue("$at", Now);
        cmd.ExecuteNonQuery();
        Audit(db, "حفظ", "الحساب", code, name);
    }

    public static void SaveParty(long? id, string name, string kind, string phone, string vatNumber, bool active)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("اسم العميل أو المورد مطلوب.");
        if (!new[] { "Customer", "Supplier", "Both" }.Contains(kind)) throw new ArgumentException("تصنيف الطرف غير صحيح.");
        using var db = Database.Open();
        using var cmd = db.CreateCommand();
        if (id.HasValue)
        {
            cmd.CommandText = "UPDATE Parties SET Name=$name,Kind=$kind,Phone=$phone,VatNumber=$vat,IsActive=$active WHERE Id=$id";
            cmd.Parameters.AddWithValue("$id", id.Value);
        }
        else
        {
            cmd.CommandText = "INSERT INTO Parties(Name,Kind,Phone,VatNumber,IsActive,CreatedAt) VALUES($name,$kind,$phone,$vat,$active,$at)";
            cmd.Parameters.AddWithValue("$at", Now);
        }
        cmd.Parameters.AddWithValue("$name", name.Trim());
        cmd.Parameters.AddWithValue("$kind", kind);
        cmd.Parameters.AddWithValue("$phone", phone.Trim());
        cmd.Parameters.AddWithValue("$vat", vatNumber.Trim());
        cmd.Parameters.AddWithValue("$active", active ? 1 : 0);
        cmd.ExecuteNonQuery();
        Audit(db, id.HasValue ? "تعديل" : "إضافة", "الطرف", id?.ToString() ?? GetLastInsertId(db).ToString(), name);
    }

    public static void SaveProduct(long? id, string code, string name, string unit, decimal cost, decimal price, decimal vatRate, bool active)
    {
        if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(name)) throw new ArgumentException("رمز الصنف واسمه مطلوبان.");
        if (cost < 0 || price < 0 || vatRate < 0 || vatRate > 100) throw new ArgumentException("تحقق من التكلفة والسعر ونسبة الضريبة.");
        using var db = Database.Open();
        using var cmd = db.CreateCommand();
        if (id.HasValue)
        {
            cmd.CommandText = @"UPDATE Products SET Code=$code,Name=$name,Unit=$unit,CostHalalas=$cost,PriceHalalas=$price,VatRate=$vat,IsActive=$active WHERE Id=$id";
            cmd.Parameters.AddWithValue("$id", id.Value);
        }
        else
        {
            cmd.CommandText = @"INSERT INTO Products(Code,Name,Unit,Quantity,CostHalalas,PriceHalalas,VatRate,IsActive,CreatedAt)
VALUES($code,$name,$unit,0,$cost,$price,$vat,$active,$at)";
            cmd.Parameters.AddWithValue("$at", Now);
        }
        cmd.Parameters.AddWithValue("$code", code.Trim());
        cmd.Parameters.AddWithValue("$name", name.Trim());
        cmd.Parameters.AddWithValue("$unit", string.IsNullOrWhiteSpace(unit) ? "حبة" : unit.Trim());
        cmd.Parameters.AddWithValue("$cost", ToHalalas(cost));
        cmd.Parameters.AddWithValue("$price", ToHalalas(price));
        cmd.Parameters.AddWithValue("$vat", vatRate);
        cmd.Parameters.AddWithValue("$active", active ? 1 : 0);
        cmd.ExecuteNonQuery();
        Audit(db, id.HasValue ? "تعديل" : "إضافة", "الصنف", id?.ToString() ?? GetLastInsertId(db).ToString(), name);
    }

    public static long PostInvoice(string kind, string number, DateTime date, int partyId, bool credit, IReadOnlyList<InvoiceLineInput> lines)
    {
        if (kind is not ("SALE" or "PURCHASE")) throw new ArgumentException("نوع الفاتورة غير صحيح.");
        if (string.IsNullOrWhiteSpace(number)) throw new ArgumentException("رقم الفاتورة مطلوب.");
        if (lines.Count == 0) throw new ArgumentException("أضف صنفًا واحدًا على الأقل.");
        if (lines.Any(x => x.Quantity <= 0 || x.UnitPrice < 0 || x.VatRate < 0 || x.VatRate > 100))
            throw new ArgumentException("تحقق من كميات وأسعار ونسب الضريبة.");

        using var db = Database.Open();
        using var tx = db.BeginTransaction();
        var subtotal = 0L;
        var vatTotal = 0L;
        var costTotal = 0L;
        var prepared = new List<(InvoiceLineInput Input, string Name, long Cost, long Net, long Vat, long Total)>();

        foreach (var line in lines)
        {
            using var p = db.CreateCommand();
            p.Transaction = tx;
            p.CommandText = "SELECT Name,CostHalalas,VatRate,IsActive FROM Products WHERE Id=$id";
            p.Parameters.AddWithValue("$id", line.ProductId);
            using var reader = p.ExecuteReader();
            if (!reader.Read()) throw new InvalidOperationException($"الصنف رقم {line.ProductId} غير موجود.");
            if (reader.GetInt32(3) == 0) throw new InvalidOperationException("لا يمكن الفوترة لصنف موقوف.");
            var productName = reader.GetString(0);
            var cost = reader.GetInt64(1);
            reader.Close();

            var net = ToHalalas(line.Quantity * line.UnitPrice);
            var vat = ToHalalas(net / 100m * line.VatRate);
            var total = checked(net + vat);
            subtotal = checked(subtotal + net);
            vatTotal = checked(vatTotal + vat);
            costTotal = checked(costTotal + ToHalalas(line.Quantity * (cost / 100m)));
            prepared.Add((line, productName, cost, net, vat, total));
        }

        var totalHalalas = checked(subtotal + vatTotal);
        var journalNumber = "INV-" + kind + "-" + number.Trim();
        using (var journal = db.CreateCommand())
        {
            journal.Transaction = tx;
            journal.CommandText = "INSERT INTO Journals(Number,EntryDate,Memo,Source,Status,CreatedAt) VALUES($n,$date,$memo,'Invoice','POSTED',$at)";
            journal.Parameters.AddWithValue("$n", journalNumber);
            journal.Parameters.AddWithValue("$date", date.ToString("yyyy-MM-dd"));
            journal.Parameters.AddWithValue("$memo", (kind == "SALE" ? "فاتورة مبيعات " : "فاتورة مشتريات ") + number.Trim());
            journal.Parameters.AddWithValue("$at", Now);
            journal.ExecuteNonQuery();
        }
        var journalId = GetLastInsertId(db, tx);

        using (var inv = db.CreateCommand())
        {
            inv.Transaction = tx;
            inv.CommandText = @"INSERT INTO Invoices(Kind,Number,InvoiceDate,PartyId,IsCredit,Status,SubtotalHalalas,VatHalalas,TotalHalalas,JournalId,CreatedAt)
VALUES($kind,$number,$date,$party,$credit,'POSTED',$subtotal,$vat,$total,$journal,$at)";
            inv.Parameters.AddWithValue("$kind", kind);
            inv.Parameters.AddWithValue("$number", number.Trim());
            inv.Parameters.AddWithValue("$date", date.ToString("yyyy-MM-dd"));
            inv.Parameters.AddWithValue("$party", partyId);
            inv.Parameters.AddWithValue("$credit", credit ? 1 : 0);
            inv.Parameters.AddWithValue("$subtotal", subtotal);
            inv.Parameters.AddWithValue("$vat", vatTotal);
            inv.Parameters.AddWithValue("$total", totalHalalas);
            inv.Parameters.AddWithValue("$journal", journalId);
            inv.Parameters.AddWithValue("$at", Now);
            inv.ExecuteNonQuery();
        }
        var invoiceId = GetLastInsertId(db, tx);

        foreach (var item in prepared)
        {
            using var lineCmd = db.CreateCommand();
            lineCmd.Transaction = tx;
            lineCmd.CommandText = @"INSERT INTO InvoiceLines(InvoiceId,ProductId,Quantity,UnitPriceHalalas,CostAtPostingHalalas,VatRate,NetHalalas,VatHalalas,TotalHalalas)
VALUES($invoice,$product,$qty,$price,$cost,$rate,$net,$vat,$total)";
            lineCmd.Parameters.AddWithValue("$invoice", invoiceId);
            lineCmd.Parameters.AddWithValue("$product", item.Input.ProductId);
            lineCmd.Parameters.AddWithValue("$qty", item.Input.Quantity);
            lineCmd.Parameters.AddWithValue("$price", ToHalalas(item.Input.UnitPrice));
            lineCmd.Parameters.AddWithValue("$cost", kind == "PURCHASE" ? ToHalalas(item.Input.UnitPrice) : item.Cost);
            lineCmd.Parameters.AddWithValue("$rate", item.Input.VatRate);
            lineCmd.Parameters.AddWithValue("$net", item.Net);
            lineCmd.Parameters.AddWithValue("$vat", item.Vat);
            lineCmd.Parameters.AddWithValue("$total", item.Total);
            lineCmd.ExecuteNonQuery();

            using var stock = db.CreateCommand();
            stock.Transaction = tx;
            if (kind == "PURCHASE")
            {
                stock.CommandText = @"UPDATE Products
SET CostHalalas=CASE WHEN Quantity+$qty > 0
  THEN CAST(ROUND((Quantity*CostHalalas + $qty*$unitCost)/(Quantity+$qty),0) AS INTEGER)
  ELSE $unitCost END,
    Quantity=Quantity+$qty
WHERE Id=$id";
                stock.Parameters.AddWithValue("$qty", item.Input.Quantity);
                stock.Parameters.AddWithValue("$unitCost", ToHalalas(item.Input.UnitPrice));
            }
            else
            {
                stock.CommandText = "UPDATE Products SET Quantity=Quantity-$qty WHERE Id=$id AND Quantity >= $qty";
                stock.Parameters.AddWithValue("$qty", item.Input.Quantity);
            }
            stock.Parameters.AddWithValue("$id", item.Input.ProductId);
            if (stock.ExecuteNonQuery() != 1)
                throw new InvalidOperationException(kind == "SALE"
                    ? $"المخزون غير كافٍ للصنف: {item.Name}."
                    : $"تعذر تحديث مخزون الصنف: {item.Name}.");
        }

        if (kind == "SALE")
        {
            AddJournalLine(db, tx, journalId, credit ? "1100" : "1010", "مدين", totalHalalas, 0);
            AddJournalLine(db, tx, journalId, "4000", "إيراد المبيعات", 0, subtotal);
            if (vatTotal > 0) AddJournalLine(db, tx, journalId, "2200", "ضريبة مخرجات", 0, vatTotal);
            if (costTotal > 0)
            {
                AddJournalLine(db, tx, journalId, "5000", "تكلفة البضاعة المباعة", costTotal, 0);
                AddJournalLine(db, tx, journalId, "1200", "تخفيض المخزون", 0, costTotal);
            }
        }
        else
        {
            AddJournalLine(db, tx, journalId, "1200", "زيادة المخزون", subtotal, 0);
            if (vatTotal > 0) AddJournalLine(db, tx, journalId, "1300", "ضريبة مدخلات", vatTotal, 0);
            AddJournalLine(db, tx, journalId, credit ? "2100" : "1010", "دائن", 0, totalHalalas);
        }

        using (var sum = db.CreateCommand())
        {
            sum.Transaction = tx;
            sum.CommandText = "SELECT COALESCE(SUM(DebitHalalas),0),COALESCE(SUM(CreditHalalas),0) FROM JournalLines WHERE JournalId=$id";
            sum.Parameters.AddWithValue("$id", journalId);
            using var reader = sum.ExecuteReader();
            reader.Read();
            if (reader.GetInt64(0) != reader.GetInt64(1))
                throw new InvalidOperationException("القيد غير متوازن؛ تم إلغاء الفاتورة تلقائيًا.");
        }

        Audit(db, tx, "ترحيل", "الفاتورة", invoiceId.ToString(), $"{kind}:{number}; الإجمالي={totalHalalas / 100m:0.00}");
        tx.Commit();
        return invoiceId;
    }

    public static long PostPartyPayment(string kind, string number, DateTime date, int partyId, decimal amount, string method, string memo)
    {
        if (kind is not ("RECEIPT" or "PAYMENT")) throw new ArgumentException("نوع السند غير صحيح.");
        if (string.IsNullOrWhiteSpace(number)) throw new ArgumentException("رقم السند مطلوب.");
        long amountHalalas = ToHalalas(amount);
        if (amountHalalas <= 0) throw new ArgumentException("يجب أن يكون المبلغ أكبر من صفر.");

        using var db = Database.Open();
        using var tx = db.BeginTransaction();
        using (var party = db.CreateCommand())
        {
            party.Transaction = tx;
            party.CommandText = "SELECT Kind,IsActive FROM Parties WHERE Id=$id";
            party.Parameters.AddWithValue("$id", partyId);
            using var reader = party.ExecuteReader();
            if (!reader.Read()) throw new InvalidOperationException("العميل أو المورد غير موجود.");
            var partyKind = reader.GetString(0);
            var active = reader.GetInt32(1) != 0;
            if (!active) throw new InvalidOperationException("الطرف موقوف.");
            if (kind == "RECEIPT" && partyKind is not ("Customer" or "Both"))
                throw new InvalidOperationException("سند القبض يتطلب عميلًا.");
            if (kind == "PAYMENT" && partyKind is not ("Supplier" or "Both"))
                throw new InvalidOperationException("سند الصرف يتطلب موردًا.");
        }

        using (var journal = db.CreateCommand())
        {
            journal.Transaction = tx;
            journal.CommandText = "INSERT INTO Journals(Number,EntryDate,Memo,Source,Status,CreatedAt) VALUES($number,$date,$memo,'PartyPayment','POSTED',$at)";
            journal.Parameters.AddWithValue("$number", "PAY-" + number.Trim());
            journal.Parameters.AddWithValue("$date", date.ToString("yyyy-MM-dd"));
            journal.Parameters.AddWithValue("$memo", (kind == "RECEIPT" ? "سند قبض " : "سند صرف ") + number.Trim() + " " + memo);
            journal.Parameters.AddWithValue("$at", Now);
            journal.ExecuteNonQuery();
        }
        long journalId = GetLastInsertId(db, tx);
        using (var payment = db.CreateCommand())
        {
            payment.Transaction = tx;
            payment.CommandText = @"INSERT INTO PartyPayments(Kind,Number,PaymentDate,PartyId,AmountHalalas,Method,Memo,JournalId,CreatedAt)
VALUES($kind,$number,$date,$party,$amount,$method,$memo,$journal,$at)";
            payment.Parameters.AddWithValue("$kind", kind);
            payment.Parameters.AddWithValue("$number", number.Trim());
            payment.Parameters.AddWithValue("$date", date.ToString("yyyy-MM-dd"));
            payment.Parameters.AddWithValue("$party", partyId);
            payment.Parameters.AddWithValue("$amount", amountHalalas);
            payment.Parameters.AddWithValue("$method", method);
            payment.Parameters.AddWithValue("$memo", memo.Trim());
            payment.Parameters.AddWithValue("$journal", journalId);
            payment.Parameters.AddWithValue("$at", Now);
            payment.ExecuteNonQuery();
        }
        long paymentId = GetLastInsertId(db, tx);
        if (kind == "RECEIPT")
        {
            AddJournalLine(db, tx, journalId, "1010", "الصندوق - قبض", amountHalalas, 0);
            AddJournalLine(db, tx, journalId, "1100", "تخفيض ذمم العملاء", 0, amountHalalas);
        }
        else
        {
            AddJournalLine(db, tx, journalId, "2100", "تخفيض ذمم الموردين", amountHalalas, 0);
            AddJournalLine(db, tx, journalId, "1010", "الصندوق - صرف", 0, amountHalalas);
        }
        Audit(db, tx, "ترحيل", kind == "RECEIPT" ? "سند قبض" : "سند صرف", paymentId.ToString(), $"{number}; المبلغ={amount:0.00}");
        tx.Commit();
        return paymentId;
    }

    private static void AddJournalLine(SqliteConnection db, SqliteTransaction tx, long journalId, string account, string memo, long debit, long credit)
    {
        using var cmd = db.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "INSERT INTO JournalLines(JournalId,AccountCode,Memo,DebitHalalas,CreditHalalas) VALUES($journal,$account,$memo,$debit,$credit)";
        cmd.Parameters.AddWithValue("$journal", journalId);
        cmd.Parameters.AddWithValue("$account", account);
        cmd.Parameters.AddWithValue("$memo", memo);
        cmd.Parameters.AddWithValue("$debit", debit);
        cmd.Parameters.AddWithValue("$credit", credit);
        cmd.ExecuteNonQuery();
    }

    private static long GetLastInsertId(SqliteConnection db, SqliteTransaction? tx = null)
    {
        using var cmd = db.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT last_insert_rowid()";
        return Convert.ToInt64(cmd.ExecuteScalar());
    }

    private static long ToHalalas(decimal amount) => checked((long)Math.Round(amount * 100m, MidpointRounding.AwayFromZero));

    private static void Audit(SqliteConnection db, string action, string entity, string id, string details)
    {
        using var cmd = db.CreateCommand();
        cmd.CommandText = "INSERT INTO AuditLog(EventAt,Actor,Action,Entity,EntityId,Details) VALUES($at,'المستخدم المحلي',$action,$entity,$id,$details)";
        cmd.Parameters.AddWithValue("$at", Now);
        cmd.Parameters.AddWithValue("$action", action);
        cmd.Parameters.AddWithValue("$entity", entity);
        cmd.Parameters.AddWithValue("$id", id);
        cmd.Parameters.AddWithValue("$details", details);
        cmd.ExecuteNonQuery();
    }

    private static void Audit(SqliteConnection db, SqliteTransaction tx, string action, string entity, string id, string details)
    {
        using var cmd = db.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "INSERT INTO AuditLog(EventAt,Actor,Action,Entity,EntityId,Details) VALUES($at,'المستخدم المحلي',$action,$entity,$id,$details)";
        cmd.Parameters.AddWithValue("$at", Now);
        cmd.Parameters.AddWithValue("$action", action);
        cmd.Parameters.AddWithValue("$entity", entity);
        cmd.Parameters.AddWithValue("$id", id);
        cmd.Parameters.AddWithValue("$details", details);
        cmd.ExecuteNonQuery();
    }
}
