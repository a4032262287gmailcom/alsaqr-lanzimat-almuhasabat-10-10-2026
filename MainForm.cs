using System.Data;
using System.Globalization;
using System.Text;
using AlSaqr.Accounting.Services;

namespace AlSaqr.Accounting;

internal sealed class MainForm : Form
{
    private readonly AuthUser _user;
    private readonly AccountingService _service = new();
    private readonly AuthService _auth = new();
    private readonly Panel _content = new();
    private readonly Label _status = new();
    private readonly Color _navy = Color.FromArgb(18, 32, 52);
    private readonly Color _blue = Color.FromArgb(37, 66, 101);
    private readonly Color _gold = Color.FromArgb(190, 151, 61);
    private readonly Color _paper = Color.FromArgb(244, 246, 249);
    private DataGridView? _tableGrid;
    private DataTable? _lastReport;

    private bool IsAdmin => _user.Role == "Admin";
    private bool CanPost => _user.Role is "Admin" or "Accountant";

    public MainForm(AuthUser user)
    {
        _user = user;
        Text = "الصقر | نظام المحاسبة";
        WindowState = FormWindowState.Maximized;
        MinimumSize = new Size(1120, 700);
        StartPosition = FormStartPosition.CenterScreen;
        RightToLeft = RightToLeft.Yes;
        RightToLeftLayout = true;
        Font = new Font("Segoe UI", 10F);
        BackColor = _paper;

        var header = new Panel { Dock = DockStyle.Top, Height = 58, BackColor = _navy, Padding = new Padding(18, 6, 18, 6) };
        var title = new Label { Text = "الصقر  |  نظام المحاسبة", AutoSize = false, Dock = DockStyle.Right, Width = 310, ForeColor = Color.White, TextAlign = ContentAlignment.MiddleRight, Font = new Font("Segoe UI", 16, FontStyle.Bold) };
        var identity = new Label { Text = $"المستخدم: {_user.UserName}  |  الدور: {RoleArabic(_user.Role)}", Dock = DockStyle.Left, Width = 320, ForeColor = Color.FromArgb(233, 220, 180), TextAlign = ContentAlignment.MiddleLeft };
        header.Controls.Add(title); header.Controls.Add(identity);

        var nav = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 54, BackColor = _blue, FlowDirection = FlowDirection.RightToLeft, WrapContents = false, AutoScroll = true, Padding = new Padding(8, 5, 8, 5) };
        AddNavigation(nav, "الرئيسية", ShowDashboard);
        AddNavigation(nav, "دليل الحسابات", ShowAccounts);
        AddNavigation(nav, "القيود اليومية", ShowJournals);
        AddNavigation(nav, "العملاء والموردون", ShowParties);
        AddNavigation(nav, "الأصناف والمخزون", ShowProducts);
        AddNavigation(nav, "فواتير البيع", () => ShowInvoices("SALE"));
        AddNavigation(nav, "فواتير الشراء", () => ShowInvoices("PURCHASE"));
        if (CanPost) {
            AddNavigation(nav, "سند قبض", () => AddSettlement("RECEIPT"));
            AddNavigation(nav, "سند صرف", () => AddSettlement("PAYMENT"));
        }
        AddNavigation(nav, "سجل السندات", ShowSettlements);
        AddNavigation(nav, "التقارير", ShowReports);
        if (IsAdmin) AddNavigation(nav, "المستخدمون والصلاحيات", ShowUsers);
        AddNavigation(nav, "نسخة احتياطية", BackupDatabase);

