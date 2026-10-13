using System.Data;
using Microsoft.Data.Sqlite;
using AlSaqr.Accounting.Data;

namespace AlSaqr.Accounting.Services;

internal sealed record JournalLineInput(string AccountCode, decimal Debit, decimal Credit, string Memo = "");
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
        Audit(db, "إضافة", "Party", db.LastInsertRowId.ToString(), $"إضافة الطرف {name.Trim()}");
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
        if (vatRate is < 0 or > 100) throw new ArgumentException("نسبة الضريبة يجب أن تكون بين 0 و100.");
        using var db = Database.Open();
        using var cmd = db.CreateCommand();
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
        try { cmd.ExecuteNonQuery(); }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 19) { throw new InvalidOperationException("رمز الصنف مستخدم مسبقًا.", ex); }
        Audit(db, "إضافة", "Product", db.LastInsertRowId.ToString(), $"إضافة الصنف {name.Trim()}");
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
            var totalHalalas = checked(netHalalas + vatHalalas);
            var costTotal = ToHalalas(line.Quantity * FromHalalas(costHalalas));
            computed.Add((line, productCode, productName, vatRate, costHalalas, netHalalas, vatHalalas, totalHalalas, costTotal));
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
            journalLines.Add(new JournalLineInput(onCredit ? "1100" : "1010", FromHalalas(totalHalalas), 0, "إجمالي فاتورة البيع"));
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
            journalLines.Add(new JournalLineInput(onCredit ? "2100" : "1010", 0, FromHalalas(totalHalalas), onCredit ? "مستحق للمورد" : "شراء نقدي"));
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
        var invoiceId = db.LastInsertRowId;
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
        }
        Audit(db, tx, "ترحيل", "Invoice", invoiceId.ToString(), $"ترحيل الفاتورة {number}; النوع={kind}; الإجمالي={FromHalalas(totalHalalas):0.00} SAR");
        tx.Commit();
        return invoiceId;
    }

    public DataTable TrialBalance(DateTime from, DateTime to)
    {
        using var db = Database.Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = @"
SELECT a.Code AS 'رقم الحساب',a.Name AS 'الحساب',
ROUND(COALESCE(SUM(CASE WHEN j.Id IS NOT NULL THEN l.DebitHalalas ELSE 0 END),0)/100.0,2) AS 'إجمالي مدين',
ROUND(COALESCE(SUM(CASE WHEN j.Id IS NOT NULL THEN l.CreditHalalas ELSE 0 END),0)/100.0,2) AS 'إجمالي دائن',
ROUND(MAX(COALESCE(SUM(CASE WHEN j.Id IS NOT NULL THEN l.DebitHalalas-l.CreditHalalas ELSE 0 END),0),0)/100.0,2) AS 'رصيد مدين',
ROUND(MAX(-COALESCE(SUM(CASE WHEN j.Id IS NOT NULL THEN l.DebitHalalas-l.CreditHalalas ELSE 0 END),0),0)/100.0,2) AS 'رصيد دائن'
FROM Accounts a LEFT JOIN JournalLines l ON l.AccountCode=a.Code
LEFT JOIN Journals j ON j.Id=l.JournalId AND j.Status='POSTED' AND j.EntryDate >= $from AND j.EntryDate <= $to
GROUP BY a.Code,a.Name ORDER BY a.Code";
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
SELECT j.EntryDate AS 'التاريخ',j.Number AS 'رقم القيد',j.Memo AS 'البيان',
ROUND(l.DebitHalalas/100.0,2) AS 'مدين',ROUND(l.CreditHalalas/100.0,2) AS 'دائن',
ROUND(SUM(l.DebitHalalas-l.CreditHalalas) OVER(ORDER BY j.EntryDate,j.Id,l.Id ROWS BETWEEN UNBOUNDED PRECEDING AND CURRENT ROW)/100.0,2) AS 'الرصيد المتحرك'
FROM JournalLines l JOIN Journals j ON j.Id=l.JournalId
WHERE l.AccountCode=$code AND j.Status='POSTED' AND j.EntryDate >= $from AND j.EntryDate <= $to
ORDER BY j.EntryDate,j.Id,l.Id";
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
        cmd.CommandText = @"
SELECT p.Id AS 'المعرف',p.Name AS 'الطرف',
SUM(CASE WHEN i.Kind='SALE' THEN i.TotalHalalas ELSE 0 END)/100.0 AS 'فواتير البيع',
SUM(CASE WHEN i.Kind='PURCHASE' THEN i.TotalHalalas ELSE 0 END)/100.0 AS 'فواتير الشراء',
COUNT(i.Id) AS 'عدد الفواتير'
FROM Parties p LEFT JOIN Invoices i ON i.PartyId=p.Id AND i.Status='POSTED'
AND i.InvoiceDate >= $from AND i.InvoiceDate <= $to GROUP BY p.Id,p.Name ORDER BY p.Name";
        cmd.Parameters.AddWithValue("$from", Date(from));
        cmd.Parameters.AddWithValue("$to", Date(to));
        return ReadTable(cmd);
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
        var id = db.LastInsertRowId;
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
            using var detail = db.CreateCommand();
            detail.Transaction = tx;
            detail.CommandText = "INSERT INTO JournalLines(JournalId,AccountCode,Memo,DebitHalalas,CreditHalalas) VALUES($id,$account,$memo,$debit,$credit)";
            detail.Parameters.AddWithValue("$id", id);
            detail.Parameters.AddWithValue("$account", line.AccountCode);
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
        cmd.Parameters.AddWithValue("$details", details);
        cmd.ExecuteNonQuery();
    }

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

    private static object Db(string? value) => string.IsNullOrWhiteSpace(value) ? DBNull.Value : value.Trim();

    private static DataTable ReadTable(SqliteCommand command)
    {
        using var reader = command.ExecuteReader();
        var table = new DataTable();
        table.Load(reader);
        return table;
    }
}
