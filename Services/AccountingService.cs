using System.Data;
using System.Globalization;
using Microsoft.Data.Sqlite;
using AlSaqr.Accounting.Data;

namespace AlSaqr.Accounting.Services;

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
        if (!active && new[] { "1010","1020","1100","1200","1300","2100","2200","3000","3100","4000","4010","4020","4090","5000","5010","5050","6000" }.Contains(code))
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

    public void UpdateParty(int id, string name, string kind, string phone, string vatNumber)
    {
        if (id <= 0) throw new ArgumentException("معرف الطرف غير صالح.");
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("اسم الطرف مطلوب.");
        if (!new[] { "Customer", "Supplier", "Both" }.Contains(kind))
            throw new ArgumentException("نوع الطرف غير صالح.");

        using var db = Database.Open();
        using var tx = db.BeginTransaction();
        using var cmd = db.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"UPDATE Parties SET Name=$name,Kind=$kind,Phone=$phone,VatNumber=$vat WHERE Id=$id";
        cmd.Parameters.AddWithValue("$name", name.Trim());
        cmd.Parameters.AddWithValue("$kind", kind);
        cmd.Parameters.AddWithValue("$phone", Db(phone));
        cmd.Parameters.AddWithValue("$vat", Db(vatNumber));
        cmd.Parameters.AddWithValue("$id", id);
        if (cmd.ExecuteNonQuery() != 1) throw new InvalidOperationException("العميل أو المورد غير موجود.");
        Audit(db, tx, "تعديل", "Party", id.ToString(CultureInfo.InvariantCulture), $"تعديل بيانات الطرف {name.Trim()}");
        tx.Commit();
    }

    public void SetPartyActive(int id, bool active)
    {
        if (id <= 0) throw new ArgumentException("معرف الطرف غير صالح.");
        using var db = Database.Open();
        using var tx = db.BeginTransaction();
        using var cmd = db.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "UPDATE Parties SET IsActive=$active WHERE Id=$id";
        cmd.Parameters.AddWithValue("$active", active ? 1 : 0);
        cmd.Parameters.AddWithValue("$id", id);
        if (cmd.ExecuteNonQuery() != 1) throw new InvalidOperationException("العميل أو المورد غير موجود.");
        Audit(db, tx, active ? "تفعيل" : "إيقاف", "Party", id.ToString(CultureInfo.InvariantCulture), "تغيير حالة الطرف");
        tx.Commit();
    }

    // Product quantity and valuation cost are not editable through the master-data form.
    // Quantity changes go through invoices, linked returns, or the audited stock-adjustment workflow.
    public void UpdateProduct(int id, string code, string name, string unit, decimal price, decimal vatRate)
    {
        if (id <= 0) throw new ArgumentException("معرف الصنف غير صالح.");
        if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("رمز الصنف والاسم مطلوبان.");
        if (price < 0) throw new ArgumentException("سعر البيع لا يمكن أن يكون سالبًا.");
        if (vatRate is < 0 or > 100) throw new ArgumentException("نسبة الضريبة يجب أن تكون بين صفر و100.");

        using var db = Database.Open();
        using var tx = db.BeginTransaction();
        using var cmd = db.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"UPDATE Products SET Code=$code,Name=$name,Unit=$unit,PriceHalalas=$price,VatRate=$vat
WHERE Id=$id AND IsActive IN (0,1)";
        cmd.Parameters.AddWithValue("$code", code.Trim());
        cmd.Parameters.AddWithValue("$name", name.Trim());
        cmd.Parameters.AddWithValue("$unit", string.IsNullOrWhiteSpace(unit) ? "حبة" : unit.Trim());
        cmd.Parameters.AddWithValue("$price", ToHalalas(price));
        cmd.Parameters.AddWithValue("$vat", (double)vatRate);
        cmd.Parameters.AddWithValue("$id", id);
        try
        {
            if (cmd.ExecuteNonQuery() != 1) throw new InvalidOperationException("الصنف غير موجود.");
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 19)
        {
            throw new InvalidOperationException("رمز الصنف مستخدم في سجل آخر.", ex);
        }
        Audit(db, tx, "تعديل", "Product", id.ToString(CultureInfo.InvariantCulture), $"تعديل الصنف {name.Trim()}");
        tx.Commit();
    }

    public void SetProductActive(int id, bool active)
    {
        using var db = Database.Open();
        using var tx = db.BeginTransaction();
        using (var check = db.CreateCommand())
        {
            check.Transaction = tx;
            check.CommandText = "SELECT Quantity FROM Products WHERE Id=$id";
            check.Parameters.AddWithValue("$id", id);
            var current = check.ExecuteScalar();
            if (current is null || current == DBNull.Value) throw new InvalidOperationException("الصنف غير موجود.");
            var quantity = Convert.ToDecimal(current, CultureInfo.InvariantCulture);
            if (!active && Math.Abs(quantity) > 0.0000001m)
                throw new InvalidOperationException("لا يمكن إيقاف صنف له رصيد مخزني. صفّر الرصيد بتسوية مخزون معتمدة أولًا.");
        }
        using (var cmd = db.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "UPDATE Products SET IsActive=$active WHERE Id=$id";
            cmd.Parameters.AddWithValue("$active", active ? 1 : 0);
            cmd.Parameters.AddWithValue("$id", id);
            if (cmd.ExecuteNonQuery() != 1) throw new InvalidOperationException("الصنف غير موجود.");
        }
        Audit(db, tx, active ? "تفعيل" : "إيقاف", "Product", id.ToString(CultureInfo.InvariantCulture), "تغيير حالة الصنف");
        tx.Commit();
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

    public long CreateInvoice(string kind, DateTime invoiceDate, int partyId, bool onCredit, IReadOnlyList<InvoiceLineInput> lines,
        decimal discountPercent = 0m, string paymentMethod = "CASH", decimal? amountTendered = null, string cashAccountCode = "1010", int? cashierSessionId = null)
    {
        if (kind != "SALE" && kind != "PURCHASE") throw new ArgumentException("نوع الفاتورة غير صالح.");
        if (partyId <= 0) throw new ArgumentException("اختر العميل أو المورد.");
        if (lines.Count == 0) throw new ArgumentException("أضف صنفًا واحدًا على الأقل.");
        if (discountPercent < 0m || discountPercent >= 100m) throw new ArgumentException("نسبة الخصم يجب أن تكون من صفر إلى أقل من 100%.");
        if (cashAccountCode is not ("1010" or "1020")) throw new ArgumentException("حساب السداد يجب أن يكون الصندوق أو البنك.");
        if (paymentMethod is not ("CASH" or "BANK" or "CREDIT")) throw new ArgumentException("طريقة السداد غير صحيحة.");
        if (onCredit) paymentMethod = "CREDIT";
        else if (paymentMethod == "CREDIT") throw new ArgumentException("اختر الآجل فقط عند تفعيل البيع على الحساب.");
        if (paymentMethod == "BANK") cashAccountCode = "1020";
        else if (paymentMethod == "CASH") cashAccountCode = "1010";
        if (kind != "SALE" && discountPercent != 0m) throw new ArgumentException("خصم شاشة الكاشير متاح للمبيعات فقط.");
        using var db = Database.Open();
        using var tx = db.BeginTransaction();

        if (cashierSessionId.HasValue)
        {
            if (kind != "SALE") throw new ArgumentException("وردية الكاشير لا تربط بفواتير الشراء.");
            using var session = db.CreateCommand();
            session.Transaction = tx;
            session.CommandText = "SELECT COUNT(*) FROM CashierSessions WHERE Id=$id AND Status='OPEN'";
            session.Parameters.AddWithValue("$id", cashierSessionId.Value);
            if (Convert.ToInt32(session.ExecuteScalar(), CultureInfo.InvariantCulture) != 1)
                throw new InvalidOperationException("وردية الكاشير مغلقة أو غير موجودة؛ لم يتم ترحيل الفاتورة.");
        }

        using (var partyCmd = db.CreateCommand())
        {
            partyCmd.Transaction = tx;
            partyCmd.CommandText = "SELECT Kind FROM Parties WHERE Id=$id AND IsActive=1";
            partyCmd.Parameters.AddWithValue("$id", partyId);
            var partyKind = partyCmd.ExecuteScalar() as string;
            if (partyKind is null || (kind == "SALE" && partyKind == "Supplier") || (kind == "PURCHASE" && partyKind == "Customer"))
                throw new InvalidOperationException(kind == "SALE" ? "الطرف المحدد ليس عميلًا نشطًا." : "الطرف المحدد ليس موردًا نشطًا.");
        }

        decimal subtotal = 0, vatTotal = 0, cogsTotal = 0, discountTotal = 0;
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

            var originalNetHalalas = ToHalalas(line.Quantity * line.UnitPrice);
            var netHalalas = kind == "SALE"
                ? ToHalalas(FromHalalas(originalNetHalalas) * (100m - discountPercent) / 100m)
                : originalNetHalalas;
            discountTotal += FromHalalas(originalNetHalalas - netHalalas);
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
        var discountHalalas = ToHalalas(discountTotal);
        var grossSubtotalHalalas = checked(subtotalHalalas + discountHalalas);
        var totalHalalas = checked(subtotalHalalas + vatHalalasTotal);
        var tenderedHalalas = onCredit ? 0L : ToHalalas(amountTendered ?? FromHalalas(totalHalalas));
        var changeHalalas = 0L;
        if (!onCredit && paymentMethod == "CASH")
        {
            if (tenderedHalalas < totalHalalas) throw new InvalidOperationException("المبلغ المستلم أقل من إجمالي الفاتورة.");
            changeHalalas = tenderedHalalas - totalHalalas;
        }
        else if (!onCredit && paymentMethod == "BANK" && tenderedHalalas != totalHalalas)
            throw new InvalidOperationException("مبلغ الشبكة يجب أن يساوي إجمالي الفاتورة.");
        if (kind == "SALE" && totalHalalas <= 0) throw new InvalidOperationException("لا يمكن ترحيل فاتورة بيع بقيمة صفر.");
        var number = (kind == "SALE" ? "S-" : "P-") + invoiceDate.ToString("yyyyMMdd") + "-" + Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
        var journalLines = new List<JournalLineInput>();
        if (kind == "SALE")
        {
            journalLines.Add(new JournalLineInput(onCredit ? "1100" : cashAccountCode, FromHalalas(totalHalalas), 0, "إجمالي فاتورة البيع", onCredit ? partyId : null));
            journalLines.Add(new JournalLineInput("4000", 0, FromHalalas(grossSubtotalHalalas), "إيراد المبيعات قبل الخصم"));
            if (discountHalalas > 0) journalLines.Add(new JournalLineInput("4020", FromHalalas(discountHalalas), 0, "خصم المبيعات"));
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
            journalLines.Add(new JournalLineInput(onCredit ? "2100" : cashAccountCode, 0, FromHalalas(totalHalalas), onCredit ? "مستحق للمورد" : "شراء نقدي", onCredit ? partyId : null));
        }
        ValidateBalanced(journalLines);
        var journalId = InsertJournal(db, tx, invoiceDate, $"فاتورة {(kind == "SALE" ? "بيع" : "شراء")} {number}", kind, journalLines);

        using (var invoiceCmd = db.CreateCommand())
        {
            invoiceCmd.Transaction = tx;
            invoiceCmd.CommandText = @"INSERT INTO Invoices(Kind,Number,InvoiceDate,PartyId,IsCredit,Status,SubtotalHalalas,VatHalalas,TotalHalalas,DiscountHalalas,DiscountPercent,PaymentMethod,AmountTenderedHalalas,ChangeHalalas,CashierSessionId,JournalId,CreatedAt)
VALUES($kind,$number,$date,$party,$credit,'POSTED',$subtotal,$vat,$total,$discount,$discountPercent,$paymentMethod,$tendered,$change,$cashierSession,$journal,$at)";
            invoiceCmd.Parameters.AddWithValue("$kind", kind);
            invoiceCmd.Parameters.AddWithValue("$number", number);
            invoiceCmd.Parameters.AddWithValue("$date", Date(invoiceDate));
            invoiceCmd.Parameters.AddWithValue("$party", partyId);
            invoiceCmd.Parameters.AddWithValue("$credit", onCredit ? 1 : 0);
            invoiceCmd.Parameters.AddWithValue("$subtotal", subtotalHalalas);
            invoiceCmd.Parameters.AddWithValue("$vat", vatHalalasTotal);
            invoiceCmd.Parameters.AddWithValue("$total", totalHalalas);
            invoiceCmd.Parameters.AddWithValue("$discount", discountHalalas);
            invoiceCmd.Parameters.AddWithValue("$discountPercent", (double)(kind == "SALE" ? discountPercent : 0m));
            invoiceCmd.Parameters.AddWithValue("$paymentMethod", paymentMethod);
            invoiceCmd.Parameters.AddWithValue("$tendered", tenderedHalalas);
            invoiceCmd.Parameters.AddWithValue("$change", changeHalalas);
            invoiceCmd.Parameters.AddWithValue("$cashierSession", cashierSessionId.HasValue ? cashierSessionId.Value : DBNull.Value);
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
        tx.Commit();
        return invoiceId;
    }

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

        using var db = Database.Open();
        using var tx = db.BeginTransaction();
        using (var party = db.CreateCommand())
        {
            party.Transaction = tx;
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
}