        _content.Dock = DockStyle.Fill;
        _content.Padding = new Padding(14);
        _status.Dock = DockStyle.Bottom;
        _status.Height = 26;
        _status.Padding = new Padding(14, 4, 14, 4);
        _status.BackColor = Color.FromArgb(230, 234, 240);
        _status.ForeColor = _navy;
        Controls.Add(_content); Controls.Add(_status); Controls.Add(nav); Controls.Add(header);
        ShowDashboard();
    }

    private static string RoleArabic(string role) => role switch { "Admin" => "مدير النظام", "Accountant" => "محاسب", _ => "عرض فقط" };

    private void AddNavigation(FlowLayoutPanel nav, string caption, Action action)
    {
        var button = new Button { Text = caption, Width = caption.Length > 12 ? 170 : 125, Height = 40, Margin = new Padding(4, 1, 4, 1), BackColor = _blue, ForeColor = Color.White, FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand };
        button.FlatAppearance.BorderColor = _gold;
        button.Click += (_, _) => action();
        nav.Controls.Add(button);
    }

    private void ShowDashboard()
    {
        _content.Controls.Clear();
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, BackColor = _paper };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 140));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var heading = new Label { Text = "لوحة المؤشرات المالية", Dock = DockStyle.Fill, Font = new Font("Segoe UI", 19, FontStyle.Bold), ForeColor = _navy, TextAlign = ContentAlignment.MiddleRight };
        var cards = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, WrapContents = true, Padding = new Padding(2), BackColor = _paper };
        var dash = _service.Dashboard(DateTime.Today);
        cards.Controls.Add(Card("الصندوق والبنوك", Money(dash.Cash), "الأرصدة المرحلة"));
        cards.Controls.Add(Card("ذمم العملاء", Money(dash.Receivables), "حركة حساب العملاء"));
        cards.Controls.Add(Card("ذمم الموردين", Money(dash.Payables), "حركة حساب الموردين"));
        cards.Controls.Add(Card("مبيعات الشهر", Money(dash.SalesMonth), $"مصروفات الشهر: {Money(dash.ExpensesMonth)}"));
        var recent = MakeGrid(_service.RecentJournals());
        var recentPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(2), BackColor = Color.White };
        var recentTitle = new Label { Text = "آخر القيود المرحلة (حتى 500 قيد)", Dock = DockStyle.Top, Height = 38, ForeColor = _navy, Font = new Font("Segoe UI", 12, FontStyle.Bold), TextAlign = ContentAlignment.MiddleRight };
        recentPanel.Controls.Add(recent); recentPanel.Controls.Add(recentTitle);
        layout.Controls.Add(heading, 0, 0); layout.Controls.Add(cards, 0, 1); layout.Controls.Add(recentPanel, 0, 2);
        _content.Controls.Add(layout);
        SetStatus($"جاهز — قاعدة البيانات الفعلية: {Data.Database.DatabasePath} — قيود الشهر: {dash.PostedEntries}");
    }

    private Control Card(string caption, string value, string subtitle)
    {
        var panel = new Panel { Width = 255, Height = 118, Margin = new Padding(6), BackColor = Color.White, BorderStyle = BorderStyle.FixedSingle, Padding = new Padding(12) };
        panel.Controls.Add(new Label { Text = caption, Dock = DockStyle.Top, Height = 25, ForeColor = _blue, TextAlign = ContentAlignment.MiddleRight });
        panel.Controls.Add(new Label { Text = value, Dock = DockStyle.Top, Height = 39, ForeColor = _navy, Font = new Font("Segoe UI", 17, FontStyle.Bold), TextAlign = ContentAlignment.MiddleRight });
        panel.Controls.Add(new Label { Text = subtitle, Dock = DockStyle.Bottom, Height = 23, ForeColor = Color.DimGray, TextAlign = ContentAlignment.MiddleRight });
        return panel;
    }

    private void ShowAccounts()
    {
        var actions = new List<(string Text, Action Run)>();
        if (IsAdmin)
        {
            actions.Add(("إضافة حساب", AddAccount));
            actions.Add(("تفعيل / إيقاف الحساب المحدد", ToggleAccount));
        }
        ShowTablePage("دليل الحسابات", _service.Accounts(), actions.ToArray());
    }

    private void AddAccount()
    {
        if (!IsAdmin) { Denied(); return; }
        using var form = Dialog("إضافة حساب جديد", 3, out var body, out var save);
        var code = TextBox(); var name = TextBox(); var type = Combo("Asset", ("Asset", "أصول"), ("Liability", "التزامات"), ("Equity", "حقوق ملكية"), ("Revenue", "إيرادات"), ("Expense", "مصروفات"));
        AddField(body, "رقم الحساب", code); AddField(body, "اسم الحساب", name); AddField(body, "تصنيف الحساب", type);
        save.Text = "حفظ الحساب";
        save.Click += (_, _) => Guard(() => { _service.AddAccount(code.Text, name.Text, Convert.ToString(type.SelectedValue) ?? "Asset"); form.DialogResult = DialogResult.OK; form.Close(); ShowAccounts(); SetStatus("تم حفظ الحساب."); });
        form.ShowDialog(this);
    }

    private void ToggleAccount()
    {
        if (!IsAdmin) { Denied(); return; }
        if (_tableGrid?.CurrentRow is null) { Notice("حدد حسابًا أولاً."); return; }
        var code = Convert.ToString(_tableGrid.CurrentRow.Cells[0].Value) ?? "";
        var active = Convert.ToString(_tableGrid.CurrentRow.Cells[3].Value) == "نشط";
        Guard(() => { _service.SetAccountActive(code, !active); ShowAccounts(); SetStatus("تم تحديث حالة الحساب."); });
    }

    private void ShowJournals()
    {
        var actions = new List<(string Text, Action Run)>();
        if (CanPost) actions.Add(("قيد يومية جديد", AddJournal));
        actions.Add(("تحديث", ShowJournals));
        ShowTablePage("القيود اليومية المرحلة — لا يمكن حذف القيد المرحل أو تعديل أثره مباشرة", _service.RecentJournals(), actions.ToArray());
    }

    private void AddJournal()
    {
        if (!CanPost) { Denied(); return; }
        using var dialog = new JournalEntryForm(_service);
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            ShowJournals();
            SetStatus("تم ترحيل القيد بعد التحقق من توازن المدين والدائن.");
        }
    }

    private void ShowParties()
    {
        var actions = new List<(string Text, Action Run)>();
        if (IsAdmin) actions.Add(("إضافة عميل / مورد", AddParty));
        actions.Add(("تحديث", ShowParties));
        ShowTablePage("العملاء والموردون", _service.Parties(), actions.ToArray());
    }

    private void AddParty()
    {
        if (!IsAdmin) { Denied(); return; }
        using var form = Dialog("إضافة عميل / مورد", 4, out var body, out var save);
        var name = TextBox(); var kind = Combo("Customer", ("Customer", "عميل"), ("Supplier", "مورد"), ("Both", "عميل ومورد"));
        var phone = TextBox(); var vat = TextBox();
        AddField(body, "الاسم التجاري / اسم الطرف", name); AddField(body, "النوع", kind); AddField(body, "الهاتف", phone); AddField(body, "الرقم الضريبي", vat);
        save.Text = "حفظ الطرف";
        save.Click += (_, _) => Guard(() => { _service.AddParty(name.Text, Convert.ToString(kind.SelectedValue) ?? "Customer", phone.Text, vat.Text); form.DialogResult = DialogResult.OK; form.Close(); ShowParties(); SetStatus("تم حفظ الطرف."); });
        form.ShowDialog(this);
    }

    private void ShowProducts()
    {
        var actions = new List<(string Text, Action Run)>();
        if (IsAdmin) actions.Add(("إضافة صنف", AddProduct));
        actions.Add(("عرض تقييم المخزون", () => ShowReportTable("تقييم المخزون", _service.StockValuation())));
        actions.Add(("تحديث", ShowProducts));
        ShowTablePage("الأصناف والمخزون", _service.Products(), actions.ToArray());
    }

    private void AddProduct()
    {
        if (!IsAdmin) { Denied(); return; }
        using var form = Dialog("إضافة صنف / رصيد افتتاحي", 6, out var body, out var save);
        var code = TextBox(); var name = TextBox(); var unit = TextBox(); unit.Text = "حبة";
        var qty = Numeric(0, 100000000, 3); var cost = Numeric(0, 1000000000, 2); var price = Numeric(0, 1000000000, 2); var vat = Numeric(0,100,2); vat.Value=15;
        AddField(body, "رمز الصنف", code); AddField(body, "اسم الصنف", name); AddField(body, "الوحدة", unit);
        AddField(body, "الكمية الافتتاحية", qty); AddField(body, "تكلفة الوحدة (ر.س)", cost); AddField(body, "سعر البيع (ر.س)", price); AddField(body, "ضريبة القيمة المضافة %", vat);
        save.Text = "حفظ الصنف";
        save.Click += (_, _) => Guard(() => { _service.AddProduct(code.Text, name.Text, unit.Text, qty.Value, cost.Value, price.Value, vat.Value); form.DialogResult = DialogResult.OK; form.Close(); ShowProducts(); SetStatus("تم حفظ الصنف."); });
        form.Height += 70;
        form.ShowDialog(this);
    }

    private void ShowInvoices(string kind)
    {
        var title = kind == "SALE" ? "فواتير البيع المرحلة" : "فواتير الشراء المرحلة";
        var actions = new List<(string Text, Action Run)>();
        if (CanPost) actions.Add((kind == "SALE" ? "فاتورة بيع جديدة" : "فاتورة شراء جديدة", () => AddInvoice(kind)));
        actions.Add(("تحديث", () => ShowInvoices(kind)));
        ShowTablePage(title, _service.InvoiceRegister(kind, new DateTime(2000,1,1), DateTime.Today.AddDays(1)), actions.ToArray());
    }

    private void AddInvoice(string kind)
    {
        if (!CanPost) { Denied(); return; }
        using var form = new InvoiceEntryForm(_service, kind);
        if (form.ShowDialog(this) == DialogResult.OK)
        {
            ShowInvoices(kind);
            SetStatus("تم ترحيل الفاتورة وتحديث القيد المحاسبي والمخزون داخل معاملة واحدة.");
        }
    }

    private void ShowUsers()
    {
        if (!IsAdmin) { Denied(); return; }
        ShowTablePage("إدارة المستخدمين والأدوار", _auth.Users(),
            ("إنشاء مستخدم", AddUser),
            ("تفعيل / إيقاف المحدد", ToggleUser),
            ("تحديث", ShowUsers));
    }

    private void AddUser()
    {
        if (!IsAdmin) { Denied(); return; }
        using var form = Dialog("إنشاء مستخدم", 3, out var body, out var save);
        var name = TextBox(); var password = TextBox(); password.UseSystemPasswordChar = true;
        var role = Combo("Accountant", ("Admin","مدير النظام"), ("Accountant","محاسب"), ("ReadOnly","عرض فقط"));
        AddField(body, "اسم المستخدم", name); AddField(body, "كلمة المرور (10 أحرف على الأقل)", password); AddField(body, "الدور", role);
        save.Text = "إنشاء";
        save.Click += (_, _) => Guard(() => { _auth.CreateUser(name.Text, password.Text, Convert.ToString(role.SelectedValue) ?? "Accountant"); form.DialogResult = DialogResult.OK; form.Close(); ShowUsers(); SetStatus("تم إنشاء المستخدم."); });
        form.ShowDialog(this);
    }

    private void ToggleUser()
    {
        if (!IsAdmin) { Denied(); return; }
        if (_tableGrid?.CurrentRow is null) { Notice("حدد مستخدمًا أولاً."); return; }
        if (!int.TryParse(Convert.ToString(_tableGrid.CurrentRow.Cells[0].Value), out var id)) { Notice("تعذر قراءة رقم المستخدم."); return; }
        var name = Convert.ToString(_tableGrid.CurrentRow.Cells[1].Value) ?? "";
        if (string.Equals(name, _user.UserName, StringComparison.OrdinalIgnoreCase)) { Notice("لا يمكنك إيقاف حسابك أثناء الجلسة الحالية."); return; }
        var active = Convert.ToString(_tableGrid.CurrentRow.Cells[3].Value) == "نشط";
        Guard(() => { _auth.SetActive(id, !active, _user.UserName); ShowUsers(); SetStatus("تم تحديث حالة المستخدم."); });
    }

    private void AddSettlement(string kind)
    {
        if (!CanPost) { Denied(); return; }
        using var form = new SettlementEntryForm(_service, kind);
        if (form.ShowDialog(this) == DialogResult.OK)
        {
            ShowSettlements();
            SetStatus(kind == "RECEIPT" ? "تم ترحيل سند القبض وتحديث الصندوق/البنك وذمم العملاء." : "تم ترحيل سند الصرف وتحديث الصندوق/البنك وذمم الموردين.");
        }
    }

    private void ShowSettlements()
    {
        var actions = new List<(string Text, Action Run)>();
        if (CanPost)
        {
            actions.Add(("سند قبض جديد", () => AddSettlement("RECEIPT")));
            actions.Add(("سند صرف جديد", () => AddSettlement("PAYMENT")));
        }
        actions.Add(("تحديث", ShowSettlements));
        ShowTablePage("سجل سندات القبض والصرف المرحلة",
            _service.SettlementRegister("ALL", new DateTime(2000,1,1), DateTime.Today.AddDays(1)), actions.ToArray());
    }

    private void ShowReports()
    {
        _content.Controls.Clear();
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1, BackColor = _paper };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 118));
        root.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        root.Controls.Add(new Label { Text="مركز التقارير المحاسبية", Dock=DockStyle.Fill, Font=new Font("Segoe UI",19,FontStyle.Bold), ForeColor=_navy, TextAlign=ContentAlignment.MiddleRight },0,0);
        var filter = new FlowLayoutPanel { Dock=DockStyle.Fill, FlowDirection=FlowDirection.RightToLeft, WrapContents=true, Padding=new Padding(4), BackColor=Color.White };
        var report=Combo("ميزان المراجعة",
            ("ميزان المراجعة","ميزان المراجعة"),("الأستاذ العام","الأستاذ العام"),("الأرباح والخسائر","الأرباح والخسائر"),
            ("الميزانية العمومية","الميزانية العمومية"),("تقييم المخزون","تقييم المخزون"),("سجل المبيعات","سجل المبيعات"),
            ("سجل المشتريات","سجل المشتريات"),("تقرير ضريبة القيمة المضافة","تقرير ضريبة القيمة المضافة"),
            ("حركة المخزون","حركة المخزون"),("تسوية المخزون","تسوية المخزون"),("سندات القبض والصرف","سندات القبض والصرف"),
            ("ملخص العملاء والموردين","ملخص العملاء والموردين"),("سجل التدقيق","سجل التدقيق"));
        report.Width=230;
        var from = new DateTimePicker { Width=145, Format=DateTimePickerFormat.Custom, CustomFormat="yyyy-MM-dd", Value=new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1) };
        var to = new DateTimePicker { Width=145, Format=DateTimePickerFormat.Custom, CustomFormat="yyyy-MM-dd", Value=DateTime.Today };
        var accounts = _service.ActiveAccounts();
        var account = Combo(accounts.FirstOrDefault()?.Code ?? "", accounts.Select(a => (a.Code, $"{a.Code} - {a.Name}")).ToArray()); account.Width=220;
        filter.Controls.AddRange(new Control[] { LabelFor("التقرير"),report,LabelFor("من تاريخ"),from,LabelFor("إلى تاريخ"),to,LabelFor("الحساب"),account });
        var btn = Button("عرض التقرير"); btn.Click += (_,_) => Guard(() => {
            if (from.Value.Date > to.Value.Date && Convert.ToString(report.SelectedValue)!="الميزانية العمومية") throw new InvalidOperationException("تاريخ البداية يجب ألا يتجاوز تاريخ النهاية.");
            var key=Convert.ToString(report.SelectedValue) ?? "ميزان المراجعة";
            var table=key switch {
                "ميزان المراجعة" => _service.TrialBalance(from.Value,to.Value),
                "الأستاذ العام" => _service.Ledger(Convert.ToString(account.SelectedValue) ?? "",from.Value,to.Value),
                "الأرباح والخسائر" => _service.ProfitAndLoss(from.Value,to.Value),
                "الميزانية العمومية" => _service.BalanceSheet(to.Value),
                "تقييم المخزون" => _service.StockValuation(),
                "سجل المبيعات" => _service.InvoiceRegister("SALE",from.Value,to.Value),
                "سجل المشتريات" => _service.InvoiceRegister("PURCHASE",from.Value,to.Value),
                "تقرير ضريبة القيمة المضافة" => _service.VatSummary(from.Value,to.Value),
                "حركة المخزون" => _service.StockMovementRegister(from.Value,to.Value),
                "تسوية المخزون" => _service.StockReconciliation(),
                "سندات القبض والصرف" => _service.SettlementRegister("ALL",from.Value,to.Value),
                "ملخص العملاء والموردين" => _service.PartyBalances(from.Value,to.Value),
                "سجل التدقيق" => IsAdmin ? _service.AuditHistory() : throw new InvalidOperationException("عرض سجل التدقيق متاح لمدير النظام فقط."),
                _ => throw new InvalidOperationException("التقرير غير معروف.")
            };
            _lastReport=table; var grid=MakeGrid(table); var old=root.GetControlFromPosition(0,2); if(old is not null) root.Controls.Remove(old); root.Controls.Add(grid,0,2);
            SetStatus($"تم إعداد التقرير «{key}» — عدد الصفوف: {table.Rows.Count}. التصدير يطابق النتائج الحالية.");
        });
        var export=Button("تصدير CSV"); export.Click += (_,_) => ExportReport();
        filter.Controls.Add(btn); filter.Controls.Add(export);
        root.Controls.Add(filter,0,1);
        root.Controls.Add(MakeGrid(_service.TrialBalance(from.Value,to.Value)),0,2);
        _lastReport=_service.TrialBalance(from.Value,to.Value);
        _content.Controls.Add(root);
    }

    private void ShowReportTable(string title, DataTable data)
    {
        ShowTablePage(title, data, ("تصدير CSV", ExportReport), ("التقارير", ShowReports));
        _lastReport=data;
    }

    private void ExportReport()
    {
        if (_lastReport is null) { Notice("اعرض تقريرًا أولاً."); return; }
        using var save = new SaveFileDialog { Filter="CSV UTF-8 (*.csv)|*.csv", FileName="AlSaqr-Report-"+DateTime.Now.ToString("yyyyMMdd-HHmm")+".csv", AddExtension=true, DefaultExt="csv" };
        if (save.ShowDialog(this)!=DialogResult.OK) return;
        try
        {
            using var writer = new StreamWriter(save.FileName,false,new UTF8Encoding(true));
            writer.WriteLine(string.Join(",",_lastReport.Columns.Cast<DataColumn>().Select(c=>Csv(c.ColumnName))));
            foreach(DataRow row in _lastReport.Rows)
                writer.WriteLine(string.Join(",",row.ItemArray.Select(v=>Csv(Convert.ToString(v,CultureInfo.InvariantCulture) ?? ""))));
            SetStatus("تم تصدير التقرير إلى "+save.FileName);
        }
        catch(Exception ex) { Notice("تعذر تصدير التقرير: "+ex.Message); }
    }

    private static string Csv(string value) => "\"" + value.Replace("\"", "\"\"").Replace("\r", " ").Replace("\n", " ") + "\"";

    private void ShowTablePage(string title, DataTable data, params (string Text, Action Run)[] actions)
    {
        _content.Controls.Clear();
        var root=new TableLayoutPanel { Dock=DockStyle.Fill, ColumnCount=1, RowCount=2, BackColor=_paper };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute,58)); root.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        var header=new Panel { Dock=DockStyle.Fill, BackColor=_paper };
        header.Controls.Add(new Label { Text=title, Dock=DockStyle.Right, Width=Math.Max(300,TextRenderer.MeasureText(title,Font).Width+60), Font=new Font("Segoe UI",16,FontStyle.Bold), ForeColor=_navy, TextAlign=ContentAlignment.MiddleRight });
        var toolbar=new FlowLayoutPanel { Dock=DockStyle.Left, AutoSize=true, FlowDirection=FlowDirection.RightToLeft, WrapContents=false, Padding=new Padding(2) };
        foreach(var action in actions)
        {
            var button=Button(action.Text); button.Width=Math.Max(110,Math.Min(230,TextRenderer.MeasureText(action.Text,Font).Width+38)); button.Click += (_,_) => action.Run(); toolbar.Controls.Add(button);
        }
        header.Controls.Add(toolbar);
        _tableGrid=MakeGrid(data);
        root.Controls.Add(header,0,0); root.Controls.Add(_tableGrid,0,1); _content.Controls.Add(root);
        _lastReport=data;
        SetStatus($"عدد السجلات المعروضة: {data.Rows.Count}");
    }

    private static DataGridView MakeGrid(DataTable data)
    {
        var grid=new DataGridView { Dock=DockStyle.Fill, DataSource=data, ReadOnly=true, AllowUserToAddRows=false, AllowUserToDeleteRows=false, MultiSelect=false, SelectionMode=DataGridViewSelectionMode.FullRowSelect, AutoSizeColumnsMode=DataGridViewAutoSizeColumnsMode.Fill, BackgroundColor=Color.White, BorderStyle=BorderStyle.FixedSingle, RowHeadersVisible=false, RightToLeft=RightToLeft.Yes, EnableHeadersVisualStyles=false };
        grid.ColumnHeadersDefaultCellStyle.BackColor=Color.FromArgb(18,32,52);
        grid.ColumnHeadersDefaultCellStyle.ForeColor=Color.White;
        grid.ColumnHeadersDefaultCellStyle.Font=new Font("Segoe UI",9.5F,FontStyle.Bold);
        grid.DefaultCellStyle.SelectionBackColor=Color.FromArgb(205,220,239);
        grid.DefaultCellStyle.SelectionForeColor=Color.FromArgb(18,32,52);
        grid.AlternatingRowsDefaultCellStyle.BackColor=Color.FromArgb(246,248,251);
        grid.RowTemplate.Height=28;
        return grid;
    }

    private static string Money(decimal amount) => amount.ToString("N2", CultureInfo.GetCultureInfo("ar-SA")) + " ر.س";
    private static Button Button(string caption) => new() { Text=caption, Height=37, AutoSize=false, BackColor=Color.FromArgb(18,32,52), ForeColor=Color.White, FlatStyle=FlatStyle.Flat, Margin=new Padding(4), Cursor=Cursors.Hand, Padding=new Padding(6,2,6,2) };
    private static Label LabelFor(string caption) => new() { Text=caption, AutoSize=true, Padding=new Padding(0,10,0,0), ForeColor=Color.FromArgb(18,32,52) };
    private static TextBox TextBox() => new() { Width=340, Height=30, BorderStyle=BorderStyle.FixedSingle };
    private static NumericUpDown Numeric(decimal min, decimal max, int decimals) => new() { Minimum=min, Maximum=max, DecimalPlaces=decimals, Increment=decimals==0?1:0.01m, Width=340, ThousandsSeparator=true };
    private static ComboBox Combo(string selected, params (string Value,string Text)[] values)
    {
        var combo=new ComboBox { DropDownStyle=ComboBoxStyle.DropDownList, Width=340, DisplayMember="Text", ValueMember="Value" };
        combo.DataSource=values.Select(v=>new Choice(v.Value,v.Text)).ToList();
        combo.SelectedValue=selected;
        return combo;
    }
    private static void AddField(FlowLayoutPanel body, string caption, Control input)
    {
        var group=new Panel { Width=390, Height=61, Margin=new Padding(4) };
        group.Controls.Add(new Label { Text=caption, Dock=DockStyle.Top, Height=25, TextAlign=ContentAlignment.MiddleRight, ForeColor=Color.FromArgb(37,66,101) });
        input.Dock=DockStyle.Bottom; input.Width=380; group.Controls.Add(input); body.Controls.Add(group);
    }
    private static Form Dialog(string title,int fields,out FlowLayoutPanel body,out Button save)
    {
        var form=new Form { Text=title, Width=480, Height=Math.Min(760,190+fields*68), StartPosition=FormStartPosition.CenterParent, FormBorderStyle=FormBorderStyle.FixedDialog, MaximizeBox=false, MinimizeBox=false, RightToLeft=RightToLeft.Yes, RightToLeftLayout=true, BackColor=Color.FromArgb(244,246,249), Font=new Font("Segoe UI",10F) };
        body=new FlowLayoutPanel { Dock=DockStyle.Fill, FlowDirection=FlowDirection.TopDown, WrapContents=false, AutoScroll=true, Padding=new Padding(14) };
        var footer=new Panel { Dock=DockStyle.Bottom, Height=55, Padding=new Padding(10) };
        save=Button("حفظ"); save.Dock=DockStyle.Right; save.Width=135;
        var cancel=Button("إلغاء"); cancel.Dock=DockStyle.Left; cancel.Width=110; cancel.DialogResult=DialogResult.Cancel;
        footer.Controls.Add(save); footer.Controls.Add(cancel);
        form.Controls.Add(body); form.Controls.Add(footer);
        form.CancelButton=cancel;
        return form;
    }

    private void BackupDatabase()
    {
        using var save=new SaveFileDialog { Filter="قاعدة بيانات SQLite (*.db)|*.db", FileName="AlSaqr-backup-"+DateTime.Now.ToString("yyyyMMdd-HHmm")+".db", AddExtension=true, DefaultExt="db" };
        if(save.ShowDialog(this)!=DialogResult.OK) return;
        Guard(() => { Data.Database.BackupTo(save.FileName); SetStatus("تم إنشاء نسخة احتياطية متسقة من قاعدة البيانات."); });
    }
    private void Denied() => Notice("ليست لديك صلاحية تنفيذ هذه العملية.");
    private void Notice(string message) => MessageBox.Show(this,message,"الصقر | نظام المحاسبة",MessageBoxButtons.OK,MessageBoxIcon.Information);
    private void SetStatus(string message) => _status.Text=message;
    private void Guard(Action action) { try { action(); } catch(Exception ex) { MessageBox.Show(this,ex.Message,"تعذر إكمال العملية",MessageBoxButtons.OK,MessageBoxIcon.Warning); } }
    private sealed record Choice(string Value,string Text);
}

