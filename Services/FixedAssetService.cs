using System.Data;
using System.Globalization;
using Microsoft.Data.Sqlite;
using AlSaqr.Accounting.Data;

namespace AlSaqr.Accounting.Services;

internal sealed record FixedAssetInfo(
    int Id, string Code, string Name, DateTime AcquisitionDate,
    decimal AcquisitionCost, decimal ResidualValue, int UsefulLifeMonths,
    decimal AccumulatedDepreciation, decimal BookValue, string Status);

/// <summary>
/// Fixed-asset register and straight-line depreciation. Each acquisition/depreciation
/// posting and its subledger update are committed in one SQLite transaction.
/// </summary>
internal sealed class FixedAssetService
{
    private static string Now() => DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);
    private static string Date(DateTime value) => value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    private static long Halalas(decimal value) => AccountingService.ToHalalas(value);
    private static decimal Money(long value) => AccountingService.FromHalalas(value);

    public DataTable Register()
    {
        using var db = Database.Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = @"
SELECT Id AS 'المعرف', AssetCode AS 'رمز الأصل', Name AS 'اسم الأصل',
       AcquisitionDate AS 'تاريخ الاقتناء', AcquisitionCostHalalas/100.0 AS 'تكلفة الاقتناء',
       ResidualValueHalalas/100.0 AS 'القيمة التخريدية', UsefulLifeMonths AS 'العمر (شهر)',
       AccumulatedDepreciationHalalas/100.0 AS 'الإهلاك المتراكم',
       (AcquisitionCostHalalas-AccumulatedDepreciationHalalas)/100.0 AS 'صافي القيمة الدفترية',
       CASE Status WHEN 'ACTIVE' THEN 'نشط' ELSE 'مستبعد' END AS 'الحالة'
FROM FixedAssets ORDER BY AssetCode";
        using var reader = cmd.ExecuteReader();
        var table = new DataTable();
        table.Load(reader);
        return table;
    }

    public IReadOnlyList<FixedAssetInfo> ActiveAssets()
    {
        using var db = Database.Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = @"SELECT Id,AssetCode,Name,AcquisitionDate,AcquisitionCostHalalas,
ResidualValueHalalas,UsefulLifeMonths,AccumulatedDepreciationHalalas,Status
FROM FixedAssets WHERE Status='ACTIVE' ORDER BY AssetCode";
        using var reader = cmd.ExecuteReader();
        var result = new List<FixedAssetInfo>();
        while (reader.Read())
        {
            var cost = reader.GetInt64(4);
            var accumulated = reader.GetInt64(7);
            result.Add(new FixedAssetInfo(reader.GetInt32(0), reader.GetString(1), reader.GetString(2),
                DateTime.ParseExact(reader.GetString(3), "yyyy-MM-dd", CultureInfo.InvariantCulture),
                Money(cost), Money(reader.GetInt64(5)), reader.GetInt32(6), Money(accumulated),
                Money(cost - accumulated), reader.GetString(8)));
        }
        return result;
    }

    public long CreateAsset(string code, string name, DateTime acquisitionDate, decimal cost,
        decimal residualValue, int usefulLifeMonths, string paymentAccountCode, string actor)
    {
        code = (code ?? string.Empty).Trim();
        name = (name ?? string.Empty).Trim();
        if (code.Length is < 2 or > 40) throw new ArgumentException("رمز الأصل مطلوب ويجب ألا يتجاوز 40 حرفًا.");
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("اسم الأصل مطلوب.");
        if (cost <= 0m) throw new ArgumentException("تكلفة الاقتناء يجب أن تكون أكبر من صفر.");
        if (residualValue < 0m || residualValue >= cost) throw new ArgumentException("القيمة التخريدية يجب أن تكون صفرًا أو أكثر وأقل من تكلفة الأصل.");
        if (usefulLifeMonths is < 1 or > 1200) throw new ArgumentException("العمر الإنتاجي يجب أن يكون من شهر إلى 1200 شهر.");
        if (paymentAccountCode is not ("1010" or "1020")) throw new ArgumentException("اختر حساب الصندوق أو البنك للاقتناء.");
        if (string.IsNullOrWhiteSpace(actor)) throw new ArgumentException("اسم المستخدم مطلوب للتدقيق.");

        var costHalalas = Halalas(cost);
        var residualHalalas = Halalas(residualValue);
        if (residualHalalas >= costHalalas) throw new ArgumentException("القيمة التخريدية بعد التقريب يجب أن تكون أقل من التكلفة.");

        using var db = Database.Open();
        using var tx = db.BeginTransaction();
        EnsureActiveAccount(db, tx, "1500");
        EnsureActiveAccount(db, tx, paymentAccountCode);

        var journalNumber = NewJournalNumber(acquisitionDate);
        using (var journal = db.CreateCommand())
        {
            journal.Transaction = tx;
            journal.CommandText = @"INSERT INTO Journals(Number,EntryDate,Memo,Source,Status,CreatedAt)
VALUES($number,$date,$memo,'FIXED_ASSET_ACQUISITION','POSTED',$at)";
            journal.Parameters.AddWithValue("$number", journalNumber);
            journal.Parameters.AddWithValue("$date", Date(acquisitionDate));
            journal.Parameters.AddWithValue("$memo", "اقتناء أصل ثابت: " + code + " - " + name);
            journal.Parameters.AddWithValue("$at", Now());
            journal.ExecuteNonQuery();
        }
        var journalId = LastId(db, tx);
        InsertLine(db, tx, journalId, "1500", costHalalas, 0, "إثبات تكلفة الأصل " + code);
        InsertLine(db, tx, journalId, paymentAccountCode, 0, costHalalas, "سداد تكلفة الأصل " + code);

        using var insert = db.CreateCommand();
        insert.Transaction = tx;
        insert.CommandText = @"INSERT INTO FixedAssets(AssetCode,Name,AcquisitionDate,AcquisitionCostHalalas,
ResidualValueHalalas,UsefulLifeMonths,AccumulatedDepreciationHalalas,Status,AcquisitionJournalId,CreatedAt)
VALUES($code,$name,$date,$cost,$residual,$life,0,'ACTIVE',$journal,$at)";
        insert.Parameters.AddWithValue("$code", code);
        insert.Parameters.AddWithValue("$name", name);
        insert.Parameters.AddWithValue("$date", Date(acquisitionDate));
        insert.Parameters.AddWithValue("$cost", costHalalas);
        insert.Parameters.AddWithValue("$residual", residualHalalas);
        insert.Parameters.AddWithValue("$life", usefulLifeMonths);
        insert.Parameters.AddWithValue("$journal", journalId);
        insert.Parameters.AddWithValue("$at", Now());
        insert.ExecuteNonQuery();
        var assetId = LastId(db, tx);
        Audit(db, tx, actor, "إضافة", assetId.ToString(CultureInfo.InvariantCulture), $"تسجيل أصل {code} بتكلفة {cost:N2}");
        tx.Commit();
        return assetId;
    }

    /// <summary>Posts one full month of straight-line depreciation for each eligible asset.</summary>
    public int PostMonthlyDepreciation(DateTime period, string actor)
    {
        if (string.IsNullOrWhiteSpace(actor)) throw new ArgumentException("اسم المستخدم مطلوب للتدقيق.");
        var monthStart = new DateTime(period.Year, period.Month, 1);
        var posted = 0;
        foreach (var asset in ActiveAssets())
        {
            // Depreciation starts in the first full month after acquisition.
            if (asset.AcquisitionDate >= monthStart) continue;
            var periodKey = monthStart.ToString("yyyy-MM", CultureInfo.InvariantCulture);
            using var db = Database.Open();
            using var tx = db.BeginTransaction();
            using (var exists = db.CreateCommand())
            {
                exists.Transaction = tx;
                exists.CommandText = "SELECT COUNT(*) FROM AssetDepreciations WHERE AssetId=$asset AND Period=$period";
                exists.Parameters.AddWithValue("$asset", asset.Id);
                exists.Parameters.AddWithValue("$period", periodKey);
                if (Convert.ToInt32(exists.ExecuteScalar(), CultureInfo.InvariantCulture) > 0)
                {
                    tx.Rollback();
                    continue;
                }
            }

            using var current = db.CreateCommand();
            current.Transaction = tx;
            current.CommandText = @"SELECT AcquisitionCostHalalas,ResidualValueHalalas,UsefulLifeMonths,AccumulatedDepreciationHalalas
FROM FixedAssets WHERE Id=$id AND Status='ACTIVE'";
            current.Parameters.AddWithValue("$id", asset.Id);
            using var reader = current.ExecuteReader();
            if (!reader.Read()) { tx.Rollback(); continue; }
            var cost = reader.GetInt64(0);
            var residual = reader.GetInt64(1);
            var life = reader.GetInt32(2);
            var accumulated = reader.GetInt64(3);
            reader.Close();

            var depreciable = cost - residual;
            var remaining = depreciable - accumulated;
            if (remaining <= 0) { tx.Rollback(); continue; }
            var monthly = Math.Max(1L, (long)decimal.Round(depreciable / (decimal)life, 0, MidpointRounding.AwayFromZero));
            var charge = Math.Min(monthly, remaining);

            EnsureActiveAccount(db, tx, "1510");
            EnsureActiveAccount(db, tx, "6010");
            var journalNumber = NewJournalNumber(monthStart);
            using (var journal = db.CreateCommand())
            {
                journal.Transaction = tx;
                journal.CommandText = @"INSERT INTO Journals(Number,EntryDate,Memo,Source,Status,CreatedAt)
VALUES($number,$date,$memo,'FIXED_ASSET_DEPRECIATION','POSTED',$at)";
                journal.Parameters.AddWithValue("$number", journalNumber);
                journal.Parameters.AddWithValue("$date", Date(monthStart));
                journal.Parameters.AddWithValue("$memo", $"إهلاك أصل ثابت {asset.Code} عن {periodKey}");
                journal.Parameters.AddWithValue("$at", Now());
                journal.ExecuteNonQuery();
            }
            var journalId = LastId(db, tx);
            InsertLine(db, tx, journalId, "6010", charge, 0, "مصروف إهلاك " + asset.Code);
            InsertLine(db, tx, journalId, "1510", 0, charge, "مجمع إهلاك " + asset.Code);

            using (var depreciation = db.CreateCommand())
            {
                depreciation.Transaction = tx;
                depreciation.CommandText = @"INSERT INTO AssetDepreciations(AssetId,Period,AmountHalalas,JournalId,CreatedAt)
VALUES($asset,$period,$amount,$journal,$at)";
                depreciation.Parameters.AddWithValue("$asset", asset.Id);
                depreciation.Parameters.AddWithValue("$period", periodKey);
                depreciation.Parameters.AddWithValue("$amount", charge);
                depreciation.Parameters.AddWithValue("$journal", journalId);
                depreciation.Parameters.AddWithValue("$at", Now());
                depreciation.ExecuteNonQuery();
            }
            using (var update = db.CreateCommand())
            {
                update.Transaction = tx;
                update.CommandText = "UPDATE FixedAssets SET AccumulatedDepreciationHalalas=AccumulatedDepreciationHalalas+$amount WHERE Id=$id";
                update.Parameters.AddWithValue("$amount", charge);
                update.Parameters.AddWithValue("$id", asset.Id);
                update.ExecuteNonQuery();
            }
            Audit(db, tx, actor, "ترحيل إهلاك", asset.Id.ToString(CultureInfo.InvariantCulture), $"إهلاك {asset.Code} للفترة {periodKey}: {Money(charge):N2}");
            tx.Commit();
            posted++;
        }
        return posted;
    }

    private static string NewJournalNumber(DateTime date) =>
        "JV-" + date.ToString("yyyyMMdd", CultureInfo.InvariantCulture) + "-" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();

    private static long LastId(SqliteConnection db, SqliteTransaction tx)
    {
        using var cmd = db.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT last_insert_rowid()";
        return Convert.ToInt64(cmd.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    private static void EnsureActiveAccount(SqliteConnection db, SqliteTransaction tx, string code)
    {
        using var cmd = db.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT IsActive FROM Accounts WHERE Code=$code";
        cmd.Parameters.AddWithValue("$code", code);
        if (Convert.ToInt64(cmd.ExecuteScalar() ?? 0, CultureInfo.InvariantCulture) != 1)
            throw new InvalidOperationException($"الحساب {code} غير موجود أو موقوف. راجع دليل الحسابات.");
    }

    private static void InsertLine(SqliteConnection db, SqliteTransaction tx, long journalId, string account, long debit, long credit, string memo)
    {
        using var cmd = db.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"INSERT INTO JournalLines(JournalId,AccountCode,PartyId,Memo,DebitHalalas,CreditHalalas)
VALUES($journal,$account,NULL,$memo,$debit,$credit)";
        cmd.Parameters.AddWithValue("$journal", journalId);
        cmd.Parameters.AddWithValue("$account", account);
        cmd.Parameters.AddWithValue("$memo", memo);
        cmd.Parameters.AddWithValue("$debit", debit);
        cmd.Parameters.AddWithValue("$credit", credit);
        cmd.ExecuteNonQuery();
    }

    private static void Audit(SqliteConnection db, SqliteTransaction tx, string actor, string action, string id, string details)
    {
        using var cmd = db.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"INSERT INTO AuditLog(EventAt,Actor,Action,Entity,EntityId,Details)
VALUES($at,$actor,$action,'FixedAsset',$id,$details)";
        cmd.Parameters.AddWithValue("$at", Now());
        cmd.Parameters.AddWithValue("$actor", actor.Trim());
        cmd.Parameters.AddWithValue("$action", action);
        cmd.Parameters.AddWithValue("$id", id);
        cmd.Parameters.AddWithValue("$details", details);
        cmd.ExecuteNonQuery();
    }
}
