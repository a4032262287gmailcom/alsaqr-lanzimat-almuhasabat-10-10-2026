using System.Data;
using System.Globalization;
using Microsoft.Data.Sqlite;
using AlSaqr.Accounting.Data;

namespace AlSaqr.Accounting.Services;

internal sealed record ReturnLineInput(int ProductId, decimal Quantity, decimal UnitPrice);

internal sealed class ReturnService
{
    private static string Now() => DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);
    private static string Date(DateTime value) => value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    private static decimal Money(long halalas) => AccountingService.FromHalalas(halalas);

    public DataTable Register(string kind, DateTime from, DateTime to)
    {
        if (kind is not ("SALE_RETURN" or "PURCHASE_RETURN" or "ALL"))
            throw new ArgumentException("نوع المردود غير صالح.");
        using var db = Database.Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = @"
SELECT r.Number AS 'رقم المردود', r.ReturnDate AS 'التاريخ',
CASE r.Kind WHEN 'SALE_RETURN' THEN 'مردود بيع' ELSE 'مردود شراء' END AS 'النوع',
p.Name AS 'العميل/المورد',
CASE r.IsCredit WHEN 1 THEN 'على الحساب' ELSE 'نقدي' END AS 'طريقة التسوية',
ROUND(r.SubtotalHalalas/100.0,2) AS 'قبل الضريبة',
ROUND(r.VatHalalas/100.0,2) AS 'الضريبة',
ROUND(r.TotalHalalas/100.0,2) AS 'الإجمالي',
r.JournalId AS 'رقم القيد'
FROM Returns r JOIN Parties p ON p.Id=r.PartyId
WHERE ($kind='ALL' OR r.Kind=$kind)
AND r.ReturnDate >= $from AND r.ReturnDate <= $to AND r.Status='POSTED'
ORDER BY r.ReturnDate,r.Id";
        cmd.Parameters.AddWithValue("$kind", kind);
        cmd.Parameters.AddWithValue("$from", Date(from));
        cmd.Parameters.AddWithValue("$to", Date(to));
        using var reader = cmd.ExecuteReader();
        var table = new DataTable();
        table.Load(reader);
        return table;
    }

    public long CreateReturn(string kind, DateTime returnDate, int partyId, bool onCredit, IReadOnlyList<ReturnLineInput> lines)
    {
        if (kind is not ("SALE_RETURN" or "PURCHASE_RETURN"))
            throw new ArgumentException("نوع المردود غير صالح.");
        if (partyId <= 0) throw new ArgumentException("اختر العميل أو المورد.");
        if (lines.Count == 0) throw new ArgumentException("أضف صنفًا واحدًا على الأقل.");
        foreach (var line in lines)
        {
            if (line.ProductId <= 0 || line.Quantity <= 0 || line.UnitPrice < 0)
                throw new ArgumentException("بيانات سطر المردود غير صالحة.");
            if (kind == "SALE_RETURN" && line.UnitPrice <= 0)
                throw new ArgumentException("سعر ردّ البيع يجب أن يكون أكبر من صفر.");
        }

        using var db = Database.Open();
        using var tx = db.BeginTransaction();

        string partyKind;
        using (var party = db.CreateCommand())
        {
            party.Transaction = tx;
            party.CommandText = "SELECT Kind FROM Parties WHERE Id=$id AND IsActive=1";
            party.Parameters.AddWithValue("$id", partyId);
            partyKind = Convert.ToString(party.ExecuteScalar(), CultureInfo.InvariantCulture) ?? "";
        }
        if (partyKind.Length == 0 ||
            (kind == "SALE_RETURN" && partyKind == "Supplier") ||
            (kind == "PURCHASE_RETURN" && partyKind == "Customer"))
            throw new InvalidOperationException(kind == "SALE_RETURN"
                ? "مردود البيع يتطلب عميلًا نشطًا."
                : "مردود الشراء يتطلب موردًا نشطًا.");

        var requestedByProduct = lines.GroupBy(x => x.ProductId)
            .ToDictionary(g => g.Key, g => g.Sum(x => x.Quantity));
        var products = new Dictionary<int, ProductSnapshot>();

        foreach (var request in requestedByProduct)
        {
            using var cmd = db.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = @"SELECT Id,Code,Name,Quantity,CostHalalas,PriceHalalas,VatRate,IsActive
FROM Products WHERE Id=$id";
            cmd.Parameters.AddWithValue("$id", request.Key);
            using var reader = cmd.ExecuteReader();
            if (!reader.Read() || reader.GetInt64(7) != 1)
                throw new InvalidOperationException("يوجد صنف غير موجود أو موقوف.");
            var product = new ProductSnapshot(
                reader.GetInt32(0), reader.GetString(1), reader.GetString(2),
                Convert.ToDecimal(reader.GetDouble(3), CultureInfo.InvariantCulture),
                reader.GetInt64(4), reader.GetInt64(5),
                Convert.ToDecimal(reader.GetDouble(6), CultureInfo.InvariantCulture));
            reader.Close();

            if (kind == "PURCHASE_RETURN" && product.Quantity < request.Value)
                throw new InvalidOperationException($"الكمية المتاحة من الصنف {product.Name} هي {product.Quantity:0.###} فقط.");

            using var eligible = db.CreateCommand();
            eligible.Transaction = tx;
            eligible.CommandText = kind == "SALE_RETURN"
                ? @"SELECT
 (SELECT COALESCE(SUM(il.Quantity),0) FROM InvoiceLines il JOIN Invoices i ON i.Id=il.InvoiceId
  WHERE i.Kind='SALE' AND i.Status='POSTED' AND i.PartyId=$party AND il.ProductId=$product)
 -(SELECT COALESCE(SUM(rl.Quantity),0) FROM ReturnLines rl JOIN Returns r ON r.Id=rl.ReturnId
  WHERE r.Kind='SALE_RETURN' AND r.Status='POSTED' AND r.PartyId=$party AND rl.ProductId=$product)"
                : @"SELECT
 (SELECT COALESCE(SUM(il.Quantity),0) FROM InvoiceLines il JOIN Invoices i ON i.Id=il.InvoiceId
  WHERE i.Kind='PURCHASE' AND i.Status='POSTED' AND i.PartyId=$party AND il.ProductId=$product)
 -(SELECT COALESCE(SUM(rl.Quantity),0) FROM ReturnLines rl JOIN Returns r ON r.Id=rl.ReturnId
  WHERE r.Kind='PURCHASE_RETURN' AND r.Status='POSTED' AND r.PartyId=$party AND rl.ProductId=$product)";
            eligible.Parameters.AddWithValue("$party", partyId);
            eligible.Parameters.AddWithValue("$product", request.Key);
            var eligibleQuantity = Convert.ToDecimal(eligible.ExecuteScalar(), CultureInfo.InvariantCulture);
            if (eligibleQuantity < request.Value)
            {
                var label = kind == "SALE_RETURN" ? "المباع للعميل بعد خصم المردودات" : "المشتريات من المورد بعد خصم المردودات";
                throw new InvalidOperationException($"الكمية المطلوبة من {product.Name} ({request.Value:0.###}) تتجاوز {label} المتاحة ({eligibleQuantity:0.###}).");
            }
            products.Add(request.Key, product);
        }

        var computed = new List<ComputedLine>();
        foreach (var line in lines)
        {
            var product = products[line.ProductId];
            var unitPriceHalalas = kind == "SALE_RETURN" ? AccountingService.ToHalalas(line.UnitPrice) : product.CostHalalas;
            if (kind == "PURCHASE_RETURN" && unitPriceHalalas <= 0)
                throw new InvalidOperationException($"لا توجد تكلفة مسجلة للصنف {product.Name} تسمح بتقييم مردود الشراء.");
            var netHalalas = kind == "SALE_RETURN"
                ? AccountingService.ToHalalas(line.Quantity * line.UnitPrice)
                : AccountingService.ToHalalas(line.Quantity * Money(unitPriceHalalas));
            var vatHalalas = AccountingService.ToHalalas(Money(netHalalas) * product.VatRate / 100m);
            var totalHalalas = checked(netHalalas + vatHalalas);
            var costHalalas = AccountingService.ToHalalas(line.Quantity * Money(product.CostHalalas));
            computed.Add(new ComputedLine(line, product, unitPriceHalalas, netHalalas, vatHalalas, totalHalalas, costHalalas));
        }

        var subtotal = computed.Aggregate(0L, (sum, item) => checked(sum + item.NetHalalas));
        var vatTotal = computed.Aggregate(0L, (sum, item) => checked(sum + item.VatHalalas));
        var total = checked(subtotal + vatTotal);
        if (subtotal <= 0 || total <= 0) throw new InvalidOperationException("إجمالي المردود يجب أن يكون أكبر من صفر.");

        var journalLines = new List<JournalLineInput>();
        if (kind == "SALE_RETURN")
        {
            journalLines.Add(new JournalLineInput("4010", Money(subtotal), 0, "مردودات المبيعات"));
            if (vatTotal > 0) journalLines.Add(new JournalLineInput("2200", Money(vatTotal), 0, "تخفيض ضريبة المخرجات"));
            journalLines.Add(new JournalLineInput(onCredit ? "1100" : "1010", 0, Money(total),
                onCredit ? "رصيد دائن للعميل بسبب مردود البيع" : "رد نقدي للعميل", onCredit ? partyId : null));
            var totalCost = computed.Aggregate(0L, (sum, item) => checked(sum + item.CostHalalas));
            if (totalCost > 0)
            {
                journalLines.Add(new JournalLineInput("1200", Money(totalCost), 0, "إعادة البضاعة المرتجعة إلى المخزون"));
                journalLines.Add(new JournalLineInput("5000", 0, Money(totalCost), "عكس تكلفة البضاعة المباعة"));
            }
        }
        else
        {
            journalLines.Add(new JournalLineInput(onCredit ? "2100" : "1010", Money(total), 0,
                onCredit ? "تخفيض مستحقات المورد" : "استرداد نقدي من المورد", onCredit ? partyId : null));
            journalLines.Add(new JournalLineInput("1200", 0, Money(subtotal), "إخراج مردود المشتريات من المخزون"));
            if (vatTotal > 0) journalLines.Add(new JournalLineInput("1300", 0, Money(vatTotal), "تخفيض ضريبة المدخلات"));
        }

        ValidateBalanced(journalLines);
        var number = (kind == "SALE_RETURN" ? "SR-" : "PR-") +
            returnDate.ToString("yyyyMMdd", CultureInfo.InvariantCulture) + "-" +
            Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
        var journalId = InsertJournal(db, tx, returnDate, $"مردود {(kind == "SALE_RETURN" ? "بيع" : "شراء")} {number}", kind, journalLines);

        long returnId;
        using (var cmd = db.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = @"INSERT INTO Returns(Kind,Number,ReturnDate,PartyId,IsCredit,Status,SubtotalHalalas,VatHalalas,TotalHalalas,JournalId,CreatedAt)
VALUES($kind,$number,$date,$party,$credit,'POSTED',$subtotal,$vat,$total,$journal,$at); SELECT last_insert_rowid()";
            cmd.Parameters.AddWithValue("$kind", kind);
            cmd.Parameters.AddWithValue("$number", number);
            cmd.Parameters.AddWithValue("$date", Date(returnDate));
            cmd.Parameters.AddWithValue("$party", partyId);
            cmd.Parameters.AddWithValue("$credit", onCredit ? 1 : 0);
            cmd.Parameters.AddWithValue("$subtotal", subtotal);
            cmd.Parameters.AddWithValue("$vat", vatTotal);
            cmd.Parameters.AddWithValue("$total", total);
            cmd.Parameters.AddWithValue("$journal", journalId);
            cmd.Parameters.AddWithValue("$at", Now());
            returnId = Convert.ToInt64(cmd.ExecuteScalar(), CultureInfo.InvariantCulture);
        }

        foreach (var item in computed)
        {
            using (var detail = db.CreateCommand())
            {
                detail.Transaction = tx;
                detail.CommandText = @"INSERT INTO ReturnLines(ReturnId,ProductId,Quantity,UnitPriceHalalas,CostAtPostingHalalas,VatRate,NetHalalas,VatHalalas,TotalHalalas)
VALUES($return,$product,$qty,$price,$cost,$rate,$net,$vat,$total)";
                detail.Parameters.AddWithValue("$return", returnId);
                detail.Parameters.AddWithValue("$product", item.Product.Id);
                detail.Parameters.AddWithValue("$qty", (double)item.Input.Quantity);
                detail.Parameters.AddWithValue("$price", item.UnitPriceHalalas);
                detail.Parameters.AddWithValue("$cost", item.Product.CostHalalas);
                detail.Parameters.AddWithValue("$rate", (double)item.Product.VatRate);
                detail.Parameters.AddWithValue("$net", item.NetHalalas);
                detail.Parameters.AddWithValue("$vat", item.VatHalalas);
                detail.Parameters.AddWithValue("$total", item.TotalHalalas);
                detail.ExecuteNonQuery();
            }

            using var movement = db.CreateCommand();
            movement.Transaction = tx;
            movement.CommandText = kind == "SALE_RETURN"
                ? "UPDATE Products SET Quantity=Quantity+$qty WHERE Id=$id AND IsActive=1"
                : "UPDATE Products SET Quantity=Quantity-$qty WHERE Id=$id AND IsActive=1 AND Quantity >= $qty";
            movement.Parameters.AddWithValue("$qty", (double)item.Input.Quantity);
            movement.Parameters.AddWithValue("$id", item.Product.Id);
            if (movement.ExecuteNonQuery() != 1)
                throw new InvalidOperationException("تعذر تحديث رصيد الصنف؛ تم إلغاء المردود كاملًا.");

            using var stock = db.CreateCommand();
            stock.Transaction = tx;
            stock.CommandText = @"INSERT INTO StockMovements(MovementDate,ProductId,Source,SourceId,QuantityIn,QuantityOut,UnitCostHalalas,Memo,CreatedAt)
VALUES($date,$product,$source,$sourceId,$in,$out,$cost,$memo,$at)";
            stock.Parameters.AddWithValue("$date", Date(returnDate));
            stock.Parameters.AddWithValue("$product", item.Product.Id);
            stock.Parameters.AddWithValue("$source", kind);
            stock.Parameters.AddWithValue("$sourceId", returnId.ToString(CultureInfo.InvariantCulture));
            stock.Parameters.AddWithValue("$in", kind == "SALE_RETURN" ? (double)item.Input.Quantity : 0d);
            stock.Parameters.AddWithValue("$out", kind == "PURCHASE_RETURN" ? (double)item.Input.Quantity : 0d);
            stock.Parameters.AddWithValue("$cost", item.Product.CostHalalas);
            stock.Parameters.AddWithValue("$memo", number);
            stock.Parameters.AddWithValue("$at", Now());
            stock.ExecuteNonQuery();
        }

        Audit(db, tx, "ترحيل", "Return", returnId.ToString(CultureInfo.InvariantCulture),
            $"ترحيل {number}; النوع={kind}; الإجمالي={Money(total):N2} ر.س");
        tx.Commit();
        return returnId;
    }

    private static long InsertJournal(SqliteConnection db, SqliteTransaction tx, DateTime date, string memo, string source, IReadOnlyList<JournalLineInput> lines)
    {
        ValidateBalanced(lines);
        var number = "JV-" + date.ToString("yyyyMMdd", CultureInfo.InvariantCulture) + "-" +
            Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        using (var cmd = db.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "INSERT INTO Journals(Number,EntryDate,Memo,Source,Status,CreatedAt) VALUES($n,$date,$memo,$source,'POSTED',$at)";
            cmd.Parameters.AddWithValue("$n", number);
            cmd.Parameters.AddWithValue("$date", Date(date));
            cmd.Parameters.AddWithValue("$memo", memo);
            cmd.Parameters.AddWithValue("$source", source);
            cmd.Parameters.AddWithValue("$at", Now());
            cmd.ExecuteNonQuery();
        }
        using (var getId = db.CreateCommand())
        {
            getId.Transaction = tx;
            getId.CommandText = "SELECT last_insert_rowid()";
            var id = Convert.ToInt64(getId.ExecuteScalar(), CultureInfo.InvariantCulture);
            foreach (var line in lines)
            {
                using var account = db.CreateCommand();
                account.Transaction = tx;
                account.CommandText = "SELECT IsActive FROM Accounts WHERE Code=$code";
                account.Parameters.AddWithValue("$code", line.AccountCode);
                var active = account.ExecuteScalar();
                if (active is null || active == DBNull.Value || Convert.ToInt64(active, CultureInfo.InvariantCulture) != 1)
                    throw new InvalidOperationException($"الحساب {line.AccountCode} غير موجود أو موقوف.");
                if (line.AccountCode is "1100" or "2100")
                {
                    if (!line.PartyId.HasValue)
                        throw new InvalidOperationException("يجب ربط حساب الذمم بالعميل أو المورد.");
                }
                else if (line.PartyId.HasValue)
                    throw new InvalidOperationException("لا يمكن ربط الطرف إلا بحساب الذمم.");
                using var detail = db.CreateCommand();
                detail.Transaction = tx;
                detail.CommandText = "INSERT INTO JournalLines(JournalId,AccountCode,PartyId,Memo,DebitHalalas,CreditHalalas) VALUES($id,$account,$party,$memo,$debit,$credit)";
                detail.Parameters.AddWithValue("$id", id);
                detail.Parameters.AddWithValue("$account", line.AccountCode);
                detail.Parameters.AddWithValue("$party", line.PartyId.HasValue ? (object)line.PartyId.Value : DBNull.Value);
                detail.Parameters.AddWithValue("$memo", line.Memo ?? "");
                detail.Parameters.AddWithValue("$debit", AccountingService.ToHalalas(line.Debit));
                detail.Parameters.AddWithValue("$credit", AccountingService.ToHalalas(line.Credit));
                detail.ExecuteNonQuery();
            }
            return id;
        }
    }

    private static void ValidateBalanced(IReadOnlyList<JournalLineInput> lines)
    {
        if (lines.Count < 2) throw new InvalidOperationException("القيد يجب أن يحتوي على سطرين على الأقل.");
        long debit = 0, credit = 0;
        foreach (var line in lines)
        {
            if (line.Debit < 0 || line.Credit < 0 ||
                (line.Debit > 0 && line.Credit > 0) ||
                (line.Debit == 0 && line.Credit == 0))
                throw new InvalidOperationException("كل سطر يجب أن يحتوي على مدين أو دائن فقط وبقيمة موجبة.");
            debit = checked(debit + AccountingService.ToHalalas(line.Debit));
            credit = checked(credit + AccountingService.ToHalalas(line.Credit));
        }
        if (debit <= 0 || debit != credit)
            throw new InvalidOperationException($"المردود غير متوازن: المدين {Money(debit):N2} ر.س، الدائن {Money(credit):N2} ر.س.");
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

    private sealed record ProductSnapshot(int Id, string Code, string Name, decimal Quantity, long CostHalalas, long PriceHalalas, decimal VatRate);
    private sealed record ComputedLine(ReturnLineInput Input, ProductSnapshot Product, long UnitPriceHalalas, long NetHalalas, long VatHalalas, long TotalHalalas, long CostHalalas);
}