internal sealed class JournalEntryForm : Form
{
    private readonly AccountingService _service;
    private readonly List<AccountInfo> _accounts;
    private readonly List<PartyLookup> _parties;
    private readonly DateTimePicker _date=new() { Width=150,Format=DateTimePickerFormat.Custom,CustomFormat="yyyy-MM-dd",Value=DateTime.Today };
    private readonly TextBox _memo=new() { Width=490 };
    private readonly DataGridView _grid=new();
    public JournalEntryForm(AccountingService service)
    {
        _service=service; _accounts=service.ActiveAccounts();
        var partyTable=service.Parties();
        _parties=partyTable.Rows.Cast<DataRow>()
            .Where(r=>Convert.ToString(r[5])=="نشط")
            .Select(r=>new PartyLookup(Convert.ToInt32(r[0]),Convert.ToString(r[1])??""))
            .ToList();
        Text="قيد يومية جديد"; Width=1040; Height=640; StartPosition=FormStartPosition.CenterParent; RightToLeft=RightToLeft.Yes; RightToLeftLayout=true; Font=new Font("Segoe UI",10F); BackColor=Color.FromArgb(244,246,249);
        var top=new FlowLayoutPanel { Dock=DockStyle.Top,Height=95,FlowDirection=FlowDirection.RightToLeft,Padding=new Padding(12),WrapContents=true };
        top.Controls.Add(new Label { Text="تاريخ القيد",AutoSize=true,Padding=new Padding(0,8,0,0) }); top.Controls.Add(_date);
        top.Controls.Add(new Label { Text="البيان",AutoSize=true,Padding=new Padding(0,8,0,0) }); top.Controls.Add(_memo);
        _grid.Dock=DockStyle.Fill; _grid.AllowUserToAddRows=true; _grid.AllowUserToDeleteRows=true; _grid.AutoSizeColumnsMode=DataGridViewAutoSizeColumnsMode.Fill; _grid.RowHeadersVisible=false; _grid.RightToLeft=RightToLeft.Yes; _grid.BackgroundColor=Color.White; _grid.EnableHeadersVisualStyles=false; _grid.ColumnHeadersDefaultCellStyle.BackColor=Color.FromArgb(18,32,52); _grid.ColumnHeadersDefaultCellStyle.ForeColor=Color.White;
        var acct=new DataGridViewComboBoxColumn { Name="account",HeaderText="الحساب",DataSource=_accounts.Select(a=>new AccountLookup(a.Code,a.Name)).ToList(),DisplayMember="DisplayText",ValueMember="Code",Width=230 };
        var party=new DataGridViewComboBoxColumn { Name="party",HeaderText="العميل / المورد",DataSource=_parties.ToList(),DisplayMember="Text",ValueMember="Id",Width=185 };
        _grid.Columns.Add(acct);
        _grid.Columns.Add(party);
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name="lineMemo",HeaderText="بيان السطر",Width=220 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name="debit",HeaderText="مدين (ر.س)",ValueType=typeof(decimal) });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name="credit",HeaderText="دائن (ر.س)",ValueType=typeof(decimal) });
        var footer=new FlowLayoutPanel { Dock=DockStyle.Bottom,Height=55,FlowDirection=FlowDirection.RightToLeft,Padding=new Padding(8) };
        var post=new Button { Text="ترحيل القيد",Width=150,Height=36,BackColor=Color.FromArgb(18,32,52),ForeColor=Color.White,FlatStyle=FlatStyle.Flat };
        var cancel=new Button { Text="إلغاء",Width=100,Height=36,DialogResult=DialogResult.Cancel };
        post.Click+=Post; footer.Controls.Add(post); footer.Controls.Add(cancel);
        Controls.Add(_grid); Controls.Add(footer); Controls.Add(top); CancelButton=cancel;
    }
    private void Post(object? sender,EventArgs e)
    {
        try
        {
            _grid.EndEdit();
            var lines=new List<JournalLineInput>();
            foreach(DataGridViewRow row in _grid.Rows)
            {
                if(row.IsNewRow) continue;
                var account=Convert.ToString(row.Cells["account"].Value)??"";
                decimal.TryParse(Convert.ToString(row.Cells["debit"].Value,CultureInfo.InvariantCulture),NumberStyles.Any,CultureInfo.InvariantCulture,out var debit);
                decimal.TryParse(Convert.ToString(row.Cells["credit"].Value,CultureInfo.InvariantCulture),NumberStyles.Any,CultureInfo.InvariantCulture,out var credit);
                var memo=Convert.ToString(row.Cells["lineMemo"].Value)??"";
                int? partyId=null;
                var partyText=Convert.ToString(row.Cells["party"].Value);
                if(int.TryParse(partyText,out var parsedParty)) partyId=parsedParty;
                if(string.IsNullOrWhiteSpace(account)&&debit==0&&credit==0) continue;
                if(account is "1100" or "2100" && !partyId.HasValue)
                    throw new InvalidOperationException("حدد العميل/المورد عند استخدام حساب الذمم 1100 أو 2100.");
                if(account is not ("1100" or "2100") && partyId.HasValue)
                    throw new InvalidOperationException("اترك خانة الطرف فارغة إلا في حساب العملاء أو الموردين.");
                lines.Add(new JournalLineInput(account,debit,credit,memo,partyId));
            }
            _service.PostJournal(_date.Value.Date,_memo.Text,lines);
            DialogResult=DialogResult.OK; Close();
        }
        catch(Exception ex){ MessageBox.Show(this,ex.Message,"تعذر ترحيل القيد",MessageBoxButtons.OK,MessageBoxIcon.Warning); }
    }
    private sealed record AccountLookup(string Code,string Name) { public string DisplayText => Code + " - " + Name; }
    private sealed record PartyLookup(int Id,string Name) { public string Text => Name; }
}

