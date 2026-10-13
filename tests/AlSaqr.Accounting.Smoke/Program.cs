using System.Data;
using AlSaqr.Accounting;
using AlSaqr.Accounting.Data;
using AlSaqr.Accounting.Services;

Environment.SetEnvironmentVariable(
    "ALSAQR_ACCOUNTING_DATA_DIR",
    Path.Combine(Path.GetTempPath(), "AlSaqr-Accounting-Smoke-" + Guid.NewGuid().ToString("N")));

try
{
    Database.Initialize();
    var service = new AccountingService();

    AppSettingsService.Save(new AccountingAppSettings(
        "شركة اختبار الصقر", "310000000000003", "0110000000", "الرياض",
        "شكرًا لتعاملكم", "Dark", 15m,
        Path.Combine(Path.GetTempPath(), "AlSaqr-Smoke-Backups")));
    var settingsRoundTrip = AppSettingsService.Load();
    Check(settingsRoundTrip.LegalEntityName == "شركة اختبار الصقر" &&
          settingsRoundTrip.VatRegistrationNumber == "310000000000003" &&
          settingsRoundTrip.Theme == "Dark" && settingsRoundTrip.DefaultVatRate == 15m,
          "company settings and theme persist through DPAPI-protected storage");

    var licenseCheck = new LicenseService().CheckAccess();
    Check(licenseCheck.HasAccess && licenseCheck.IsTrial && !licenseCheck.IsLicensed && licenseCheck.ExpiresAtUtc.HasValue,
          "first launch grants the configured device-bound trial");
    var licenseCheckAgain = new LicenseService().CheckAccess();
    Check(licenseCheckAgain.HasAccess && licenseCheckAgain.IsTrial &&
          licenseCheckAgain.ExpiresAtUtc == licenseCheck.ExpiresAtUtc,
          "trial expiration persists between application checks");

    var searchData = new DataTable();
    searchData.Columns.Add("رقم المستند", typeof(string));
    searchData.Columns.Add("الإجمالي", typeof(decimal));
    searchData.Rows.Add("INV-100", 100m);
    searchData.Rows.Add("PO-200", 250m);
    searchData.Rows.Add("100% Discount", 5m);
    var searchView = new DataView(searchData);
    TableSearchFilter.Apply(searchView, "INV");
    Check(searchView.Count == 1, "register search matches text columns");
    TableSearchFilter.Apply(searchView, "250");
    Check(searchView.Count == 1 && Convert.ToString(searchView[0]["رقم المستند"]) == "PO-200",
        "register search matches numeric values");
    TableSearchFilter.Apply(searchView, "%");
    Check(searchView.Count == 1 && Convert.ToString(searchView[0]["رقم المستند"]) == "100% Discount",
        "register search treats percent as a literal character");
    TableSearchFilter.Apply(searchView, "");
    Check(searchView.Count == 3, "clearing register search restores all rows");

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

    var returnService = new ReturnService();
    var salesReturnable = returnService.ReturnableLines("SALE_RETURN", salesInvoice);
    Check(salesReturnable.Rows.Count > 0, "sale invoice lines are available for a linked return");
    var saleLine = salesReturnable.Rows[0];
    var saleLineId = Convert.ToInt64(saleLine["SourceInvoiceLineId"]);
    var saleReturnId = returnService.CreateReturn("SALE_RETURN", DateTime.Today, salesInvoice, true,
        new[] { new ReturnLineInput(saleLineId, product.Id, 1m) });
    Check(saleReturnId > 0, "sales return posts against the original invoice");
    Check(service.ActiveProducts().Single(p => p.Id == product.Id).Quantity == 14m, "sales return restores returned quantity");
    Check(returnService.Register("SALE_RETURN", DateTime.Today, DateTime.Today).Rows.Count == 1, "sales return appears in the returns register");
    ExpectFailure(() => returnService.CreateReturn("SALE_RETURN", DateTime.Today, salesInvoice, true,
        new[] { new ReturnLineInput(saleLineId, product.Id, 2m) }),
        "return quantities cannot exceed the original invoice line less earlier returns");

    var purchaseReturnable = returnService.ReturnableLines("PURCHASE_RETURN", purchaseInvoice);
    Check(purchaseReturnable.Rows.Count > 0, "purchase invoice lines are available for a linked return");
    var purchaseLine = purchaseReturnable.Rows[0];
    var purchaseLineId = Convert.ToInt64(purchaseLine["SourceInvoiceLineId"]);
    var purchaseReturnId = returnService.CreateReturn("PURCHASE_RETURN", DateTime.Today, purchaseInvoice, true,
        new[] { new ReturnLineInput(purchaseLineId, product.Id, 1m) });
    Check(purchaseReturnId > 0, "purchase return posts against the original supplier invoice");
    Check(service.ActiveProducts().Single(p => p.Id == product.Id).Quantity == 13m, "purchase return removes the returned stock");
    Check(returnService.Register("PURCHASE_RETURN", DateTime.Today, DateTime.Today).Rows.Count == 1, "purchase return appears in the returns register");
    ExpectFailure(() => returnService.CreateReturn("PURCHASE_RETURN", DateTime.Today, purchaseInvoice, true,
        new[] { new ReturnLineInput(purchaseLineId, product.Id, 100m) }),
        "purchase return cannot exceed the original invoice quantity");

    var finalVat = service.VatSummary(DateTime.Today, DateTime.Today);
    Check(Money(finalVat.Rows[0]["ضريبة المخرجات"]) == 3m, "sales return reduces output VAT");
    Check(Money(finalVat.Rows[0]["ضريبة المدخلات"]) == 4.8m, "purchase return reduces input VAT");
    Check(Money(finalVat.Rows[0]["صافي الضريبة المستحقة"]) == -1.8m, "VAT report nets both kinds of returns");
    var adjustments = new InventoryAdjustmentService();
    var decreaseId = adjustments.AdjustStock(product.Id, DateTime.Today, -1m, "اختبار عجز جرد");
    var increaseId = adjustments.AdjustStock(product.Id, DateTime.Today, 2m, "اختبار زيادة جرد");
    Check(decreaseId > 0 && increaseId > 0, "inventory adjustments are persisted with unique references");
    Check(service.ActiveProducts().Single(p => p.Id == product.Id).Quantity == 14m, "stock adjustment updates the real product balance");
    Check(adjustments.Register(DateTime.Today, DateTime.Today).Rows.Count == 2, "inventory adjustment register reads persisted records");
    ExpectFailure(() => adjustments.AdjustStock(product.Id, DateTime.Today, -100m, "اختبار عجز يتجاوز الرصيد"),
        "stock shortage greater than quantity is rejected");
    Check(service.StockMovementRegister(DateTime.Today, DateTime.Today).Rows.Count == 7, "both returns and stock adjustments are retained in movement ledger");
    Check(service.StockReconciliation().Rows.Count == 0, "stock ledger reconciles after returns and adjustments");
    var finalTrialBalance = service.TrialBalance(DateTime.Today, DateTime.Today);
    Check(finalTrialBalance.Rows.Cast<DataRow>().Sum(r => Money(r["إجمالي مدين"])) ==
          finalTrialBalance.Rows.Cast<DataRow>().Sum(r => Money(r["إجمالي دائن"])),
          "trial balance remains balanced after returns");
    var cashier = new CashierService();
    var cashCustomerId = cashier.EnsureCashCustomer();
    var shift = cashier.OpenSession("smoke-cashier", 100m, "افتتاح اختبار");
    Check(shift.IsOpen && shift.OpeningCash == 100m, "cashier shift opens with counted opening cash");
    ExpectFailure(() => cashier.OpenSession("smoke-cashier", 5m), "duplicate open cashier shifts are rejected");
    var cashierInvoiceId = cashier.Checkout(DateTime.Today, cashCustomerId,
        new[] { new InvoiceLineInput(product.Id, 1m, 20m) },
        10m, "CASH", 25m, "smoke-cashier", shift.Id);
    var cashierReceipt = cashier.GetReceipt(cashierInvoiceId);
    Check(cashierReceipt.Discount == 2m && cashierReceipt.Vat == 2.7m && cashierReceipt.Total == 20.7m,
        "cashier checkout applies line discount and VAT correctly");
    Check(cashierReceipt.AmountTendered == 25m && cashierReceipt.Change == 4.3m,
        "cashier checkout records tender and calculated change");
    var openedShiftAfterSale = cashier.GetActiveSession("smoke-cashier");
    Check(openedShiftAfterSale is not null && openedShiftAfterSale.CashSales == 20.7m && openedShiftAfterSale.InvoiceCount == 1,
        "cashier shift tracks the posted cash invoice");
    var closedShift = cashier.CloseSession(shift.Id, "smoke-cashier", 120m, "إقفال اختبار");
    Check(!closedShift.IsOpen && closedShift.ExpectedCash == 120.7m && closedShift.CountedCash == 120m &&
          closedShift.Difference == -0.7m, "cashier shift closure stores expected cash and shortage variance");
    Check(cashier.GetActiveSession("smoke-cashier") is null, "closed cashier shift no longer appears as active");
    var shiftRegister = cashier.SessionRegister(DateTime.Today, DateTime.Today);
    Check(shiftRegister.Rows.Count == 1 && Convert.ToString(shiftRegister.Rows[0]["الحالة"]) == "مغلقة" &&
          Money(shiftRegister.Rows[0]["الفرق"]) == -0.7m,
        "cashier shift register reports closure status and counted cash variance");
    Check(service.ActiveProducts().Single(p => p.Id == product.Id).Quantity == 13m,
        "cashier sale persists stock reduction");
    Check(service.StockMovementRegister(DateTime.Today, DateTime.Today).Rows.Count == 8,
        "cashier sale is recorded in stock movement ledger");
    Check(service.StockReconciliation().Rows.Count == 0, "stock ledger reconciles after POS sale");
    var postCashierTrialBalance = service.TrialBalance(DateTime.Today, DateTime.Today);
    Check(postCashierTrialBalance.Rows.Cast<DataRow>().Sum(r => Money(r["إجمالي مدين"])) ==
          postCashierTrialBalance.Rows.Cast<DataRow>().Sum(r => Money(r["إجمالي دائن"])),
          "trial balance remains balanced after cashier checkout");
    Console.WriteLine("SMOKE TEST PASSED: accounting, VAT, returns, inventory adjustments, POS checkout, cashier shifts, subledgers, reports, security and audit.");
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
