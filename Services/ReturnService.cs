using System.Data;
using System.Globalization;
using Microsoft.Data.Sqlite;
using AlSaqr.Accounting.Data;

namespace AlSaqr.Accounting.Services;

internal sealed record ReturnLineInput(long SourceInvoiceLineId, int ProductId, decimal Quantity);

internal sealed class ReturnService
{
    private static string Now() => DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);
    private static string Date(DateTime value) => value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    private static decimal Money(long halalas) => AccountingService.FromHalalas(halalas);

    public DataTable ReturnableInvoices(string kind, int partyId)
    {
        ValidateKind(kind);
        var sourceKind = kind == "SALE_RETURN" ? "SALE" : "PURCHASE";
        using var db = Database.Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = @"
SELECT i.Id AS 'المعرف',i.Number AS 'رقم الفاتورة',i.InvoiceDate AS 'التاريخ',
       ROUND(i.TotalHalalas/100.0,2) AS 'إجمالي الفاتورة',
       CASE i.IsCredit WHEN 1 THEN 'آجل' ELSE 'نقدي' END AS 'طريقة السداد'
FROM Invoices i
WHERE i.Kind=$sourceKind AND i.PartyId=$party AND i.Status='POSTED'
AND EXISTS (
 SELECT 1 FROM InvoiceLines il
 WHERE il.InvoiceId=i.Id
 AND il.Quantity > COALESCE((
   SELECT SUM(rl.Quantity) FROM ReturnLines rl
   JOIN Returns r ON r.Id=rl.ReturnId
   WHERE rl.SourceInvoiceLineId=il.Id AND r.Status='POSTED'
 ),0) + 0.0000001
)
ORDER BY i.InvoiceDate DESC,i.Id DESC";
        cmd.Parameters.AddWithValue("$sourceKind", sourceKind);
        cmd.Parameters.AddWithValue("$party", partyId);
        using var reader = cmd.ExecuteReader();
        var table = new DataTable();
        table.Load(reader);
        return table;
    }

    public DataTable ReturnableLines(string kind, long sourceInvoiceId)
    {
        ValidateKind(kind);
        var sourceKind = kind == "SALE_RETURN" ? "SALE" : "PURCHASE";
        using var db = Database.Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = @"
SELECT il.Id AS 'SourceInvoiceLineId',il.ProductId AS 'ProductId',
       p.Code AS 'رمز الصنف',p.Name AS 'الصنف',
       ROUND(il.Quantity,3) AS 'كمية الفاتورة',
       ROUND(COALESCE((SELECT SUM(rl.Quantity) FROM ReturnLines rl
          JOIN Returns r ON r.Id=rl.ReturnId
          WHERE rl.SourceInvoiceLineId=il.Id AND r.Status='POSTED'),0),3) AS 'الكمية المرتجعة',
       ROUND(il.Quantity-COALESCE((SELECT SUM(rl.Quantity) FROM ReturnLines rl
          JOIN Returns r ON r.Id=rl.ReturnId
          WHERE rl.SourceInvoiceLineId=il.Id AND r.Status='POSTED'),0),3) AS 'الكمية المتاحة للمردود',
       ROUND(il.UnitPriceHalalas/100.0,2) AS 'سعر الوحدة الأصلي',
       ROUND(il.VatRate,2) AS 'الضريبة %',
       ROUND(il.CostAtPostingHalalas/100.0,2) AS 'التكلفة وقت الفاتورة',
       ROUND(p.CostHalalas/100.0,2) AS 'متوسط التكلفة الحالي',
       ROUND(p.Quantity,3) AS 'المخزون الحالي'
FROM InvoiceLines il
JOIN Invoices i ON i.Id=il.InvoiceId
JOIN Products p ON p.Id=il.ProductId
WHERE i.Id=$invoice AND i.Kind=$kind AND i.Status='POSTED'
AND il.Quantity > COALESCE((SELECT SUM(rl.Quantity) FROM ReturnLines rl
    JOIN Returns r ON r.Id=rl.ReturnId
    WHERE rl.SourceInvoiceLineId=il.Id AND r.Status='POSTED'),0) + 0.0000001
ORDER BY il.Id";
        cmd.Parameters.AddWithValue("$invoice", sourceInvoiceId);
        cmd.Parameters.AddWithValue("$kind", sourceKind);
        using var reader = cmd.ExecuteReader();
        var table = new DataTable();
        table.Load(reader);
        return table;
    }

    public DataTable Register(string kind, DateTime from, DateTime to)
    {
        if (kind is not ("SALE_RETURN" or "PURCHASE_RETURN" or "ALL"))
            throw new ArgumentException("نوع المردود غير صالح.");
        using var db = Database.Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = @"
SELECT r.Number AS 'رقم المردود',r.ReturnDate AS 'التاريخ',
CASE r.Kind WHEN 'SALE_RETURN' THEN 'مردود بيع' ELSE 'مردود شراء' END AS 'النوع',
p.Name AS 'العميل/المورد',src.Number AS 'الفاتورة الأصلية',
CASE r.IsCredit WHEN 1 THEN 'تسوية على الحساب' ELSE 'تسوية نقدية' END AS 'طريقة التسوية',
ROUND(r.SubtotalHalalas/100.0,2) AS 'قبل الضريبة',
ROUND(r.VatHalalas/100.0,2) AS 'الضريبة',
ROUND(r.TotalHalalas/100.0,2) AS 'الإجمالي',
r.JournalId AS 'رقم القيد'
FROM Returns r
JOIN Parties p ON p.Id=r.PartyId
LEFT JOIN Invoices src ON src.Id=r.SourceInvoiceId
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

    public long CreateReturn(string kind, DateTime returnDate, long sourceInvoiceId, bool onCredit, IReadOnlyList<ReturnLineInput> lines)
    {
        ValidateKind(kind);
        if (sourceInvoiceId <= 0) throw new ArgumentException("حدد الفاتورة الأصلية.");
        if (lines.Count == 0) throw new ArgumentException("أضف سطرًا واحدًا على الأقل للمردود.");
        if (lines.Any(x => x.SourceInvoiceLineId <= 0 || x.ProductId <= 0 || x.Quantity <= 0))
            throw new ArgumentException("بيانات سطر المردود غير صالحة.");

        var sourceKind = kind == "SALE_RETURN" ? "SALE" : "PURCHASE";
        using var db = Database.Open();
        using var tx = db.BeginTransaction();

        int partyId;
        using (var source = db.CreateCommand())
        {
            source.Transaction = tx;
            source.CommandText = "SELECT PartyId FROM Invoices WHERE Id=$id AND Kind=$kind AND Status='POSTED'";
            source.Parameters.AddWithValue("$id", sourceInvoiceId);
            source.Parameters.AddWithValue("$kind", sourceKind);
            var value = source.ExecuteScalar();
            if (value is null || value == DBNull.Value)
                throw new InvalidOperationException("الفاتورة الأصلية غير موجودة أو لا تتوافق مع نوع المردود.");
            partyId = Convert.ToInt32(value, CultureInfo.InvariantCulture);
        }

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
            throw new InvalidOperationException("نوع الطرف في الفاتورة الأصلية غير مناسب للمردود.");

        var quantityByLine = lines.GroupBy(x => x.SourceInvoiceLineId)
            .ToDictionary(g => g.Key, g => g.Sum(x => x.Quantity));
        var eligible = new Dictionary<long, SourceLine>();
        foreach (var item in quantityByLine)
        {
            using var cmd = db.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = @"
SELECT il.Id,il.ProductId,il.Quantity,il.UnitPriceHalalas,il.CostAtPostingHalalas,il.VatRate,
       p.Code,p.Name,p.Quantity,p.CostHalalas,p.IsActive
FROM InvoiceLines il
JOIN Products p ON p.Id=il.ProductId
WHERE il.Id=$line AND il.InvoiceId=$invoice";
            cmd.Parameters.AddWithValue("$line", item.Key);
            cmd.Parameters.AddWithValue("$invoice", sourceInvoiceId);
            using var reader = cmd.ExecuteReader();
            if (!reader.Read())
                throw new InvalidOperationException("أحد أسطر المردود لا ينتمي إلى الفاتورة الأصلية المحددة.");
            var line = new SourceLine(
                reader.GetInt64(0),reader.GetInt32(1),Convert.ToDecimal(reader.GetDouble(2),CultureInfo.InvariantCulture),
                reader.GetInt64(3),reader.GetInt64(4),Convert.ToDecimal(reader.GetDouble(5),CultureInfo.InvariantCulture),
                reader.GetString(6),reader.GetString(7),Convert.ToDecimal(reader.GetDouble(8),CultureInfo.InvariantCulture),
                reader.GetInt64(9),reader.GetInt64(10)==1);
            reader.Close();
            if (!line.IsActive) throw new InvalidOperationException($"الصنف {line.ProductName} موقوف.");
            if (line.ProductId != lines.First(x => x.SourceInvoiceLineId == item.Key).ProductId)
                throw new InvalidOperationException("الصنف المحدد لا يتوافق مع سطر الفاتورة الأصلية.");

            using var already = db.CreateCommand();
            already.Transaction = tx;
            already.CommandText = "SELECT COALESCE(SUM(rl.Quantity),0) FROM ReturnLines rl JOIN Returns r ON r.Id=rl.ReturnId WHERE rl.SourceInvoiceLineId=$line AND r.Status='POSTED'";
            already.Parameters.AddWithValue("$line", item.Key);
            var previouslyReturned = Convert.ToDecimal(already.ExecuteScalar(),CultureInfo.InvariantCulture);
            var available = line.InvoiceQuantity - previouslyReturned;
            if (item.Value > available + 0.0000001m)
                throw new InvalidOperationException($"كمية المردود من {line.ProductName} تتجاوز الكمية غير المرتجعة في الفاتورة. المتاح: {Math.Max(available,0):0.###}.");
            if (kind == "PURCHASE_RETURN" && item.Value > line.StockQuantity + 0.0000001m)
                throw new InvalidOperationException($"الرصيد الحالي من {line.ProductName} لا يكفي لإرجاع هذه الكمية. المتوفر: {line.StockQuantity:0.###}.");
            eligible.Add(item.Key,line);
        }

        var computed = new List<ComputedLine>();
        foreach (var input in lines)
        {
            var source = eligible[input.SourceInvoiceLineId];
            var netHalalas = AccountingService.ToHalalas(input.Quantity * Money(source.UnitPriceHalalas));
            var vatHalalas = AccountingService.ToHalalas(Money(netHalalas) * source.VatRate / 100m);
            var totalHalalas = checked(netHalalas + vatHalalas);
            var stockCostPerUnit = kind == "SALE_RETURN" ? source.HistoricalCostHalalas : source.CurrentCostHalalas;
            var stockCostHalalas = AccountingService.ToHalalas(input.Quantity * Money(stockCostPerUnit));
            if (kind == "SALE_RETURN" && source.HistoricalCostHalalas < 0)
                throw new InvalidOperationException("التكلفة التاريخية للصنف غير صالحة.");
            computed.Add(new ComputedLine(input,source,stockCostPerUnit,netHalalas,vatHalalas,totalHalalas,stockCostHalalas));
        }

        var subtotal = computed.Aggregate(0L,(sum,x)=>checked(sum+x.NetHalalas));
        var vatTotal = computed.Aggregate(0L,(sum,x)=>checked(sum+x.VatHalalas));
        var total = checked(subtotal+vatTotal);
        if (total <= 0) throw new InvalidOperationException("إجمالي المردود يجب أن يكون أكبر من صفر.");

        var journalLines = new List<JournalLineInput>();
        if (kind == "SALE_RETURN")
        {
            journalLines.Add(new JournalLineInput("4010",Money(subtotal),0,"مردودات المبيعات"));
            if (vatTotal > 0) journalLines.Add(new JournalLineInput("2200",Money(vatTotal),0,"تخفيض ضريبة المخرجات"));
            journalLines.Add(new JournalLineInput(onCredit ? "1100" : "1010",0,Money(total),
                onCredit ? "رصيد دائن للعميل بسبب مردود البيع" : "رد نقدي للعميل",onCredit ? partyId : null));
            var cost = computed.Aggregate(0L,(sum,x)=>checked(sum+x.StockCostHalalas));
            if (cost > 0)
            {
                journalLines.Add(new JournalLineInput("1200",Money(cost),0,"إعادة البضاعة المرتجعة للمخزون بتكلفتها التاريخية"));
                journalLines.Add(new JournalLineInput("5000",0,Money(cost),"عكس تكلفة البضاعة المباعة"));
            }
        }
        else
        {
            var stockCost = computed.Aggregate(0L,(sum,x)=>checked(sum+x.StockCostHalalas));
            journalLines.Add(new JournalLineInput(onCredit ? "2100" : "1010",Money(total),0,
                onCredit ? "تخفيض مستحقات المورد" : "استرداد نقدي من المورد",onCredit ? partyId : null));
            if (stockCost > 0) journalLines.Add(new JournalLineInput("1200",0,Money(stockCost),"إخراج مردود المشتريات من المخزون بالتكلفة الدفترية"));
            if (vatTotal > 0) journalLines.Add(new JournalLineInput("1300",0,Money(vatTotal),"تخفيض ضريبة المدخلات"));
            var variance = subtotal - stockCost;
            if (variance > 0)
                journalLines.Add(new JournalLineInput("5050",0,Money(variance),"فارق تكلفة مردود المشتريات"));
            else if (variance < 0)
                journalLines.Add(new JournalLineInput("5050",Money(-variance),0,"فارق تكلفة مردود المشتريات"));
        }
        ValidateBalanced(journalLines);

        var number = (kind=="SALE_RETURN"?"SR-":"PR-")+
            returnDate.ToString("yyyyMMdd",CultureInfo.InvariantCulture)+"-"+Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
        var journalId = InsertJournal(db,tx,returnDate,$"مردود {(kind=="SALE_RETURN"?"بيع":"شراء")} {number}",kind,journalLines);

        long returnId;
        using (var cmd=db.CreateCommand())
        {
            cmd.Transaction=tx;
            cmd.CommandText=@"INSERT INTO Returns(Kind,Number,ReturnDate,PartyId,SourceInvoiceId,IsCredit,Status,SubtotalHalalas,VatHalalas,TotalHalalas,JournalId,CreatedAt)
VALUES($kind,$number,$date,$party,$source,$credit,'POSTED',$subtotal,$vat,$total,$journal,$at); SELECT last_insert_rowid()";
            cmd.Parameters.AddWithValue("$kind",kind);
            cmd.Parameters.AddWithValue("$number",number);
            cmd.Parameters.AddWithValue("$date",Date(returnDate));
            cmd.Parameters.AddWithValue("$party",partyId);
            cmd.Parameters.AddWithValue("$source",sourceInvoiceId);
            cmd.Parameters.AddWithValue("$credit",onCredit?1:0);
            cmd.Parameters.AddWithValue("$subtotal",subtotal);
            cmd.Parameters.AddWithValue("$vat",vatTotal);
            cmd.Parameters.AddWithValue("$total",total);
            cmd.Parameters.AddWithValue("$journal",journalId);
            cmd.Parameters.AddWithValue("$at",Now());
            returnId=Convert.ToInt64(cmd.ExecuteScalar(),CultureInfo.InvariantCulture);
        }

        foreach (var line in computed)
        {
            using (var detail=db.CreateCommand())
            {
                detail.Transaction=tx;
                detail.CommandText=@"INSERT INTO ReturnLines(ReturnId,SourceInvoiceLineId,ProductId,Quantity,UnitPriceHalalas,CostAtPostingHalalas,VatRate,NetHalalas,VatHalalas,TotalHalalas)
VALUES($return,$sourceLine,$product,$qty,$price,$cost,$rate,$net,$vat,$total)";
                detail.Parameters.AddWithValue("$return",returnId);
                detail.Parameters.AddWithValue("$sourceLine",line.Input.SourceInvoiceLineId);
                detail.Parameters.AddWithValue("$product",line.Source.ProductId);
                detail.Parameters.AddWithValue("$qty",(double)line.Input.Quantity);
                detail.Parameters.AddWithValue("$price",line.Source.UnitPriceHalalas);
                detail.Parameters.AddWithValue("$cost",line.StockCostPerUnitHalalas);
                detail.Parameters.AddWithValue("$rate",(double)line.Source.VatRate);
                detail.Parameters.AddWithValue("$net",line.NetHalalas);
                detail.Parameters.AddWithValue("$vat",line.VatHalalas);
                detail.Parameters.AddWithValue("$total",line.TotalHalalas);
                detail.ExecuteNonQuery();
            }

            using var stock=db.CreateCommand();
            stock.Transaction=tx;
            if (kind=="SALE_RETURN")
            {
                stock.CommandText=@"UPDATE Products SET
CostHalalas=CASE WHEN Quantity+$qty>0 THEN CAST(((Quantity*CostHalalas)+($qty*$cost))/(Quantity+$qty) AS INTEGER) ELSE CostHalalas END,
Quantity=Quantity+$qty WHERE Id=$id AND IsActive=1";
                stock.Parameters.AddWithValue("$cost",line.Source.HistoricalCostHalalas);
            }
            else
            {
                stock.CommandText="UPDATE Products SET Quantity=Quantity-$qty WHERE Id=$id AND IsActive=1 AND Quantity >= $qty";
            }
            stock.Parameters.AddWithValue("$qty",(double)line.Input.Quantity);
            stock.Parameters.AddWithValue("$id",line.Source.ProductId);
            if (stock.ExecuteNonQuery()!=1)
                throw new InvalidOperationException("تعذر تحديث المخزون؛ تم التراجع عن المردود كاملًا.");

            using var movement=db.CreateCommand();
            movement.Transaction=tx;
            movement.CommandText=@"INSERT INTO StockMovements(MovementDate,ProductId,Source,SourceId,QuantityIn,QuantityOut,UnitCostHalalas,Memo,CreatedAt)
VALUES($date,$product,$source,$sourceId,$in,$out,$cost,$memo,$at)";
            movement.Parameters.AddWithValue("$date",Date(returnDate));
            movement.Parameters.AddWithValue("$product",line.Source.ProductId);
            movement.Parameters.AddWithValue("$source",kind);
            movement.Parameters.AddWithValue("$sourceId",returnId.ToString(CultureInfo.InvariantCulture));
            movement.Parameters.AddWithValue("$in",kind=="SALE_RETURN"?(double)line.Input.Quantity:0d);
            movement.Parameters.AddWithValue("$out",kind=="PURCHASE_RETURN"?(double)line.Input.Quantity:0d);
            movement.Parameters.AddWithValue("$cost",line.StockCostPerUnitHalalas);
            movement.Parameters.AddWithValue("$memo",number);
            movement.Parameters.AddWithValue("$at",Now());
            movement.ExecuteNonQuery();
        }

        Audit(db,tx,"ترحيل","Return",returnId.ToString(CultureInfo.InvariantCulture),
            $"ترحيل {number}; الفاتورة الأصلية={sourceInvoiceId}; الإجمالي={Money(total):N2} ر.س");
        tx.Commit();
        return returnId;
    }

    private static void ValidateKind(string kind)
    {
        if (kind is not ("SALE_RETURN" or "PURCHASE_RETURN"))
            throw new ArgumentException("نوع المردود غير صالح.");
    }

    private static long InsertJournal(SqliteConnection db,SqliteTransaction tx,DateTime date,string memo,string source,IReadOnlyList<JournalLineInput> lines)
    {
        ValidateBalanced(lines);
        var number="JV-"+date.ToString("yyyyMMdd",CultureInfo.InvariantCulture)+"-"+Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        using(var cmd=db.CreateCommand())
        {
            cmd.Transaction=tx;
            cmd.CommandText="INSERT INTO Journals(Number,EntryDate,Memo,Source,Status,CreatedAt) VALUES($n,$date,$memo,$source,'POSTED',$at)";
            cmd.Parameters.AddWithValue("$n",number);
            cmd.Parameters.AddWithValue("$date",Date(date));
            cmd.Parameters.AddWithValue("$memo",memo);
            cmd.Parameters.AddWithValue("$source",source);
            cmd.Parameters.AddWithValue("$at",Now());
            cmd.ExecuteNonQuery();
        }
        long journalId;
        using(var cmd=db.CreateCommand())
        {
            cmd.Transaction=tx;
            cmd.CommandText="SELECT last_insert_rowid()";
            journalId=Convert.ToInt64(cmd.ExecuteScalar(),CultureInfo.InvariantCulture);
        }
        foreach(var line in lines)
        {
            using var account=db.CreateCommand();
            account.Transaction=tx;
            account.CommandText="SELECT IsActive FROM Accounts WHERE Code=$code";
            account.Parameters.AddWithValue("$code",line.AccountCode);
            var active=account.ExecuteScalar();
            if(active is null||active==DBNull.Value||Convert.ToInt64(active,CultureInfo.InvariantCulture)!=1)
                throw new InvalidOperationException($"الحساب {line.AccountCode} غير موجود أو موقوف.");
            if(line.AccountCode is "1100" or "2100")
            {
                if(!line.PartyId.HasValue) throw new InvalidOperationException("يجب ربط سطر الذمم بالطرف.");
                using var party=db.CreateCommand();
                party.Transaction=tx;
                party.CommandText="SELECT Kind,IsActive FROM Parties WHERE Id=$id";
                party.Parameters.AddWithValue("$id",line.PartyId.Value);
                using var reader=party.ExecuteReader();
                if(!reader.Read()||reader.GetInt64(1)!=1) throw new InvalidOperationException("الطرف غير موجود أو موقوف.");
                var pk=reader.GetString(0);
                if((line.AccountCode=="1100"&&pk=="Supplier")||(line.AccountCode=="2100"&&pk=="Customer"))
                    throw new InvalidOperationException("نوع الطرف لا يتوافق مع حساب الذمم.");
            }
            else if(line.PartyId.HasValue) throw new InvalidOperationException("لا يجوز ربط طرف إلا بحساب الذمم.");
            using var detail=db.CreateCommand();
            detail.Transaction=tx;
            detail.CommandText="INSERT INTO JournalLines(JournalId,AccountCode,PartyId,Memo,DebitHalalas,CreditHalalas) VALUES($id,$account,$party,$memo,$debit,$credit)";
            detail.Parameters.AddWithValue("$id",journalId);
            detail.Parameters.AddWithValue("$account",line.AccountCode);
            detail.Parameters.AddWithValue("$party",line.PartyId.HasValue?(object)line.PartyId.Value:DBNull.Value);
            detail.Parameters.AddWithValue("$memo",line.Memo??"");
            detail.Parameters.AddWithValue("$debit",AccountingService.ToHalalas(line.Debit));
            detail.Parameters.AddWithValue("$credit",AccountingService.ToHalalas(line.Credit));
            detail.ExecuteNonQuery();
        }
        return journalId;
    }

    private static void ValidateBalanced(IReadOnlyList<JournalLineInput> lines)
    {
        if(lines.Count<2) throw new InvalidOperationException("القيد يجب أن يحتوي على سطرين على الأقل.");
        long debit=0,credit=0;
        foreach(var line in lines)
        {
            if(line.Debit<0||line.Credit<0||(line.Debit>0&&line.Credit>0)||(line.Debit==0&&line.Credit==0))
                throw new InvalidOperationException("كل سطر يجب أن يحتوي على مدين أو دائن فقط وبقيمة موجبة.");
            debit=checked(debit+AccountingService.ToHalalas(line.Debit));
            credit=checked(credit+AccountingService.ToHalalas(line.Credit));
        }
        if(debit<=0||debit!=credit) throw new InvalidOperationException($"القيد غير متوازن: المدين {Money(debit):N2}، الدائن {Money(credit):N2}.");
    }

    private static void Audit(SqliteConnection db,SqliteTransaction tx,string action,string entity,string id,string details)
    {
        using var cmd=db.CreateCommand();
        cmd.Transaction=tx;
        cmd.CommandText="INSERT INTO AuditLog(EventAt,Actor,Action,Entity,EntityId,Details) VALUES($at,$actor,$action,$entity,$id,$details)";
        cmd.Parameters.AddWithValue("$at",Now());
        cmd.Parameters.AddWithValue("$actor",Environment.UserName);
        cmd.Parameters.AddWithValue("$action",action);
        cmd.Parameters.AddWithValue("$entity",entity);
        cmd.Parameters.AddWithValue("$id",id);
        cmd.Parameters.AddWithValue("$details",details);
        cmd.ExecuteNonQuery();
    }

    private sealed record SourceLine(long Id,int ProductId,decimal InvoiceQuantity,long UnitPriceHalalas,long HistoricalCostHalalas,decimal VatRate,string ProductCode,string ProductName,decimal StockQuantity,long CurrentCostHalalas,bool IsActive);
    private sealed record ComputedLine(ReturnLineInput Input,SourceLine Source,long StockCostPerUnitHalalas,long NetHalalas,long VatHalalas,long TotalHalalas,long StockCostHalalas);
}