internal sealed class InvoiceEntryForm : Form
{
    private readonly AccountingService _service;
    private readonly string _kind;
    private readonly ComboBox _party=new() { Width=330,DropDownStyle=ComboBoxStyle.DropDownList };
    private readonly ComboBox _product=new() { Width=330,DropDownStyle=ComboBoxStyle.DropDownList };
    private readonly NumericUpDown _quantity=new() { Width=120,Minimum=0.001m,Maximum=10000000,DecimalPlaces=3,Value=1,Increment=1 };
    private readonly NumericUpDown _price=new() { Width=140,Minimum=0,Maximum=100000000,DecimalPlaces=2,Increment=1 };
    private readonly DateTimePicker _date=new() { Width=150,Format=DateTimePickerFormat.Custom,CustomFormat="yyyy-MM-dd",Value=DateTime.Today };
    private readonly CheckBox _credit=new() { Text="على الحساب (آجل)",AutoSize=true,Checked=true };
    private readonly DataGridView _linesGrid=new();
    private readonly List<(InvoiceLineInput Input,string Name,decimal Rate)> _lines=new();
    private readonly List<ProductInfo> _products;
    private readonly List<PartyChoice> _parties;

    public InvoiceEntryForm(AccountingService service,string kind)
    {
        _service=service; _kind=kind;
        var partiesTable=service.Parties(kind=="SALE"?"Customer":"Supplier");
        _parties=partiesTable.Rows.Cast<DataRow>().Select(r=>new PartyChoice(Convert.ToInt32(r[0]),Convert.ToString(r[1])??"",Convert.ToString(r[2])??"")).ToList();
        _products=service.ActiveProducts();
        Text=kind=="SALE"?"فاتورة بيع":"فاتورة شراء"; Width=1050; Height=690; StartPosition=FormStartPosition.CenterParent; RightToLeft=RightToLeft.Yes; RightToLeftLayout=true; Font=new Font("Segoe UI",10F); BackColor=Color.FromArgb(244,246,249);
        var top=new FlowLayoutPanel { Dock=DockStyle.Top,Height=145,FlowDirection=FlowDirection.RightToLeft,WrapContents=true,Padding=new Padding(10) };
        top.Controls.Add(new Label { Text="الطرف",AutoSize=true,Padding=new Padding(0,8,0,0) });
        _party.DisplayMember="Text"; _party.ValueMember="Id"; _party.DataSource=_parties; top.Controls.Add(_party);
        top.Controls.Add(new Label { Text="التاريخ",AutoSize=true,Padding=new Padding(0,8,0,0) }); top.Controls.Add(_date); top.Controls.Add(_credit);
        top.Controls.Add(new Label { Text="الصنف",AutoSize=true,Padding=new Padding(0,8,0,0) });
        _product.DisplayMember="Text"; _product.ValueMember="Id";
        _product.DataSource=_products.Select(p=>new ProductChoice(p.Id,$"{p.Code} - {p.Name}",p.Price,p.Cost)).ToList();
        _product.SelectedIndexChanged+=(_,_)=>SetDefaultPrice(); top.Controls.Add(_product);
        top.Controls.Add(new Label { Text="الكمية",AutoSize=true,Padding=new Padding(0,8,0,0) }); top.Controls.Add(_quantity);
        top.Controls.Add(new Label { Text="سعر الوحدة",AutoSize=true,Padding=new Padding(0,8,0,0) }); top.Controls.Add(_price);
        var add=new Button { Text="إضافة سطر",Width=120,Height=34,BackColor=Color.FromArgb(37,66,101),ForeColor=Color.White,FlatStyle=FlatStyle.Flat };
        add.Click+=AddLine; top.Controls.Add(add);
        _linesGrid.Dock=DockStyle.Fill; _linesGrid.ReadOnly=true; _linesGrid.AllowUserToAddRows=false; _linesGrid.AllowUserToDeleteRows=false; _linesGrid.AutoSizeColumnsMode=DataGridViewAutoSizeColumnsMode.Fill; _linesGrid.RowHeadersVisible=false; _linesGrid.RightToLeft=RightToLeft.Yes; _linesGrid.BackgroundColor=Color.White; _linesGrid.EnableHeadersVisualStyles=false; _linesGrid.ColumnHeadersDefaultCellStyle.BackColor=Color.FromArgb(18,32,52); _linesGrid.ColumnHeadersDefaultCellStyle.ForeColor=Color.White;
        _linesGrid.Columns.Add("product","الصنف"); _linesGrid.Columns.Add("qty","الكمية"); _linesGrid.Columns.Add("price","سعر الوحدة"); _linesGrid.Columns.Add("vat","الضريبة"); _linesGrid.Columns.Add("total","الإجمالي");
        var footer=new FlowLayoutPanel { Dock=DockStyle.Bottom,Height=58,FlowDirection=FlowDirection.RightToLeft,Padding=new Padding(8) };
        var total=new Label { Name="total",Text="الإجمالي: 0.00 ر.س",Width=360,Height=38,TextAlign=ContentAlignment.MiddleRight,Font=new Font("Segoe UI",12,FontStyle.Bold),ForeColor=Color.FromArgb(18,32,52) };
        var post=new Button { Text="ترحيل الفاتورة",Width=160,Height=36,BackColor=Color.FromArgb(18,32,52),ForeColor=Color.White,FlatStyle=FlatStyle.Flat };
        var cancel=new Button { Text="إلغاء",Width=100,Height=36,DialogResult=DialogResult.Cancel };
        post.Click+=Post; footer.Controls.Add(post); footer.Controls.Add(cancel); footer.Controls.Add(total);
        Controls.Add(_linesGrid); Controls.Add(footer); Controls.Add(top); CancelButton=cancel;
        SetDefaultPrice();
    }

