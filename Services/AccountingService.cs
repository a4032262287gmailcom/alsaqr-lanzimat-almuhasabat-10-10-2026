 desktop-no-license-window
=======
using System.Data;
 main
using Microsoft.Data.Sqlite;
using AlSaqr.Accounting.Data;

namespace AlSaqr.Accounting.Services;

 desktop-no-license-window
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
=======
internal sealed record JournalLineInput(string AccountCode, decimal Debit, decimal Credit, string Memo = "", int? PartyId = null);
internal sealed record InvoiceLineInput(int ProductId, decimal Quantity, decimal UnitPrice);
internal sealed record AccountInfo(string Code, string Name, string Type, bool IsActive);
internal sealed record ProductInfo(int Id, string Code, string Name, string Unit, decimal Quantity, decimal Cost, decimal Price, decimal VatRate);
internal sealed record DashboardInfo(decimal Cash, decimal Receivables, decimal Payables, decimal SalesMonth, decimal ExpensesMonth, int PostedEntries);

internal sealed class AccountingService
{
    public static long ToHalalas(decimal amount) =>
        checked((long)decimal.Round(amount * 100m, 0, MidpointRounding.AwayFromZero));

    public static decimal FromHalalas(long amount) => amount / 100m;
    private static string Now() => DateTime.UtcNow.ToString("O");
    private static string Date(DateTime value) => value.ToString("yyyy-MM-dd");

    public DataTable Accounts()
    {
        using var db = Database.Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = "SELECT Code AS 'رقم الحساب', Name AS 'اسم الحساب', Type AS 'التصنيف', CASE IsActive WHEN 1 THEN 'نشط' ELSE 'موقوف' END AS 'الحالة' FROM Accounts ORDER BY Code";
        return ReadTable(cmd);
    }

    public List<AccountInfo> ActiveAccounts()
    {
        using var db = Database.Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = "SELECT Code,Name,Type,IsActive FROM Accounts WHERE IsActive=1 ORDER BY Code";
        using var reader = cmd.ExecuteReader();
        var result = new List<AccountInfo>();
        while (reader.Read())
            result.Add(new AccountInfo(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetInt64(3) == 1));
        return result;
    }

