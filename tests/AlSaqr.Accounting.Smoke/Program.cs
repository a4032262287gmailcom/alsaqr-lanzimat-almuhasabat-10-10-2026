using System.Data;
using AlSaqr.Accounting.Data;
using AlSaqr.Accounting.Services;

Environment.SetEnvironmentVariable(
    "ALSAQR_ACCOUNTING_DATA_DIR",
    Path.Combine(Path.GetTempPath(), "AlSaqr-Accounting-Smoke-" + Guid.NewGuid().ToString("N")));

try
{
    Database.Initialize();
    var service = new AccountingService();

    Check(service.Accounts().Rows.Count >= 12, "chart of accounts is seeded");
    var auth = new AuthService();
    auth.CreateFirstAdmin("smoke-admin", "Smoke-Password-123!");
    var user = auth.Authenticate("smoke-admin", "Smoke-Password-123!");
    Check(user is not null && user.Role == "Admin", "first administrator can authenticate");
    Check(auth.Authenticate("smoke-admin", "wrong-password") is null, "wrong password is rejected");

    var customerId = AddParty(service, "عميل اختبار", "Customer");
    var supplierId = AddParty(service, "مورد اختبار", "Supplier");
    service.AddProduct("SMOKE-001", "صنف اختبار", "حبة", 10m, 10m, 20m, 15m);
    var product = service.ActiveProducts().Single(p => p.Code == "SMOKE-001");
    Check(product.Quantity == 10m, "opening stock quantity is stored");
    Check(service.StockMovementRegister(DateTime.Today, DateTime.Today).Rows.Count == 1, "opening stock movement is recorded");

    var salesInvoice = service.CreateInvoice("SALE", DateTime.Today, customerId, true,
        new[] { new InvoiceLineInput(product.Id, 2m, 20m) });
    Check(salesInvoice > 0, "sales invoice posts");
    Check(service.ActiveProducts().Single(p => p.Id == product.Id).Quantity == 8m, "sales invoice reduces inventory");

    service.CreateSettlement("RECEIPT", DateTime.Today, customerId, "1010", 20m, "استلام اختبار");
    var customerBalance = FindParty(service.PartyBalances(DateTime.Today, DateTime.Today), customerId);
    Check(Money(customerBalance["رصيد العملاء آخر المدة"]) == 26m, "customer subledger nets the receipt against the credit sale");

    var purchaseInvoice = service.CreateInvoice("PURCHASE", DateTime.Today, supplierId, true,
        new[] { new InvoiceLineInput(product.Id, 5m, 8m) });
    Check(purchaseInvoice > 0, "purchase invoice posts");
    Check(service.ActiveProducts().Single(p => p.Id == product.Id).Quantity == 13m, "purchase invoice adds inventory");
    service.CreateSettlement("PAYMENT", DateTime.Today, supplierId, "1020", 10m, "دفعة اختبار");
    var supplierBalance = FindParty(service.PartyBalances(DateTime.Today, DateTime.Today), supplierId);
    Check(Money(supplierBalance["رصيد الموردين آخر المدة"]) == 36m, "supplier subledger nets the payment against the credit purchase");

    var vat = service.VatSummary(DateTime.Today, DateTime.Today);
    Check(Money(vat.Rows[0]["ضريبة المخرجات"]) == 6m, "sales VAT is calculated");
    Check(Money(vat.Rows[0]["ضريبة المدخلات"]) == 6m, "purchase VAT is calculated");
    Check(Money(vat.Rows[0]["صافي الضريبة المستحقة"]) == 0m, "net VAT due reconciles");

    var movements = service.StockMovementRegister(DateTime.Today, DateTime.Today);
    Check(movements.Rows.Count == 3, "opening, sales and purchase movements are all retained");
    Check(service.StockReconciliation().Rows.Count == 0, "stock quantity reconciles to the movement ledger");

    var trialBalance = service.TrialBalance(DateTime.Today, DateTime.Today);
    var totalDebit = trialBalance.Rows.Cast<DataRow>().Sum(r => Money(r["إجمالي مدين"]));
    var totalCredit = trialBalance.Rows.Cast<DataRow>().Sum(r => Money(r["إجمالي دائن"]));
    Check(totalDebit == totalCredit, "trial balance total debits equal total credits");

    Check(service.RecentJournals().Rows.Count >= 5, "journal register contains the real posted transactions");
    Check(service.AuditHistory().Rows.Count >= 5, "audit log records the transactions");
    Check(service.ProfitAndLoss(DateTime.Today, DateTime.Today).Rows.Count > 0, "profit and loss report executes");
    Check(service.BalanceSheet(DateTime.Today).Rows.Count > 0, "balance sheet report executes");

    ExpectFailure(() => service.PostJournal(DateTime.Today, "قيد غير متوازن",
        new[] { new JournalLineInput("1010", 10m, 0m), new JournalLineInput("3000", 0m, 9m) }),
        "unbalanced journal is rejected");

    ExpectFailure(() => service.SetAccountActive("1100", false), "system control account cannot be disabled");
    ExpectFailure(() => auth.SetActive(user!.Id, false, user.UserName), "last administrator cannot be disabled");

    Console.WriteLine("SMOKE TEST PASSED: real posting, inventory, customer/supplier subledgers, VAT, reports, security and audit.");
    Environment.ExitCode = 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine("SMOKE TEST FAILED");
    Console.Error.WriteLine(ex);
    Environment.ExitCode = 1;
}

static int AddParty(AccountingService service, string name, string kind)
{
    service.AddParty(name, kind, "", "");
    var table = service.Parties();
    foreach (DataRow row in table.Rows)
        if (string.Equals(Convert.ToString(row[1]), name, StringComparison.Ordinal))
            return Convert.ToInt32(row[0]);
    throw new InvalidOperationException("Test party was not saved: " + name);
}

static DataRow FindParty(DataTable table, int id)
{
    foreach (DataRow row in table.Rows)
        if (Convert.ToInt32(row["المعرف"]) == id) return row;
    throw new InvalidOperationException("Subledger balance not found for party " + id);
}

static decimal Money(object value) => decimal.Round(Convert.ToDecimal(value), 2, MidpointRounding.AwayFromZero);

static void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException("ASSERTION FAILED: " + message);
    Console.WriteLine("PASS: " + message);
}

static void ExpectFailure(Action action, string message)
{
    try { action(); }
    catch (Exception) { Console.WriteLine("PASS: " + message); return; }
    throw new InvalidOperationException("ASSERTION FAILED: expected failure: " + message);
}