    private void SetDefaultPrice()
    {
        if(_product.SelectedItem is ProductChoice item) _price.Value=Math.Min(_price.Maximum,Math.Max(_price.Minimum,_kind=="SALE"?item.Price:item.Cost));
    }

    private void AddLine(object? sender,EventArgs e)
    {
        try
        {
            if(_product.SelectedItem is not ProductChoice selected) throw new InvalidOperationException("أضف أصنافًا إلى دليل الأصناف أولًا.");
            var product=_products.First(p=>p.Id==selected.Id);
            var net=AccountingService.ToHalalas(_quantity.Value*_price.Value);
            var vat=AccountingService.ToHalalas(AccountingService.FromHalalas(net)*product.VatRate/100m);
            var total=AccountingService.FromHalalas(net+vat);
            _lines.Add((new InvoiceLineInput(product.Id,_quantity.Value,_price.Value),product.Name,product.VatRate));
            _linesGrid.Rows.Add(product.Name,_quantity.Value.ToString("0.###"),_price.Value.ToString("N2"),AccountingService.FromHalalas(vat).ToString("N2"),total.ToString("N2"));
            var totalValue=_lines.Sum(line =>
            {
                var netHalalas=AccountingService.ToHalalas(line.Input.Quantity*line.Input.UnitPrice);
                var vatHalalas=AccountingService.ToHalalas(AccountingService.FromHalalas(netHalalas)*line.Rate/100m);
                return AccountingService.FromHalalas(netHalalas+vatHalalas);
            });
            Controls.OfType<FlowLayoutPanel>().SelectMany(p=>p.Controls.OfType<Label>()).FirstOrDefault(l=>l.Name=="total")!.Text="الإجمالي: "+totalValue.ToString("N2")+" ر.س";
        }
        catch(Exception ex){MessageBox.Show(this,ex.Message,"سطر الفاتورة",MessageBoxButtons.OK,MessageBoxIcon.Warning);}
    }

    private void Post(object? sender,EventArgs e)
    {
        try
        {
            if(_party.SelectedValue is null) throw new InvalidOperationException("أضف الطرف المناسب واختره أولًا.");
            _service.CreateInvoice(_kind,_date.Value.Date,Convert.ToInt32(_party.SelectedValue),_credit.Checked,_lines.Select(l=>l.Input).ToList());
            DialogResult=DialogResult.OK; Close();
        }
        catch(Exception ex){MessageBox.Show(this,ex.Message,"تعذر ترحيل الفاتورة",MessageBoxButtons.OK,MessageBoxIcon.Warning);}
    }

    private sealed record PartyChoice(int Id,string Name,string Kind) { public string Text => Name; }
    private sealed record ProductChoice(int Id,string Text,decimal Price,decimal Cost);
}