    public void AddAccount(string code, string name, string type)
    {
        code = (code ?? "").Trim();
        name = (name ?? "").Trim();
        if (code.Length is < 3 or > 20) throw new ArgumentException("رقم الحساب يجب أن يكون بين 3 و20 حرفًا.");
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("اسم الحساب مطلوب.");
        if (!new[] { "Asset", "Liability", "Equity", "Revenue", "Expense" }.Contains(type))
            throw new ArgumentException("نوع الحساب غير صالح.");
        using var db = Database.Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = "INSERT INTO Accounts(Code,Name,Type,IsActive,CreatedAt) VALUES($code,$name,$type,1,$at)";
        cmd.Parameters.AddWithValue("$code", code);
        cmd.Parameters.AddWithValue("$name", name);
        cmd.Parameters.AddWithValue("$type", type);
        cmd.Parameters.AddWithValue("$at", Now());
        try { cmd.ExecuteNonQuery(); }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 19) { throw new InvalidOperationException("رقم الحساب مستخدم مسبقًا.", ex); }
        Audit(db, "إضافة", "Account", code, $"إضافة الحساب {name}");
    }

    public void SetAccountActive(string code, bool active)
    {
        if (!active && new[] { "1010","1020","1100","1200","1300","2100","2200","3000","3100","4000","4010","5000","5050","6000" }.Contains(code))
            throw new InvalidOperationException("لا يمكن إيقاف حساب نظام أساسي مستخدم في الترحيل. أنشئ حسابًا فرعيًا بدلًا منه.");
        using var db = Database.Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = "UPDATE Accounts SET IsActive=$active WHERE Code=$code";
        cmd.Parameters.AddWithValue("$active", active ? 1 : 0);
        cmd.Parameters.AddWithValue("$code", code);
        if (cmd.ExecuteNonQuery() != 1) throw new InvalidOperationException("الحساب غير موجود.");
        Audit(db, active ? "تفعيل" : "إيقاف", "Account", code, "تغيير حالة الحساب");
    }

    public DataTable Parties(string kind = "All")
    {
        using var db = Database.Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = @"
SELECT Id AS 'المعرف', Name AS 'الاسم',
CASE Kind WHEN 'Customer' THEN 'عميل' WHEN 'Supplier' THEN 'مورد' ELSE 'عميل ومورد' END AS 'النوع',
COALESCE(Phone,'') AS 'الهاتف', COALESCE(VatNumber,'') AS 'الرقم الضريبي',
CASE IsActive WHEN 1 THEN 'نشط' ELSE 'موقوف' END AS 'الحالة'
FROM Parties WHERE ($kind='All' OR Kind=$kind OR Kind='Both') ORDER BY Name";
        cmd.Parameters.AddWithValue("$kind", kind);
        return ReadTable(cmd);
    }

    public void AddParty(string name, string kind, string phone, string vatNumber)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("اسم الطرف مطلوب.");
        if (!new[] { "Customer", "Supplier", "Both" }.Contains(kind)) throw new ArgumentException("نوع الطرف غير صالح.");
        using var db = Database.Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = "INSERT INTO Parties(Name,Kind,Phone,VatNumber,IsActive,CreatedAt) VALUES($name,$kind,$phone,$vat,1,$at)";
        cmd.Parameters.AddWithValue("$name", name.Trim());
        cmd.Parameters.AddWithValue("$kind", kind);
        cmd.Parameters.AddWithValue("$phone", Db(phone));
        cmd.Parameters.AddWithValue("$vat", Db(vatNumber));
        cmd.Parameters.AddWithValue("$at", Now());
        cmd.ExecuteNonQuery();
        Audit(db, "إضافة", "Party", LastInsertId(db).ToString(), $"إضافة الطرف {name.Trim()}");
    }

    public DataTable Products()
    {
        using var db = Database.Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = @"SELECT Id AS 'المعرف',Code AS 'رمز الصنف',Name AS 'اسم الصنف',Unit AS 'الوحدة',
printf('%.3f',Quantity) AS 'الكمية المتاحة',CostHalalas/100.0 AS 'التكلفة',PriceHalalas/100.0 AS 'سعر البيع',
VatRate AS 'الضريبة %',CASE IsActive WHEN 1 THEN 'نشط' ELSE 'موقوف' END AS 'الحالة'
FROM Products ORDER BY Code";
        return ReadTable(cmd);
    }

    public List<ProductInfo> ActiveProducts()
    {
        using var db = Database.Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = "SELECT Id,Code,Name,Unit,Quantity,CostHalalas,PriceHalalas,VatRate FROM Products WHERE IsActive=1 ORDER BY Code";
        using var reader = cmd.ExecuteReader();
        var result = new List<ProductInfo>();
        while (reader.Read())
            result.Add(new ProductInfo(reader.GetInt32(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
                Convert.ToDecimal(reader.GetDouble(4)), FromHalalas(reader.GetInt64(5)), FromHalalas(reader.GetInt64(6)), Convert.ToDecimal(reader.GetDouble(7))));
        return result;
    }

    public void AddProduct(string code, string name, string unit, decimal openingQuantity, decimal cost, decimal price, decimal vatRate)
    {
        if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(name)) throw new ArgumentException("رمز الصنف والاسم مطلوبان.");
        if (openingQuantity < 0 || cost < 0 || price < 0) throw new ArgumentException("لا يُسمح بقيم سالبة.");
        if (openingQuantity > 0 && cost <= 0) throw new ArgumentException("أدخل تكلفة موجبة عند إدخال رصيد افتتاحي للمخزون.");
        if (vatRate is < 0 or > 100) throw new ArgumentException("نسبة الضريبة يجب أن تكون بين 0 و100.");
        using var db = Database.Open();
        using var tx = db.BeginTransaction();
        try
        {
            using var cmd = db.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = @"INSERT INTO Products(Code,Name,Unit,Quantity,CostHalalas,PriceHalalas,VatRate,IsActive,CreatedAt)
VALUES($code,$name,$unit,$qty,$cost,$price,$vat,1,$at)";
            cmd.Parameters.AddWithValue("$code", code.Trim());
            cmd.Parameters.AddWithValue("$name", name.Trim());
            cmd.Parameters.AddWithValue("$unit", string.IsNullOrWhiteSpace(unit) ? "حبة" : unit.Trim());
            cmd.Parameters.AddWithValue("$qty", (double)openingQuantity);
            cmd.Parameters.AddWithValue("$cost", ToHalalas(cost));
            cmd.Parameters.AddWithValue("$price", ToHalalas(price));
            cmd.Parameters.AddWithValue("$vat", (double)vatRate);
            cmd.Parameters.AddWithValue("$at", Now());
            cmd.ExecuteNonQuery();
            var productId = LastInsertId(db, tx);

            if (openingQuantity > 0)
            {
                var openingValue = ToHalalas(openingQuantity * cost);
                var lines = new[]
                {
                    new JournalLineInput("1200", FromHalalas(openingValue), 0, "رصيد مخزون افتتاحي"),
                    new JournalLineInput("3000", 0, FromHalalas(openingValue), "مقابل الرصيد الافتتاحي")
                };
                InsertJournal(db, tx, DateTime.Today, $"رصيد افتتاحي للصنف {code.Trim()}", "OPENING_STOCK", lines);

                using var movement = db.CreateCommand();
                movement.Transaction = tx;
                movement.CommandText = @"INSERT INTO StockMovements(MovementDate,ProductId,Source,SourceId,QuantityIn,QuantityOut,UnitCostHalalas,Memo,CreatedAt)
VALUES($date,$product,'OPENING',$product,$qty,$zero,$cost,'رصيد افتتاحي',$at)";
                movement.Parameters.AddWithValue("$date", Date(DateTime.Today));
                movement.Parameters.AddWithValue("$product", productId);
                movement.Parameters.AddWithValue("$qty", (double)openingQuantity);
                movement.Parameters.AddWithValue("$zero", 0d);
                movement.Parameters.AddWithValue("$cost", ToHalalas(cost));
                movement.Parameters.AddWithValue("$at", Now());
                movement.ExecuteNonQuery();
            }
            Audit(db, tx, "إضافة", "Product", productId.ToString(), $"إضافة الصنف {name.Trim()}؛ رصيد افتتاحي={openingQuantity:0.###}");
            tx.Commit();
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 19)
        {
            throw new InvalidOperationException("رمز الصنف مستخدم مسبقًا أو توجد بيانات مرتبطة تمنع الحفظ.", ex);
        }
    }

    public long PostJournal(DateTime entryDate, string memo, IReadOnlyList<JournalLineInput> lines)
    {
        if (string.IsNullOrWhiteSpace(memo)) throw new ArgumentException("بيان القيد مطلوب.");
        ValidateBalanced(lines);
        using var db = Database.Open();
        using var tx = db.BeginTransaction();
        var id = InsertJournal(db, tx, entryDate, memo.Trim(), "MANUAL", lines);
        Audit(db, tx, "ترحيل", "Journal", id.ToString(), $"ترحيل قيد يدوي ({lines.Count} سطر)");
        tx.Commit();
        return id;
    }

    public long CreateInvoice(string kind, DateTime invoiceDate, int partyId, bool onCredit, IReadOnlyList<InvoiceLineInput> lines)
    {
        if (kind != "SALE" && kind != "PURCHASE") throw new ArgumentException("نوع الفاتورة غير صالح.");
        if (partyId <= 0) throw new ArgumentException("اختر العميل أو المورد.");
        if (lines.Count == 0) throw new ArgumentException("أضف صنفًا واحدًا على الأقل.");
        using var db = Database.Open();
        using var tx = db.BeginTransaction();

        using (var partyCmd = db.CreateCommand())
        {
            partyCmd.Transaction = tx;
            partyCmd.CommandText = "SELECT Kind FROM Parties WHERE Id=$id AND IsActive=1";
            partyCmd.Parameters.AddWithValue("$id", partyId);
            var partyKind = partyCmd.ExecuteScalar() as string;
            if (partyKind is null || (kind == "SALE" && partyKind == "Supplier") || (kind == "PURCHASE" && partyKind == "Customer"))
                throw new InvalidOperationException(kind == "SALE" ? "الطرف المحدد ليس عميلًا نشطًا." : "الطرف المحدد ليس موردًا نشطًا.");
        }

        decimal subtotal = 0, vatTotal = 0, cogsTotal = 0;
        var computed = new List<(InvoiceLineInput Input, string ProductCode, string ProductName, decimal VatRate, long UnitCost, long Net, long Vat, long Total, long CostTotal)>();
        foreach (var line in lines)
        {
            if (line.ProductId <= 0 || line.Quantity <= 0 || line.UnitPrice < 0) throw new ArgumentException("بيانات سطر الفاتورة غير صالحة.");
            using var productCmd = db.CreateCommand();
            productCmd.Transaction = tx;
            productCmd.CommandText = "SELECT Code,Name,Quantity,CostHalalas,PriceHalalas,VatRate,IsActive FROM Products WHERE Id=$id";
            productCmd.Parameters.AddWithValue("$id", line.ProductId);
            using var reader = productCmd.ExecuteReader();
            if (!reader.Read() || reader.GetInt64(6) != 1) throw new InvalidOperationException("يوجد صنف غير موجود أو موقوف.");
            var productCode = reader.GetString(0);
            var productName = reader.GetString(1);
            var quantity = Convert.ToDecimal(reader.GetDouble(2));
            var costHalalas = reader.GetInt64(3);
            var vatRate = Convert.ToDecimal(reader.GetDouble(5));
            reader.Close();
            if (kind == "SALE" && quantity < line.Quantity)
                throw new InvalidOperationException($"الكمية المتاحة من الصنف {productName} هي {quantity:0.###} فقط.");

            var netHalalas = ToHalalas(line.Quantity * line.UnitPrice);
            var vatHalalas = ToHalalas(FromHalalas(netHalalas) * vatRate / 100m);
            var lineTotalHalalas = checked(netHalalas + vatHalalas);
            var costTotal = ToHalalas(line.Quantity * FromHalalas(costHalalas));
            computed.Add((line, productCode, productName, vatRate, costHalalas, netHalalas, vatHalalas, lineTotalHalalas, costTotal));
            subtotal += FromHalalas(netHalalas);
            vatTotal += FromHalalas(vatHalalas);
            cogsTotal += FromHalalas(costTotal);
        }

        var subtotalHalalas = ToHalalas(subtotal);
        var vatHalalasTotal = ToHalalas(vatTotal);
        var totalHalalas = checked(subtotalHalalas + vatHalalasTotal);
        var number = (kind == "SALE" ? "S-" : "P-") + invoiceDate.ToString("yyyyMMdd") + "-" + Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
        var journalLines = new List<JournalLineInput>();
        if (kind == "SALE")
        {
            journalLines.Add(new JournalLineInput(onCredit ? "1100" : "1010", FromHalalas(totalHalalas), 0, "إجمالي فاتورة البيع", onCredit ? partyId : null));
            journalLines.Add(new JournalLineInput("4000", 0, FromHalalas(subtotalHalalas), "إيراد المبيعات"));
            if (vatHalalasTotal > 0) journalLines.Add(new JournalLineInput("2200", 0, FromHalalas(vatHalalasTotal), "ضريبة مخرجات"));
            if (cogsTotal > 0)
            {
                journalLines.Add(new JournalLineInput("5000", cogsTotal, 0, "تكلفة البضاعة المباعة"));
                journalLines.Add(new JournalLineInput("1200", 0, cogsTotal, "تخفيض المخزون بالتكلفة"));
            }
        }
        else
        {
            journalLines.Add(new JournalLineInput("1200", FromHalalas(subtotalHalalas), 0, "إضافة للمخزون"));
            if (vatHalalasTotal > 0) journalLines.Add(new JournalLineInput("1300", 0 + FromHalalas(vatHalalasTotal), 0, "ضريبة مدخلات"));
            journalLines.Add(new JournalLineInput(onCredit ? "2100" : "1010", 0, FromHalalas(totalHalalas), onCredit ? "مستحق للمورد" : "شراء نقدي", onCredit ? partyId : null));
        }
        ValidateBalanced(journalLines);
        var journalId = InsertJournal(db, tx, invoiceDate, $"فاتورة {(kind == "SALE" ? "بيع" : "شراء")} {number}", kind, journalLines);

        using (var invoiceCmd = db.CreateCommand())
        {
            invoiceCmd.Transaction = tx;
            invoiceCmd.CommandText = @"INSERT INTO Invoices(Kind,Number,InvoiceDate,PartyId,IsCredit,Status,SubtotalHalalas,VatHalalas,TotalHalalas,JournalId,CreatedAt)
VALUES($kind,$number,$date,$party,$credit,'POSTED',$subtotal,$vat,$total,$journal,$at)";
            invoiceCmd.Parameters.AddWithValue("$kind", kind);
            invoiceCmd.Parameters.AddWithValue("$number", number);
            invoiceCmd.Parameters.AddWithValue("$date", Date(invoiceDate));
            invoiceCmd.Parameters.AddWithValue("$party", partyId);
            invoiceCmd.Parameters.AddWithValue("$credit", onCredit ? 1 : 0);
            invoiceCmd.Parameters.AddWithValue("$subtotal", subtotalHalalas);
            invoiceCmd.Parameters.AddWithValue("$vat", vatHalalasTotal);
            invoiceCmd.Parameters.AddWithValue("$total", totalHalalas);
            invoiceCmd.Parameters.AddWithValue("$journal", journalId);
            invoiceCmd.Parameters.AddWithValue("$at", Now());
            invoiceCmd.ExecuteNonQuery();
        }
        var invoiceId = LastInsertId(db, tx);
        foreach (var item in computed)
        {
            using (var lineCmd = db.CreateCommand())
            {
                lineCmd.Transaction = tx;
                lineCmd.CommandText = @"INSERT INTO InvoiceLines(InvoiceId,ProductId,Quantity,UnitPriceHalalas,CostAtPostingHalalas,VatRate,NetHalalas,VatHalalas,TotalHalalas)
VALUES($invoice,$product,$qty,$price,$cost,$rate,$net,$vat,$total)";
                lineCmd.Parameters.AddWithValue("$invoice", invoiceId);
                lineCmd.Parameters.AddWithValue("$product", item.Input.ProductId);
                lineCmd.Parameters.AddWithValue("$qty", (double)item.Input.Quantity);
                lineCmd.Parameters.AddWithValue("$price", ToHalalas(item.Input.UnitPrice));
                lineCmd.Parameters.AddWithValue("$cost", item.UnitCost);
                lineCmd.Parameters.AddWithValue("$rate", (double)item.VatRate);
                lineCmd.Parameters.AddWithValue("$net", item.Net);
                lineCmd.Parameters.AddWithValue("$vat", item.Vat);
                lineCmd.Parameters.AddWithValue("$total", item.Total);
                lineCmd.ExecuteNonQuery();
            }
            using var stockCmd = db.CreateCommand();
            stockCmd.Transaction = tx;
            if (kind == "SALE")
            {
                stockCmd.CommandText = "UPDATE Products SET Quantity=Quantity-$qty WHERE Id=$id AND Quantity >= $qty";
            }
            else
            {
                // Weighted-average cost: value the remaining stock plus received quantity.
                stockCmd.CommandText = @"UPDATE Products SET CostHalalas=
CASE WHEN Quantity+$qty > 0 THEN CAST(((Quantity*CostHalalas)+($qty*$unitCost))/ (Quantity+$qty) AS INTEGER) ELSE CostHalalas END,
Quantity=Quantity+$qty WHERE Id=$id";
                stockCmd.Parameters.AddWithValue("$unitCost", ToHalalas(item.Input.UnitPrice));
            }
            stockCmd.Parameters.AddWithValue("$qty", (double)item.Input.Quantity);
            stockCmd.Parameters.AddWithValue("$id", item.Input.ProductId);
            if (stockCmd.ExecuteNonQuery() != 1) throw new InvalidOperationException("تعذر تحديث رصيد المخزون؛ تم إلغاء الفاتورة كاملة.");

            using var movement = db.CreateCommand();
            movement.Transaction = tx;
            movement.CommandText = @"INSERT INTO StockMovements(MovementDate,ProductId,Source,SourceId,QuantityIn,QuantityOut,UnitCostHalalas,Memo,CreatedAt)
VALUES($date,$product,$source,$sourceId,$in,$out,$cost,$memo,$at)";
            movement.Parameters.AddWithValue("$date", Date(invoiceDate));
            movement.Parameters.AddWithValue("$product", item.Input.ProductId);
            movement.Parameters.AddWithValue("$source", kind);
            movement.Parameters.AddWithValue("$sourceId", invoiceId.ToString());
            movement.Parameters.AddWithValue("$in", kind == "PURCHASE" ? (double)item.Input.Quantity : 0d);
            movement.Parameters.AddWithValue("$out", kind == "SALE" ? (double)item.Input.Quantity : 0d);
            movement.Parameters.AddWithValue("$cost", kind == "PURCHASE" ? ToHalalas(item.Input.UnitPrice) : item.UnitCost);
            movement.Parameters.AddWithValue("$memo", number);
            movement.Parameters.AddWithValue("$at", Now());
            movement.ExecuteNonQuery();
        }
        Audit(db, tx, "ترحيل", "Invoice", invoiceId.ToString(), $"ترحيل الفاتورة {number}; النوع={kind}; الإجمالي={FromHalalas(totalHalalas):0.00} SAR");
 main
        tx.Commit();
        return invoiceId;
    }

 desktop-no-license-window
    public static long PostPartyPayment(string kind, string number, DateTime date, int partyId, decimal amount, string method, string memo)
    {
        if (kind is not ("RECEIPT" or "PAYMENT")) throw new ArgumentException("نوع السند غير صحيح.");
        if (string.IsNullOrWhiteSpace(number)) throw new ArgumentException("رقم السند مطلوب.");
        long amountHalalas = ToHalalas(amount);
        if (amountHalalas <= 0) throw new ArgumentException("يجب أن يكون المبلغ أكبر من صفر.");
=======
    public DataTable TrialBalance(DateTime from, DateTime to)
    {
        using var db = Database.Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = @"
WITH Balances AS (
 SELECT a.Code,a.Name,
 COALESCE(SUM(CASE WHEN j.EntryDate < $from THEN l.DebitHalalas-l.CreditHalalas ELSE 0 END),0) AS OpeningBalance,
 COALESCE(SUM(CASE WHEN j.EntryDate >= $from AND j.EntryDate <= $to THEN l.DebitHalalas ELSE 0 END),0) AS PeriodDebit,
 COALESCE(SUM(CASE WHEN j.EntryDate >= $from AND j.EntryDate <= $to THEN l.CreditHalalas ELSE 0 END),0) AS PeriodCredit,
 COALESCE(SUM(CASE WHEN j.EntryDate <= $to THEN l.DebitHalalas-l.CreditHalalas ELSE 0 END),0) AS ClosingBalance
 FROM Accounts a LEFT JOIN JournalLines l ON l.AccountCode=a.Code
 LEFT JOIN Journals j ON j.Id=l.JournalId AND j.Status='POSTED' AND j.EntryDate <= $to
 GROUP BY a.Code,a.Name
)
SELECT Code AS 'رقم الحساب',Name AS 'الحساب',
ROUND(MAX(OpeningBalance,0)/100.0,2) AS 'افتتاحي مدين',
ROUND(MAX(-OpeningBalance,0)/100.0,2) AS 'افتتاحي دائن',
ROUND(PeriodDebit/100.0,2) AS 'إجمالي مدين',
ROUND(PeriodCredit/100.0,2) AS 'إجمالي دائن',
ROUND(MAX(ClosingBalance,0)/100.0,2) AS 'رصيد ختامي مدين',
ROUND(MAX(-ClosingBalance,0)/100.0,2) AS 'رصيد ختامي دائن'
FROM Balances ORDER BY Code";
        cmd.Parameters.AddWithValue("$from", Date(from));
        cmd.Parameters.AddWithValue("$to", Date(to));
        return ReadTable(cmd);
    }

    public DataTable ProfitAndLoss(DateTime from, DateTime to)
    {
        using var db = Database.Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = @"
SELECT a.Code AS 'رقم الحساب',a.Name AS 'الحساب',a.Type AS 'التصنيف',
ROUND(SUM(l.CreditHalalas-l.DebitHalalas)/100.0,2) AS 'صافي الحركة'
FROM Accounts a JOIN JournalLines l ON l.AccountCode=a.Code JOIN Journals j ON j.Id=l.JournalId
WHERE j.Status='POSTED' AND j.EntryDate >= $from AND j.EntryDate <= $to AND a.Type IN ('Revenue','Expense')
GROUP BY a.Code,a.Name,a.Type ORDER BY a.Type,a.Code";
        cmd.Parameters.AddWithValue("$from", Date(from));
        cmd.Parameters.AddWithValue("$to", Date(to));
        return ReadTable(cmd);
    }

    public DataTable BalanceSheet(DateTime asOf)
    {
        using var db = Database.Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = @"
WITH Bal AS (
 SELECT a.Code,a.Name,a.Type,
 COALESCE(SUM(CASE WHEN j.EntryDate <= $date THEN l.DebitHalalas-l.CreditHalalas ELSE 0 END),0) AS Signed
 FROM Accounts a LEFT JOIN JournalLines l ON l.AccountCode=a.Code
 LEFT JOIN Journals j ON j.Id=l.JournalId AND j.Status='POSTED'
 GROUP BY a.Code,a.Name,a.Type
), Profit AS (
 SELECT COALESCE(SUM(l.CreditHalalas-l.DebitHalalas),0) AS Net
 FROM Accounts a JOIN JournalLines l ON l.AccountCode=a.Code JOIN Journals j ON j.Id=l.JournalId
 WHERE j.Status='POSTED' AND j.EntryDate <= $date AND a.Type IN ('Revenue','Expense')
)
SELECT Code AS 'رقم الحساب',Name AS 'الحساب',
CASE WHEN Type='Asset' THEN 'الأصول' WHEN Type='Liability' THEN 'الالتزامات' ELSE 'حقوق الملكية' END AS 'القسم',
ROUND(CASE WHEN Type='Asset' THEN Signed WHEN Type='Liability' OR Type='Equity' THEN -Signed ELSE 0 END/100.0,2) AS 'الرصيد'
FROM Bal WHERE Type IN ('Asset','Liability','Equity') AND ABS(Signed)>0
UNION ALL
SELECT 'NET-INCOME','صافي الربح حتى التاريخ المحدد','حقوق الملكية',ROUND(Net/100.0,2) FROM Profit WHERE Net<>0
ORDER BY 3,1";
        cmd.Parameters.AddWithValue("$date", Date(asOf));
        return ReadTable(cmd);
    }

    public DataTable Ledger(string accountCode, DateTime from, DateTime to)
    {
        using var db = Database.Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = @"
WITH Movements AS (
 SELECT j.EntryDate,j.Number,j.Memo,l.DebitHalalas,l.CreditHalalas,j.Id AS JournalId,l.Id AS LineId,
 SUM(l.DebitHalalas-l.CreditHalalas) OVER(ORDER BY j.EntryDate,j.Id,l.Id ROWS BETWEEN UNBOUNDED PRECEDING AND CURRENT ROW) AS RunningBalance
 FROM JournalLines l JOIN Journals j ON j.Id=l.JournalId
 WHERE l.AccountCode=$code AND j.Status='POSTED' AND j.EntryDate <= $to
)
SELECT EntryDate AS 'التاريخ',Number AS 'رقم القيد',Memo AS 'البيان',
ROUND(DebitHalalas/100.0,2) AS 'مدين',ROUND(CreditHalalas/100.0,2) AS 'دائن',
ROUND(RunningBalance/100.0,2) AS 'الرصيد المتحرك'
FROM Movements WHERE EntryDate >= $from ORDER BY EntryDate,JournalId,LineId";
        cmd.Parameters.AddWithValue("$code", accountCode);
        cmd.Parameters.AddWithValue("$from", Date(from));
        cmd.Parameters.AddWithValue("$to", Date(to));
        return ReadTable(cmd);
    }

    public DataTable StockValuation()
    {
        using var db = Database.Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = @"SELECT Code AS 'رمز الصنف',Name AS 'الصنف',Unit AS 'الوحدة',
ROUND(Quantity,3) AS 'الكمية',ROUND(CostHalalas/100.0,2) AS 'تكلفة الوحدة',
ROUND(Quantity*CostHalalas/100.0,2) AS 'قيمة المخزون',ROUND(PriceHalalas/100.0,2) AS 'سعر البيع'
FROM Products ORDER BY Code";
        return ReadTable(cmd);
    }

    public DataTable VatSummary(DateTime from, DateTime to)
    {
        using var db = Database.Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = @"
WITH TaxDocuments AS (
 SELECT Kind,SubtotalHalalas,VatHalalas
 FROM Invoices
 WHERE Status='POSTED' AND InvoiceDate >= $from AND InvoiceDate <= $to
 UNION ALL
 SELECT CASE Kind WHEN 'SALE_RETURN' THEN 'SALE' ELSE 'PURCHASE' END,
        -SubtotalHalalas,-VatHalalas
 FROM Returns
 WHERE Status='POSTED' AND ReturnDate >= $from AND ReturnDate <= $to
)
SELECT
ROUND(COALESCE(SUM(CASE WHEN Kind='SALE' THEN SubtotalHalalas ELSE 0 END),0)/100.0,2) AS 'مبيعات خاضعة للضريبة قبل الضريبة',
ROUND(COALESCE(SUM(CASE WHEN Kind='SALE' THEN VatHalalas ELSE 0 END),0)/100.0,2) AS 'ضريبة المخرجات',
ROUND(COALESCE(SUM(CASE WHEN Kind='PURCHASE' THEN SubtotalHalalas ELSE 0 END),0)/100.0,2) AS 'مشتريات قبل الضريبة',
ROUND(COALESCE(SUM(CASE WHEN Kind='PURCHASE' THEN VatHalalas ELSE 0 END),0)/100.0,2) AS 'ضريبة المدخلات',
ROUND((COALESCE(SUM(CASE WHEN Kind='SALE' THEN VatHalalas ELSE 0 END),0)
      - COALESCE(SUM(CASE WHEN Kind='PURCHASE' THEN VatHalalas ELSE 0 END),0))/100.0,2) AS 'صافي الضريبة المستحقة',
(SELECT COUNT(*) FROM Invoices WHERE Status='POSTED' AND InvoiceDate >= $from AND InvoiceDate <= $to)
+ (SELECT COUNT(*) FROM Returns WHERE Status='POSTED' AND ReturnDate >= $from AND ReturnDate <= $to) AS 'عدد الفواتير والمردودات'
FROM TaxDocuments";
        cmd.Parameters.AddWithValue("$from", Date(from));
        cmd.Parameters.AddWithValue("$to", Date(to));
        return ReadTable(cmd);
    }

    public DataTable StockReconciliation()
    {
        using var db = Database.Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = @"
SELECT p.Code AS 'رمز الصنف',p.Name AS 'الصنف',ROUND(p.Quantity,3) AS 'الكمية الحالية',
ROUND(COALESCE(SUM(m.QuantityIn-m.QuantityOut),0),3) AS 'صافي الحركات',
ROUND(p.Quantity-COALESCE(SUM(m.QuantityIn-m.QuantityOut),0),3) AS 'فرق التسوية'
FROM Products p LEFT JOIN StockMovements m ON m.ProductId=p.Id
GROUP BY p.Id,p.Code,p.Name,p.Quantity
HAVING ABS(p.Quantity-COALESCE(SUM(m.QuantityIn-m.QuantityOut),0)) > 0.0005
ORDER BY p.Code";
        return ReadTable(cmd);
    }

    public DataTable InvoiceRegister(string kind, DateTime from, DateTime to)
    {
        using var db = Database.Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = @"SELECT i.Number AS 'رقم الفاتورة',i.InvoiceDate AS 'التاريخ',p.Name AS 'العميل/المورد',
CASE i.IsCredit WHEN 1 THEN 'آجل' ELSE 'نقدي' END AS 'طريقة السداد',
ROUND(i.SubtotalHalalas/100.0,2) AS 'قبل الضريبة',ROUND(i.VatHalalas/100.0,2) AS 'الضريبة',
ROUND(i.TotalHalalas/100.0,2) AS 'الإجمالي',i.JournalId AS 'رقم القيد'
FROM Invoices i JOIN Parties p ON p.Id=i.PartyId
WHERE i.Kind=$kind AND i.InvoiceDate >= $from AND i.InvoiceDate <= $to AND i.Status='POSTED'
ORDER BY i.InvoiceDate,i.Id";
        cmd.Parameters.AddWithValue("$kind", kind);
        cmd.Parameters.AddWithValue("$from", Date(from));
        cmd.Parameters.AddWithValue("$to", Date(to));
        return ReadTable(cmd);
    }

    public DataTable PartyBalances(DateTime from, DateTime to)
    {
        using var db = Database.Open();
        using var cmd = db.CreateCommand();
        // Subledger balances are calculated from the posted journal lines that carry each party's ID.
        cmd.CommandText = @"
SELECT p.Id AS 'المعرف',p.Name AS 'الطرف',
ROUND(COALESCE(SUM(CASE WHEN l.AccountCode='1100' AND j.EntryDate < $from THEN l.DebitHalalas-l.CreditHalalas ELSE 0 END),0)/100.0,2) AS 'رصيد العملاء أول المدة',
ROUND(COALESCE(SUM(CASE WHEN l.AccountCode='1100' AND j.EntryDate >= $from AND j.EntryDate <= $to THEN l.DebitHalalas-l.CreditHalalas ELSE 0 END),0)/100.0,2) AS 'صافي حركة العملاء',
ROUND(COALESCE(SUM(CASE WHEN l.AccountCode='1100' THEN l.DebitHalalas-l.CreditHalalas ELSE 0 END),0)/100.0,2) AS 'رصيد العملاء آخر المدة',
ROUND(COALESCE(SUM(CASE WHEN l.AccountCode='2100' AND j.EntryDate < $from THEN l.CreditHalalas-l.DebitHalalas ELSE 0 END),0)/100.0,2) AS 'رصيد الموردين أول المدة',
ROUND(COALESCE(SUM(CASE WHEN l.AccountCode='2100' AND j.EntryDate >= $from AND j.EntryDate <= $to THEN l.CreditHalalas-l.DebitHalalas ELSE 0 END),0)/100.0,2) AS 'صافي حركة الموردين',
ROUND(COALESCE(SUM(CASE WHEN l.AccountCode='2100' THEN l.CreditHalalas-l.DebitHalalas ELSE 0 END),0)/100.0,2) AS 'رصيد الموردين آخر المدة'
FROM Parties p
LEFT JOIN JournalLines l ON l.PartyId=p.Id
LEFT JOIN Journals j ON j.Id=l.JournalId AND j.Status='POSTED' AND j.EntryDate <= $to
GROUP BY p.Id,p.Name ORDER BY p.Name";
        cmd.Parameters.AddWithValue("$from", Date(from));
        cmd.Parameters.AddWithValue("$to", Date(to));
        return ReadTable(cmd);
    }

    public DataTable StockMovementRegister(DateTime from, DateTime to)
    {
        using var db = Database.Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = @"SELECT m.MovementDate AS 'التاريخ',p.Code AS 'رمز الصنف',p.Name AS 'الصنف',
m.Source AS 'نوع الحركة',m.SourceId AS 'مرجع المصدر',ROUND(m.QuantityIn,3) AS 'وارد',
ROUND(m.QuantityOut,3) AS 'صادر',ROUND(m.UnitCostHalalas/100.0,2) AS 'تكلفة الوحدة',
ROUND((m.QuantityIn-m.QuantityOut)*m.UnitCostHalalas/100.0,2) AS 'قيمة الحركة',m.Memo AS 'البيان'
FROM StockMovements m JOIN Products p ON p.Id=m.ProductId
WHERE m.MovementDate >= $from AND m.MovementDate <= $to ORDER BY m.MovementDate,m.Id";
        cmd.Parameters.AddWithValue("$from", Date(from));
        cmd.Parameters.AddWithValue("$to", Date(to));
        return ReadTable(cmd);
    }

    public DataTable SettlementRegister(string kind, DateTime from, DateTime to)
    {
        using var db = Database.Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = @"SELECT s.Number AS 'رقم السند',s.SettlementDate AS 'التاريخ',
CASE s.Kind WHEN 'RECEIPT' THEN 'قبض' ELSE 'صرف' END AS 'النوع',p.Name AS 'الطرف',
s.CashAccountCode AS 'حساب النقدية',ROUND(s.AmountHalalas/100.0,2) AS 'المبلغ',
s.Memo AS 'البيان',s.JournalId AS 'رقم القيد'
FROM Settlements s JOIN Parties p ON p.Id=s.PartyId
WHERE ($kind='ALL' OR s.Kind=$kind) AND s.SettlementDate >= $from AND s.SettlementDate <= $to
ORDER BY s.SettlementDate,s.Id";
        cmd.Parameters.AddWithValue("$kind", kind);
        cmd.Parameters.AddWithValue("$from", Date(from));
        cmd.Parameters.AddWithValue("$to", Date(to));
        return ReadTable(cmd);
    }

    public long CreateSettlement(string kind, DateTime settlementDate, int partyId, string cashAccountCode, decimal amount, string memo)
    {
        if (kind is not ("RECEIPT" or "PAYMENT")) throw new ArgumentException("نوع السند غير صالح.");
        if (partyId <= 0) throw new ArgumentException("اختر العميل أو المورد.");
        var amountHalalas = ToHalalas(amount);
        if (amountHalalas <= 0) throw new ArgumentException("قيمة السند يجب أن تكون أكبر من صفر.");
        if (cashAccountCode is not ("1010" or "1020")) throw new ArgumentException("اختر الصندوق أو البنك.");
        if (string.IsNullOrWhiteSpace(memo)) memo = kind == "RECEIPT" ? "سند قبض" : "سند صرف";
main

        using var db = Database.Open();
        using var tx = db.BeginTransaction();
        using (var party = db.CreateCommand())
        {
            party.Transaction = tx;
 desktop-no-license-window
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
=======
            party.CommandText = "SELECT Kind FROM Parties WHERE Id=$id AND IsActive=1";
            party.Parameters.AddWithValue("$id", partyId);
            var partyKind = party.ExecuteScalar() as string;
            if (partyKind is null || (kind == "RECEIPT" && partyKind == "Supplier") || (kind == "PAYMENT" && partyKind == "Customer"))
                throw new InvalidOperationException(kind == "RECEIPT" ? "سند القبض يتطلب عميلًا نشطًا." : "سند الصرف يتطلب موردًا نشطًا.");
        }
        var lines = kind == "RECEIPT"
            ? new[] { new JournalLineInput(cashAccountCode, FromHalalas(amountHalalas), 0, "قبض من عميل"), new JournalLineInput("1100", 0, FromHalalas(amountHalalas), "تخفيض ذمم العملاء", partyId) }
            : new[] { new JournalLineInput("2100", FromHalalas(amountHalalas), 0, "تخفيض ذمم الموردين", partyId), new JournalLineInput(cashAccountCode, 0, FromHalalas(amountHalalas), "صرف لمورد") };
        var number = (kind == "RECEIPT" ? "RC-" : "PY-") + settlementDate.ToString("yyyyMMdd") + "-" + Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
        var journalId = InsertJournal(db, tx, settlementDate, $"{(kind == "RECEIPT" ? "سند قبض" : "سند صرف")} {number}", kind, lines);
        using (var cmd = db.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = @"INSERT INTO Settlements(Kind,Number,SettlementDate,PartyId,AmountHalalas,CashAccountCode,JournalId,Memo,CreatedAt)
VALUES($kind,$number,$date,$party,$amount,$cash,$journal,$memo,$at)";
            cmd.Parameters.AddWithValue("$kind", kind);
            cmd.Parameters.AddWithValue("$number", number);
            cmd.Parameters.AddWithValue("$date", Date(settlementDate));
            cmd.Parameters.AddWithValue("$party", partyId);
            cmd.Parameters.AddWithValue("$amount", amountHalalas);
            cmd.Parameters.AddWithValue("$cash", cashAccountCode);
            cmd.Parameters.AddWithValue("$journal", journalId);
            cmd.Parameters.AddWithValue("$memo", memo.Trim());
            cmd.Parameters.AddWithValue("$at", Now());
            cmd.ExecuteNonQuery();
        }
        var id = LastInsertId(db, tx);
        Audit(db, tx, "ترحيل", "Settlement", id.ToString(), $"ترحيل {number} بقيمة {FromHalalas(amountHalalas):0.00} ر.س");
        tx.Commit();
        return id;
    }

    public DataTable RecentJournals()
    {
        using var db = Database.Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = @"SELECT j.Number AS 'رقم القيد',j.EntryDate AS 'التاريخ',j.Memo AS 'البيان',
j.Source AS 'المصدر',ROUND(SUM(l.DebitHalalas)/100.0,2) AS 'إجمالي المدين',
ROUND(SUM(l.CreditHalalas)/100.0,2) AS 'إجمالي الدائن',j.Status AS 'الحالة'
FROM Journals j JOIN JournalLines l ON l.JournalId=j.Id
GROUP BY j.Id,j.Number,j.EntryDate,j.Memo,j.Source,j.Status
ORDER BY j.Id DESC LIMIT 500";
        return ReadTable(cmd);
    }

    public DataTable AuditHistory()
    {
        using var db = Database.Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = @"SELECT EventAt AS 'الوقت',Actor AS 'المستخدم',Action AS 'العملية',Entity AS 'الكيان',
EntityId AS 'المعرف',Details AS 'التفاصيل' FROM AuditLog ORDER BY Id DESC LIMIT 1000";
        return ReadTable(cmd);
    }

    public DashboardInfo Dashboard(DateTime today)
    {
        using var db = Database.Open();
        decimal cash = BalanceByAccount(db, "1010") + BalanceByAccount(db, "1020");
        decimal receivables = BalanceByAccount(db, "1100");
        decimal payables = -BalanceByAccount(db, "2100");
        using var cmd = db.CreateCommand();
        cmd.CommandText = @"SELECT
COALESCE(SUM(CASE WHEN a.Type='Revenue' THEN l.CreditHalalas-l.DebitHalalas ELSE 0 END),0),
COALESCE(SUM(CASE WHEN a.Type='Expense' THEN l.DebitHalalas-l.CreditHalalas ELSE 0 END),0),
COUNT(DISTINCT j.Id)
FROM Journals j JOIN JournalLines l ON l.JournalId=j.Id JOIN Accounts a ON a.Code=l.AccountCode
WHERE j.Status='POSTED' AND j.EntryDate >= $from AND j.EntryDate <= $to";
        cmd.Parameters.AddWithValue("$from", today.ToString("yyyy-MM-01"));
        cmd.Parameters.AddWithValue("$to", Date(today));
        using var reader = cmd.ExecuteReader();
        reader.Read();
        return new DashboardInfo(cash, receivables, payables, FromHalalas(reader.GetInt64(0)), FromHalalas(reader.GetInt64(1)), reader.GetInt32(2));
    }

    private static decimal BalanceByAccount(SqliteConnection db, string code)
    {
        using var cmd = db.CreateCommand();
        cmd.CommandText = "SELECT COALESCE(SUM(l.DebitHalalas-l.CreditHalalas),0)/100.0 FROM JournalLines l JOIN Journals j ON j.Id=l.JournalId WHERE l.AccountCode=$code AND j.Status='POSTED'";
        cmd.Parameters.AddWithValue("$code", code);
        return Convert.ToDecimal(cmd.ExecuteScalar());
    }

    private static long InsertJournal(SqliteConnection db, SqliteTransaction tx, DateTime entryDate, string memo, string source, IReadOnlyList<JournalLineInput> lines)
    {
        ValidateBalanced(lines);
        var number = "JV-" + entryDate.ToString("yyyyMMdd") + "-" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        using (var cmd = db.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "INSERT INTO Journals(Number,EntryDate,Memo,Source,Status,CreatedAt) VALUES($n,$date,$memo,$source,'POSTED',$at)";
            cmd.Parameters.AddWithValue("$n", number);
            cmd.Parameters.AddWithValue("$date", Date(entryDate));
            cmd.Parameters.AddWithValue("$memo", memo);
            cmd.Parameters.AddWithValue("$source", source);
            cmd.Parameters.AddWithValue("$at", Now());
            cmd.ExecuteNonQuery();
        }
        var id = LastInsertId(db, tx);
        foreach (var line in lines)
        {
            if (line.Debit < 0 || line.Credit < 0 || (line.Debit > 0 && line.Credit > 0) || (line.Debit == 0 && line.Credit == 0))
                throw new ArgumentException("كل سطر يجب أن يكون مدينًا أو دائنًا بقيمة موجبة، وليس الاثنين معًا.");
            using var account = db.CreateCommand();
            account.Transaction = tx;
            account.CommandText = "SELECT IsActive FROM Accounts WHERE Code=$code";
            account.Parameters.AddWithValue("$code", line.AccountCode);
            var active = account.ExecuteScalar();
            if (active is null || active == DBNull.Value || Convert.ToInt64(active) != 1)
                throw new InvalidOperationException($"الحساب {line.AccountCode} غير موجود أو موقوف.");
            if (line.AccountCode is "1100" or "2100")
            {
                if (!line.PartyId.HasValue)
                    throw new InvalidOperationException($"يجب تحديد العميل/المورد لسطر حساب الذمم {line.AccountCode}.");
                using var party = db.CreateCommand();
                party.Transaction = tx;
                party.CommandText = "SELECT Kind,IsActive FROM Parties WHERE Id=$id";
                party.Parameters.AddWithValue("$id", line.PartyId.Value);
                using var partyReader = party.ExecuteReader();
                if (!partyReader.Read() || partyReader.GetInt64(1) != 1)
                    throw new InvalidOperationException("الطرف المرتبط بالذمم غير موجود أو موقوف.");
                var partyKind = partyReader.GetString(0);
                if ((line.AccountCode == "1100" && partyKind == "Supplier") ||
                    (line.AccountCode == "2100" && partyKind == "Customer"))
                    throw new InvalidOperationException("نوع الطرف لا يتوافق مع حساب الذمم المحدد.");
            }
            else if (line.PartyId.HasValue)
            {
                throw new InvalidOperationException("يمكن ربط الطرف فقط بأسطر حساب العملاء أو الموردين 1100 و2100.");
            }

            using var detail = db.CreateCommand();
            detail.Transaction = tx;
            detail.CommandText = "INSERT INTO JournalLines(JournalId,AccountCode,PartyId,Memo,DebitHalalas,CreditHalalas) VALUES($id,$account,$party,$memo,$debit,$credit)";
            detail.Parameters.AddWithValue("$id", id);
            detail.Parameters.AddWithValue("$account", line.AccountCode);
            detail.Parameters.AddWithValue("$party", line.PartyId.HasValue ? (object)line.PartyId.Value : DBNull.Value);
            detail.Parameters.AddWithValue("$memo", line.Memo ?? "");
            detail.Parameters.AddWithValue("$debit", ToHalalas(line.Debit));
            detail.Parameters.AddWithValue("$credit", ToHalalas(line.Credit));
            detail.ExecuteNonQuery();
        }
        return id;
    }

    private static void ValidateBalanced(IReadOnlyList<JournalLineInput> lines)
    {
        if (lines.Count < 2) throw new ArgumentException("القيد يجب أن يحتوي على سطرين على الأقل.");
        long debit = 0, credit = 0;
        foreach (var line in lines)
        {
            if (line.Debit < 0 || line.Credit < 0 || (line.Debit > 0 && line.Credit > 0) || (line.Debit == 0 && line.Credit == 0))
                throw new ArgumentException("كل سطر يجب أن يحتوي على مدين أو دائن فقط وبقيمة موجبة.");
            debit = checked(debit + ToHalalas(line.Debit));
            credit = checked(credit + ToHalalas(line.Credit));
        }
        if (debit <= 0 || debit != credit)
            throw new InvalidOperationException($"القيد غير متوازن. المدين {FromHalalas(debit):N2} ر.س، الدائن {FromHalalas(credit):N2} ر.س.");
    }

    private static void Audit(SqliteConnection db, string action, string entity, string entityId, string details)
    {
        using var cmd = db.CreateCommand();
        cmd.CommandText = "INSERT INTO AuditLog(EventAt,Actor,Action,Entity,EntityId,Details) VALUES($at,$actor,$action,$entity,$id,$details)";
        cmd.Parameters.AddWithValue("$at", Now());
        cmd.Parameters.AddWithValue("$actor", Environment.UserName);
        cmd.Parameters.AddWithValue("$action", action);
        cmd.Parameters.AddWithValue("$entity", entity);
        cmd.Parameters.AddWithValue("$id", entityId);
main
        cmd.Parameters.AddWithValue("$details", details);
        cmd.ExecuteNonQuery();
    }

 desktop-no-license-window
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
=======
    private static void Audit(SqliteConnection db, SqliteTransaction tx, string action, string entity, string entityId, string details)
    {
        using var cmd = db.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "INSERT INTO AuditLog(EventAt,Actor,Action,Entity,EntityId,Details) VALUES($at,$actor,$action,$entity,$id,$details)";
        cmd.Parameters.AddWithValue("$at", Now());
        cmd.Parameters.AddWithValue("$actor", Environment.UserName);
        cmd.Parameters.AddWithValue("$action", action);
        cmd.Parameters.AddWithValue("$entity", entity);
        cmd.Parameters.AddWithValue("$id", entityId);
        cmd.Parameters.AddWithValue("$details", details);
        cmd.ExecuteNonQuery();
    }

    private static long LastInsertId(SqliteConnection db, SqliteTransaction? tx = null)
    {
        using var cmd = db.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT last_insert_rowid()";
        return Convert.ToInt64(cmd.ExecuteScalar());
    }

    private static object Db(string? value) => string.IsNullOrWhiteSpace(value) ? DBNull.Value : value.Trim();

    private static DataTable ReadTable(SqliteCommand command)
    {
        using var reader = command.ExecuteReader();
        var table = new DataTable();
        for (var i = 0; i < reader.FieldCount; i++)
        {
            var columnType = reader.GetFieldType(i);
            table.Columns.Add(reader.GetName(i), columnType ?? typeof(object));
        }
        while (reader.Read())
        {
            var values = new object[reader.FieldCount];
            reader.GetValues(values);
            table.Rows.Add(values);
        }
        return table;
    }
 main
}
