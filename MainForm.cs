using System.Data;
using System.Globalization;
using System.Text;
using AlSaqr.Accounting.Services;
using AlSaqr.Accounting.Forms;

namespace AlSaqr.Accounting;

internal sealed partial class MainForm : Form
{
    private readonly AuthUser _user;
    private readonly AccountingService _service = new();
    private readonly ReturnService _returns = new();
    private readonly InventoryAdjustmentService _adjustmentService = new();
    private readonly AuthService _auth = new();
    private readonly LicenseService _licenseService = new();
    private readonly Panel _content = new();
    private readonly Label _status = new();
    private Panel? _headerPanel;
    private FlowLayoutPanel? _navPanel;
    private AccountingAppSettings _appSettings = AccountingAppSettings.Defaults();
    private UiThemePalette _palette = UiThemePalette.All[0];
    private Color _navy = Color.FromArgb(18, 32, 52);
    private Color _blue = Color.FromArgb(37, 66, 101);
    private Color _gold = Color.FromArgb(190, 151, 61);
    private Color _paper = Color.FromArgb(244, 246, 249);
    private DataGridView? _tableGrid;
    private DataTable? _lastReport;
    private string _lastReportTitle = "التقرير";

    private bool LicenseAllowsUse => _licenseService.CheckAccess().HasAccess;
    private bool IsAdmin => _user.Role == "Admin" && LicenseAllowsUse;
    private bool CanPost => (_user.Role is "Admin" or "Accountant") && LicenseAllowsUse;

    public MainForm(AuthUser user)
    {
        _user = user;
        _appSettings = AppSettingsService.Load();
        ApplyThemeColors(_appSettings.Theme);
        Text = "الصقر لأنظمة المحاسبة";
        WindowState = FormWindowState.Maximized;
        MinimumSize = new Size(1120, 700);
        StartPosition = FormStartPosition.CenterScreen;
        RightToLeft = RightToLeft.Yes;
        RightToLeftLayout = true;
        Font = new Font("Segoe UI", 10F);
        BackColor = _paper;

        var header = new Panel { Dock = DockStyle.Top, Height = 58, BackColor = _navy, Padding = new Padding(18, 6, 18, 6) };
        _headerPanel = header;
        var title = new Label { Text = "الصقر لأنظمة المحاسبة", AutoSize = false, Dock = DockStyle.Right, Width = 310, ForeColor = Color.White, TextAlign = ContentAlignment.MiddleRight, Font = new Font("Segoe UI", 16, FontStyle.Bold) };
        var identity = new Label { Text = $"المستخدم: {_user.UserName}  |  الدور: {RoleArabic(_user.Role)}", Dock = DockStyle.Left, Width = 320, ForeColor = Color.FromArgb(233, 220, 180), TextAlign = ContentAlignment.MiddleLeft };
        header.Controls.Add(title); header.Controls.Add(identity);

        var nav = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 54,
            BackColor = _blue,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            AutoScroll = true,
            Padding = new Padding(8, 5, 8, 5),
            Margin = Padding.Empty
        };
        _navPanel = nav;

        // Keep the requested classic ERP top navigation, grouped by work area instead of
        // putting every operation into a single long row of buttons.
        AddNavigation(nav, "الرئيسية", ShowDashboard);
        AddNavigationGroup(nav, "الحسابات",
            ("دليل الحسابات", (Action)ShowAccounts),
            ("القيود اليومية", (Action)ShowJournals),
            ("العملاء والموردون", (Action)ShowParties),
            ("سجل السندات", (Action)ShowSettlements),
            ("سند قبض", (Action)(() => { if (CanPost) AddSettlement("RECEIPT"); else Denied(); })),
            ("سند صرف", (Action)(() => { if (CanPost) AddSettlement("PAYMENT"); else Denied(); })));
        if (CanPost)
        {
            AddNavigationGroup(nav, "المبيعات",
                ("الكاشير / نقطة البيع", (Action)OpenCashier),
                ("فواتير البيع", (Action)(() => ShowInvoices("SALE"))),
                ("مردودات البيع", (Action)(() => ShowReturns("SALE_RETURN"))));
            AddNavigationGroup(nav, "المشتريات",
                ("فواتير الشراء", (Action)(() => ShowInvoices("PURCHASE"))),
                ("مردودات الشراء", (Action)(() => ShowReturns("PURCHASE_RETURN"))));
        }
        else
        {
            AddNavigationGroup(nav, "المبيعات",
                ("فواتير البيع", (Action)(() => ShowInvoices("SALE"))),
                ("مردودات البيع", (Action)(() => ShowReturns("SALE_RETURN"))));
            AddNavigationGroup(nav, "المشتريات",
                ("فواتير الشراء", (Action)(() => ShowInvoices("PURCHASE"))),
                ("مردودات الشراء", (Action)(() => ShowReturns("PURCHASE_RETURN"))));
        }
        AddNavigationGroup(nav, "المخزون",
            ("الأصناف والمخزون", (Action)ShowProducts),
            ("تسوية المخزون", (Action)AddStockAdjustment),
            ("سجل تسويات المخزون", (Action)ShowStockAdjustments));
        AddNavigationGroup(nav, "الأصول الثابتة",
            ("سجل الأصول والإهلاك", (Action)OpenFixedAssets));
        AddNavigation(nav, "التقارير", ShowReports);
        var systemMenu = new List<(string Caption, Action Execute)>
        {
            ("الترخيص والتفعيل", OpenLicense)
        };
        if (IsAdmin)
        {
            systemMenu.Insert(0, ("اتصال قاعدة SQL Server", (Action)OpenLocalSqlServerConnection));
            systemMenu.Insert(1, ("الإعدادات والثيمات", (Action)OpenSettings));
            systemMenu.Add(("المستخدمون والصلاحيات", (Action)ShowUsers));
            systemMenu.Add(("نسخة احتياطية", (Action)BackupDatabase));
        }
        AddNavigationGroup(nav, "النظام", systemMenu.ToArray());

        _content.Dock = DockStyle.Fill;
        _content.Padding = new Padding(14);
        _status.Dock = DockStyle.Bottom;
        _status.Height = 26;
        _status.Padding = new Padding(14, 4, 14, 4);
        _status.BackColor = Color.FromArgb(_palette.SurfaceR, _palette.SurfaceG, _palette.SurfaceB);
        _status.ForeColor = ThemeColor(_palette.ForegroundR, _palette.ForegroundG, _palette.ForegroundB);
        Controls.Add(_content); Controls.Add(_status); Controls.Add(nav); Controls.Add(header);
        ApplyThemeToChrome();
        ShowDashboard();
    }

    private static string RoleArabic(string role) => role switch { "Admin" => "مدير النظام", "Accountant" => "محاسب", _ => "عرض فقط" };

    private static Color ThemeColor(int r, int g, int b) => Color.FromArgb(r, g, b);

    private void ApplyThemeColors(string themeName)
    {
        _palette = AppSettingsService.GetPalette(themeName);
        _navy = ThemeColor(_palette.PrimaryR, _palette.PrimaryG, _palette.PrimaryB);
        _blue = ThemeColor(_palette.SecondaryR, _palette.SecondaryG, _palette.SecondaryB);
        _gold = ThemeColor(_palette.AccentR, _palette.AccentG, _palette.AccentB);
        _paper = ThemeColor(_palette.BackgroundR, _palette.BackgroundG, _palette.BackgroundB);
        BackColor = _paper;
    }

    private void ApplyThemeToChrome()
    {
        if (_headerPanel is not null)
        {
            _headerPanel.BackColor = _navy;
            foreach (Control control in _headerPanel.Controls)
            {
                if (control is Label label)
                {
                    label.BackColor = _navy;
                    label.ForeColor = label.Dock == DockStyle.Left ? _gold : Color.White;
                }
            }
        }
        if (_navPanel is not null)
        {
            _navPanel.BackColor = _blue;
            foreach (var button in _navPanel.Controls.OfType<Button>())
            {
                button.BackColor = _blue;
                button.ForeColor = Color.White;
                button.FlatAppearance.BorderColor = _gold;
                button.FlatAppearance.MouseOverBackColor = _navy;
                if (button.Tag is ContextMenuStrip menu)
                {
                    menu.BackColor = _paper;
                    menu.ForeColor = _navy;
                    foreach (ToolStripItem item in menu.Items)
                    {
                        item.BackColor = _paper;
                        item.ForeColor = _navy;
                    }
                }
            }
        }
        _status.BackColor = ThemeColor(_palette.SurfaceR, _palette.SurfaceG, _palette.SurfaceB);
        _status.ForeColor = ThemeColor(_palette.ForegroundR, _palette.ForegroundG, _palette.ForegroundB);
        _content.BackColor = _paper;
    }

    private void OpenSettings()
    {
        if (!IsAdmin) { Denied(); return; }
        using var form = new SettingsForm();
        if (form.ShowDialog(this) != DialogResult.OK || form.SavedSettings is null) return;
        _appSettings = form.SavedSettings;
        ApplyThemeColors(_appSettings.Theme);
        ApplyThemeToChrome();
        ShowDashboard();
        SetStatus("تم حفظ الإعدادات التجارية والثيم «" + _palette.DisplayName + "» على هذا الجهاز.");
    }

    private void OpenLicense()
    {
        using var form = new LicenseForm(_licenseService);
        form.ShowDialog(this);
        var status = _licenseService.CheckAccess();
        SetStatus(status.Message);
        if (!status.HasAccess)
        {
            Notice("انتهى الوصول المرخص. احفظ العمل الجاري وأعد تفعيل الترخيص.");
            return;
        }
        ShowDashboard();
    }

    private void AddNavigation(FlowLayoutPanel nav, string caption, Action action)
    {
        var button = new Button
        {
            Text = caption,
            Width = 118,
            Height = 40,
            Margin = new Padding(4, 1, 4, 1),
            Padding = new Padding(8, 0, 8, 0),
            BackColor = _blue,
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Cursor = Cursors.Hand,
            Font = new Font("Segoe UI", 10F, FontStyle.Bold)
        };
        button.FlatAppearance.BorderColor = _gold;
        button.FlatAppearance.BorderSize = 1;
        button.FlatAppearance.MouseOverBackColor = _navy;
        button.Click += (_, _) => action();
        nav.Controls.Add(button);
    }

    private void AddNavigationGroup(FlowLayoutPanel nav, string caption, params (string Caption, Action Execute)[] entries)
    {
        var menu = new ContextMenuStrip
        {
            Font = new Font("Segoe UI", 10F),
            RightToLeft = RightToLeft.Yes,
            ShowImageMargin = false,
            ShowCheckMargin = false,
            BackColor = _paper,
            ForeColor = _navy
        };
        foreach (var entry in entries)
        {
            var item = new ToolStripMenuItem(entry.Caption)
            {
                Font = new Font("Segoe UI", 10F),
                ForeColor = _navy,
                BackColor = _paper,
                Padding = new Padding(8, 5, 8, 5)
            };
            item.Click += (_, _) => entry.Execute();
            menu.Items.Add(item);
        }

        var button = new Button
        {
            Text = caption + "  ▾",
            Width = caption.Length >= 10 ? 154 : 124,
            Height = 40,
            Margin = new Padding(4, 1, 4, 1),
            Padding = new Padding(8, 0, 8, 0),
            BackColor = _blue,
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Cursor = Cursors.Hand,
            Font = new Font("Segoe UI", 10F, FontStyle.Bold),
            Tag = menu
        };
        button.FlatAppearance.BorderColor = _gold;
        button.FlatAppearance.BorderSize = 1;
        button.FlatAppearance.MouseOverBackColor = _navy;
        button.Click += (_, _) => menu.Show(button, new Point(0, button.Height));
        nav.Controls.Add(button);
    }

    private void ShowDashboard()
    {
        _content.Controls.Clear();
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, BackColor = _paper };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 140));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var heading = new Label { Text = "لوحة المؤشرات المالية", Dock = DockStyle.Fill, Font = new Font("Segoe UI", 19, FontStyle.Bold), ForeColor = ThemeColor(_palette.ForegroundR, _palette.ForegroundG, _palette.ForegroundB), TextAlign = ContentAlignment.MiddleRight };
        var cards = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, WrapContents = true, Padding = new Padding(2), BackColor = _paper };
        var dash = _service.Dashboard(DateTime.Today);
        cards.Controls.Add(Card("الصندوق والبنوك", Money(dash.Cash), "الأرصدة المرحلة"));
        cards.Controls.Add(Card("ذمم العملاء", Money(dash.Receivables), "حركة حساب العملاء"));
        cards.Controls.Add(Card("ذمم الموردين", Money(dash.Payables), "حركة حساب الموردين"));
        cards.Controls.Add(Card("مبيعات الشهر", Money(dash.SalesMonth), $"مصروفات الشهر: {Money(dash.ExpensesMonth)}"));
        var recent = MakeGrid(_service.RecentJournals());
        var recentPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(2), BackColor = ThemeColor(_palette.SurfaceR, _palette.SurfaceG, _palette.SurfaceB) };
        var recentTitle = new Label { Text = "آخر القيود المرحلة (حتى 500 قيد)", Dock = DockStyle.Top, Height = 38, ForeColor = ThemeColor(_palette.ForegroundR, _palette.ForegroundG, _palette.ForegroundB), Font = new Font("Segoe UI", 12, FontStyle.Bold), TextAlign = ContentAlignment.MiddleRight };
        recentPanel.Controls.Add(recent); recentPanel.Controls.Add(recentTitle);
        layout.Controls.Add(heading, 0, 0); layout.Controls.Add(cards, 0, 1); layout.Controls.Add(recentPanel, 0, 2);
        _content.Controls.Add(layout);
        SetStatus($"جاهز — قاعدة البيانات المحلية: {Data.Database.DatabasePath} — قيود الشهر: {dash.PostedEntries} — {_licenseService.CheckAccess().Message}");
    }

    private Control Card(string caption, string value, string subtitle)
    {
        var panel = new Panel { Width = 255, Height = 118, Margin = new Padding(6), BackColor = Color.White, BorderStyle = BorderStyle.FixedSingle, Padding = new Padding(12) };
        panel.BackColor = ThemeColor(_palette.SurfaceR, _palette.SurfaceG, _palette.SurfaceB);
        panel.Controls.Add(new Label { Text = caption, Dock = DockStyle.Top, Height = 25, ForeColor = _blue, TextAlign = ContentAlignment.MiddleRight });
        panel.Controls.Add(new Label { Text = value, Dock = DockStyle.Top, Height = 39, ForeColor = ThemeColor(_palette.ForegroundR, _palette.ForegroundG, _palette.ForegroundB), Font = new Font("Segoe UI", 17, FontStyle.Bold), TextAlign = ContentAlignment.MiddleRight });
        panel.Controls.Add(new Label { Text = subtitle, Dock = DockStyle.Bottom, Height = 23, ForeColor = ThemeColor(_palette.MutedR, _palette.MutedG, _palette.MutedB), TextAlign = ContentAlignment.MiddleRight });
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
        if (IsAdmin)
        {
            actions.Add(("إضافة عميل / مورد", AddParty));
            actions.Add(("تعديل الطرف المحدد", EditParty));
            actions.Add(("تفعيل / إيقاف المحدد", ToggleParty));
        }
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

    private void EditParty()
    {
        if (!IsAdmin) { Denied(); return; }
        if (_tableGrid?.CurrentRow is null) { Notice("حدد العميل أو المورد المراد تعديله."); return; }
        var row = _tableGrid.CurrentRow;
        var id = Convert.ToInt32(row.Cells[0].Value, CultureInfo.InvariantCulture);
        var name = TextBox(); name.Text = Convert.ToString(row.Cells[1].Value) ?? "";
        var kindText = Convert.ToString(row.Cells[2].Value) ?? "عميل";
        var kindValue = kindText switch { "مورد" => "Supplier", "عميل ومورد" => "Both", _ => "Customer" };
        var kind = Combo(kindValue, ("Customer", "عميل"), ("Supplier", "مورد"), ("Both", "عميل ومورد"));
        var phone = TextBox(); phone.Text = Convert.ToString(row.Cells[3].Value) ?? "";
        var vat = TextBox(); vat.Text = Convert.ToString(row.Cells[4].Value) ?? "";
        using var form = Dialog("تعديل بيانات العميل / المورد", 4, out var body, out var save);
        AddField(body, "الاسم التجاري / اسم الطرف", name); AddField(body, "النوع", kind);
        AddField(body, "الهاتف", phone); AddField(body, "الرقم الضريبي", vat);
        save.Text = "حفظ التعديلات";
        save.Click += (_, _) => Guard(() => {
            _service.UpdateParty(id, name.Text, Convert.ToString(kind.SelectedValue) ?? "Customer", phone.Text, vat.Text);
            form.DialogResult = DialogResult.OK; form.Close(); ShowParties(); SetStatus("تم تحديث بيانات الطرف في قاعدة البيانات المحلية.");
        });
        form.ShowDialog(this);
    }

    private void ToggleParty()
    {
        if (!IsAdmin) { Denied(); return; }
        if (_tableGrid?.CurrentRow is null) { Notice("حدد العميل أو المورد أولًا."); return; }
        var row = _tableGrid.CurrentRow;
        var id = Convert.ToInt32(row.Cells[0].Value, CultureInfo.InvariantCulture);
        var active = Convert.ToString(row.Cells[5].Value) == "نشط";
        Guard(() => {
            _service.SetPartyActive(id, !active);
            ShowParties();
            SetStatus(active ? "تم إيقاف الطرف مع الاحتفاظ بسجلاته المحاسبية." : "تم تفعيل الطرف.");
        });
    }

    private void ShowProducts()
    {
        var actions = new List<(string Text, Action Run)>();
        if (IsAdmin)
        {
            actions.Add(("إضافة صنف", AddProduct));
            actions.Add(("تعديل الصنف المحدد", EditProduct));
            actions.Add(("تعيين / تعديل باركود", EditProductBarcode));
            actions.Add(("تفعيل / إيقاف المحدد", ToggleProduct));
        }
        actions.Add(("تسوية مخزون", AddStockAdjustment));
        actions.Add(("عرض تقييم المخزون", () => ShowReportTable("تقييم المخزون", _service.StockValuation())));
        actions.Add(("تحديث", ShowProducts));
        ShowTablePage("الأصناف والمخزون", _service.Products(), actions.ToArray());
    }

    private void AddProduct()
    {
        if (!IsAdmin) { Denied(); return; }
        using var form = Dialog("إضافة صنف / رصيد افتتاحي", 6, out var body, out var save);
        var code = TextBox(); var name = TextBox(); var unit = TextBox(); unit.Text = "حبة";
        var qty = Numeric(0, 100000000, 3); var cost = Numeric(0, 1000000000, 2); var price = Numeric(0, 1000000000, 2); var vat = Numeric(0,100,2); vat.Value=Math.Min(vat.Maximum, Math.Max(vat.Minimum, _appSettings.DefaultVatRate));
        AddField(body, "رمز الصنف", code); AddField(body, "اسم الصنف", name); AddField(body, "الوحدة", unit);
        AddField(body, "الكمية الافتتاحية", qty); AddField(body, "تكلفة الوحدة (ر.س)", cost); AddField(body, "سعر البيع (ر.س)", price); AddField(body, "ضريبة القيمة المضافة %", vat);
        save.Text = "حفظ الصنف";
        save.Click += (_, _) => Guard(() => { _service.AddProduct(code.Text, name.Text, unit.Text, qty.Value, cost.Value, price.Value, vat.Value); form.DialogResult = DialogResult.OK; form.Close(); ShowProducts(); SetStatus("تم حفظ الصنف."); });
        form.Height += 70;
        form.ShowDialog(this);
    }

    private void EditProduct()
    {
        if (!IsAdmin) { Denied(); return; }
        if (_tableGrid?.CurrentRow is null) { Notice("حدد الصنف المراد تعديله."); return; }
        var row = _tableGrid.CurrentRow;
        var id = Convert.ToInt32(row.Cells[0].Value, CultureInfo.InvariantCulture);
        var code = TextBox(); code.Text = Convert.ToString(row.Cells[1].Value) ?? "";
        var name = TextBox(); name.Text = Convert.ToString(row.Cells[2].Value) ?? "";
        var unit = TextBox(); unit.Text = Convert.ToString(row.Cells[3].Value) ?? "حبة";
        var price = Numeric(0, 1000000000, 2); price.Value = Math.Min(price.Maximum, Math.Max(price.Minimum, Convert.ToDecimal(row.Cells[6].Value, CultureInfo.InvariantCulture)));
        var vat = Numeric(0, 100, 2); vat.Value = Math.Min(vat.Maximum, Math.Max(vat.Minimum, Convert.ToDecimal(row.Cells[7].Value, CultureInfo.InvariantCulture)));
        using var form = Dialog("تعديل بيانات الصنف", 5, out var body, out var save);
        AddField(body, "رمز الصنف", code); AddField(body, "اسم الصنف", name); AddField(body, "الوحدة", unit);
        AddField(body, "سعر البيع", price); AddField(body, "ضريبة القيمة المضافة %", vat);
        AddField(body, "ملاحظة", new Label { Text = "لا يتم تعديل الرصيد أو التكلفة من شاشة البيانات الأساسية؛ استخدم تسوية المخزون.", Width = 360, Height = 45 });
        save.Text = "حفظ التعديلات";
        save.Click += (_, _) => Guard(() => {
            _service.UpdateProduct(id, code.Text, name.Text, unit.Text, price.Value, vat.Value);
            form.DialogResult = DialogResult.OK; form.Close(); ShowProducts(); SetStatus("تم تحديث الصنف مع إبقاء تقييم المخزون محفوظًا.");
        });
        form.Height += 65;
        form.ShowDialog(this);
    }

    private void EditProductBarcode()
    {
        if (!IsAdmin) { Denied(); return; }
        if (_tableGrid?.CurrentRow is null) { Notice("حدد الصنف المراد تعيين باركوده."); return; }
        var id = Convert.ToInt32(_tableGrid.CurrentRow.Cells[0].Value, CultureInfo.InvariantCulture);
        var name = Convert.ToString(_tableGrid.CurrentRow.Cells[2].Value) ?? "";
        var barcode = TextBox();
        var service = new CashierService();
        barcode.Text = service.GetBarcode(id);
        using var form = Dialog("تعيين باركود للصنف: " + name, 1, out var body, out var save);
        AddField(body, "الباركود (اتركه فارغًا للإزالة)", barcode);
        save.Text = "حفظ الباركود";
        save.Click += (_, _) => Guard(() =>
        {
            service.SetBarcode(id, barcode.Text, _user.UserName);
            form.DialogResult = DialogResult.OK;
            form.Close();
            ShowProducts();
            SetStatus("تم حفظ الباركود. يمكن للكاشير الآن البحث به أو مسحه.");
        });
        form.ShowDialog(this);
    }

    private void OpenCashier()
    {
        if (!CanPost) { Denied(); return; }
        using var form = new CashierForm(_user);
        form.ShowDialog(this);
        ShowDashboard();
    }

    private void OpenFixedAssets()
    {
        using var form = new FixedAssetsForm(_user);
        form.ShowDialog(this);
        ShowDashboard();
    }

    private void OpenLocalSqlServerConnection()
    {
        if (!IsAdmin) { Denied(); return; }
        using var form = new LocalSqlServerConnectionForm();
        form.ShowDialog(this);
    }

    private void ToggleProduct()
    {
        if (!IsAdmin) { Denied(); return; }
        if (_tableGrid?.CurrentRow is null) { Notice("حدد الصنف أولًا."); return; }
        var row = _tableGrid.CurrentRow;
        var id = Convert.ToInt32(row.Cells[0].Value, CultureInfo.InvariantCulture);
        var active = Convert.ToString(row.Cells[8].Value) == "نشط";
        Guard(() => {
            _service.SetProductActive(id, !active);
            ShowProducts();
            SetStatus(active ? "تم إيقاف الصنف." : "تم تفعيل الصنف.");
        });
    }

    private void AddStockAdjustment()
    {
        if (!CanPost) { Denied(); return; }
        var products = _service.ActiveProducts();
        if (products.Count == 0) { Notice("أضف أصنافًا نشطة إلى دليل الأصناف أولًا."); return; }
        var options = products.Select(p => (p.Id.ToString(CultureInfo.InvariantCulture), $"{p.Code} - {p.Name} | الرصيد {p.Quantity:0.###} | التكلفة {p.Cost:N2}")).ToArray();
        using var form = Dialog("تسوية مخزون فعلية", 5, out var body, out var save);
        var product = Combo(options[0].Item1, options.Select(p => (Value: p.Item1, Text: p.Item2)).ToArray());
        var direction = Combo("IN", ("IN", "زيادة رصيد حسب الجرد"), ("OUT", "عجز / تخفيض رصيد حسب الجرد"));
        var qty = Numeric(0.001m, 100000000m, 3);
        var date = new DateTimePicker { Width = 340, Format = DateTimePickerFormat.Custom, CustomFormat = "yyyy-MM-dd", Value = DateTime.Today };
        var reason = TextBox();
        AddField(body, "الصنف", product);
        AddField(body, "نوع التسوية", direction);
        AddField(body, "فرق الكمية", qty);
        AddField(body, "تاريخ الجرد", date);
        AddField(body, "سبب التسوية / رقم محضر الجرد", reason);
        save.Text = "ترحيل التسوية";
        save.Click += (_, _) => Guard(() => {
            var productId = Convert.ToInt32(product.SelectedValue, CultureInfo.InvariantCulture);
            var delta = Convert.ToString(direction.SelectedValue) == "OUT" ? -qty.Value : qty.Value;
            var id = _adjustmentService.AdjustStock(productId, date.Value.Date, delta, reason.Text);
            form.DialogResult = DialogResult.OK;
            form.Close();
            ShowProducts();
            SetStatus($"تم ترحيل تسوية المخزون رقم داخلي {id} مع قيد محاسبي وحركة مخزنية وسجل تدقيق.");
        });
        form.ShowDialog(this);
    }

    private void ShowStockAdjustments()
    {
        ShowTablePage("سجل تسويات المخزون المرحلة",
            _adjustmentService.Register(new DateTime(2000, 1, 1), DateTime.Today.AddDays(1)),
            ("تصدير CSV", ExportReport), ("تحديث", ShowStockAdjustments));
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
        using var form = new LegacyInvoiceEntryForm(_service, kind);
        if (form.ShowDialog(this) == DialogResult.OK)
        {
            ShowInvoices(kind);
            var invoiceNumber = form.PostedInvoiceNumber ?? "رقم غير متاح";
            SetStatus($"تم ترحيل الفاتورة {invoiceNumber} وتحديث القيد المحاسبي والمخزون داخل معاملة واحدة.");
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
        root.Controls.Add(new Label { Text="مركز التقارير المحاسبية", Dock=DockStyle.Fill, Font=new Font("Segoe UI",19,FontStyle.Bold), ForeColor=ThemeColor(_palette.ForegroundR, _palette.ForegroundG, _palette.ForegroundB), TextAlign=ContentAlignment.MiddleRight },0,0);
        var filter = new FlowLayoutPanel { Dock=DockStyle.Fill, FlowDirection=FlowDirection.RightToLeft, WrapContents=true, Padding=new Padding(4), BackColor=Color.White };
        var report=Combo("ميزان المراجعة",
            ("ميزان المراجعة","ميزان المراجعة"),("الأستاذ العام","الأستاذ العام"),("الأرباح والخسائر","الأرباح والخسائر"),
            ("الميزانية العمومية","الميزانية العمومية"),("تقييم المخزون","تقييم المخزون"),("سجل المبيعات","سجل المبيعات"),
            ("سجل المشتريات","سجل المشتريات"),("مردودات البيع","مردودات البيع"),("مردودات الشراء","مردودات الشراء"),("تقرير ضريبة القيمة المضافة","تقرير ضريبة القيمة المضافة"),
            ("حركة المخزون","حركة المخزون"),("تسوية المخزون","تسوية المخزون"),("سجل تسويات المخزون","سجل تسويات المخزون"),("سندات القبض والصرف","سندات القبض والصرف"),
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
                "مردودات البيع" => _returns.Register("SALE_RETURN",from.Value,to.Value),
                "مردودات الشراء" => _returns.Register("PURCHASE_RETURN",from.Value,to.Value),
                "تقرير ضريبة القيمة المضافة" => _service.VatSummary(from.Value,to.Value),
                "حركة المخزون" => _service.StockMovementRegister(from.Value,to.Value),
                "تسوية المخزون" => _service.StockReconciliation(),
                "سجل تسويات المخزون" => _adjustmentService.Register(from.Value,to.Value),
                "سندات القبض والصرف" => _service.SettlementRegister("ALL",from.Value,to.Value),
                "ملخص العملاء والموردين" => _service.PartyBalances(from.Value,to.Value),
                "سجل التدقيق" => IsAdmin ? _service.AuditHistory() : throw new InvalidOperationException("عرض سجل التدقيق متاح لمدير النظام فقط."),
                _ => throw new InvalidOperationException("التقرير غير معروف.")
            };
            _lastReport=table; _lastReportTitle=key; var grid=MakeGrid(table); var old=root.GetControlFromPosition(0,2); if(old is not null) root.Controls.Remove(old); root.Controls.Add(grid,0,2);
            SetStatus($"تم إعداد التقرير «{key}» — عدد الصفوف: {table.Rows.Count}. التصدير يطابق النتائج الحالية.");
        });
        var export=Button("تصدير CSV"); export.Click += (_,_) => ExportReport();
        var print=Button("معاينة الطباعة"); print.Click += (_,_) => Guard(() => {
            if (_lastReport is null) throw new InvalidOperationException("اعرض تقريرًا أولاً.");
            ReportPrintService.Preview(this, _lastReport, _lastReportTitle, _navy);
        });
        filter.Controls.Add(btn); filter.Controls.Add(export); filter.Controls.Add(print);
        root.Controls.Add(filter,0,1);
        var initialTrialBalance = _service.TrialBalance(from.Value,to.Value);
        root.Controls.Add(MakeGrid(initialTrialBalance),0,2);
        _lastReport=initialTrialBalance;
        _lastReportTitle="ميزان المراجعة";
        _content.Controls.Add(root);
    }

    private void ShowReportTable(string title, DataTable data)
    {
        _lastReportTitle=title;
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
        header.Controls.Add(new Label { Text=title, Dock=DockStyle.Right, Width=Math.Max(300,TextRenderer.MeasureText(title,Font).Width+60), Font=new Font("Segoe UI",16,FontStyle.Bold), ForeColor=ThemeColor(_palette.ForegroundR, _palette.ForegroundG, _palette.ForegroundB), TextAlign=ContentAlignment.MiddleRight });
        var toolbar=new FlowLayoutPanel { Dock=DockStyle.Left, AutoSize=true, FlowDirection=FlowDirection.RightToLeft, WrapContents=false, Padding=new Padding(2) };
        foreach(var action in actions.Concat(new (string Text, Action Run)[]
        {
            ("معاينة الطباعة", () => Guard(() => ReportPrintService.Preview(this, data, title, _navy)))
        }))
        {
            var button=Button(action.Text); button.Width=Math.Max(110,Math.Min(230,TextRenderer.MeasureText(action.Text,Font).Width+38)); button.Click += (_,_) => action.Run(); toolbar.Controls.Add(button);
        }
        header.Controls.Add(toolbar);
        _tableGrid=MakeGrid(data);
        root.Controls.Add(header,0,0); root.Controls.Add(_tableGrid,0,1); _content.Controls.Add(root);
        _lastReport=data;
        SetStatus($"عدد السجلات المعروضة: {data.Rows.Count}");
    }

    private DataGridView MakeGrid(DataTable data)
    {
        var surface = ThemeColor(_palette.SurfaceR, _palette.SurfaceG, _palette.SurfaceB);
        var grid=new DataGridView { Dock=DockStyle.Fill, DataSource=data, ReadOnly=true, AllowUserToAddRows=false, AllowUserToDeleteRows=false, MultiSelect=false, SelectionMode=DataGridViewSelectionMode.FullRowSelect, AutoSizeColumnsMode=DataGridViewAutoSizeColumnsMode.Fill, BackgroundColor=surface, BorderStyle=BorderStyle.FixedSingle, RowHeadersVisible=false, RightToLeft=RightToLeft.Yes, EnableHeadersVisualStyles=false };
        grid.ColumnHeadersDefaultCellStyle.BackColor=_navy;
        grid.ColumnHeadersDefaultCellStyle.ForeColor=Color.White;
        grid.ColumnHeadersDefaultCellStyle.Font=new Font("Segoe UI",9.5F,FontStyle.Bold);
        grid.DefaultCellStyle.SelectionBackColor=Color.FromArgb(_palette.AccentR, _palette.AccentG, _palette.AccentB);
        grid.DefaultCellStyle.SelectionForeColor=Color.White;
        grid.DefaultCellStyle.BackColor=surface;
        grid.DefaultCellStyle.ForeColor=ThemeColor(_palette.ForegroundR, _palette.ForegroundG, _palette.ForegroundB);
        grid.AlternatingRowsDefaultCellStyle.BackColor=_paper;
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
        if (!IsAdmin) { Denied(); return; }
        using var save=new SaveFileDialog { Filter="قاعدة بيانات SQLite (*.db)|*.db", FileName="AlSaqr-backup-"+DateTime.Now.ToString("yyyyMMdd-HHmm")+".db", InitialDirectory=Directory.Exists(_appSettings.BackupDirectory) ? _appSettings.BackupDirectory : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), AddExtension=true, DefaultExt="db" };
        if(save.ShowDialog(this)!=DialogResult.OK) return;
        Guard(() => {
            Data.Database.BackupTo(save.FileName);
            _appSettings = _appSettings with { BackupDirectory = Path.GetDirectoryName(save.FileName) ?? _appSettings.BackupDirectory };
            AppSettingsService.Save(_appSettings);
            SetStatus("تم إنشاء نسخة احتياطية متسقة وحفظ مجلد النسخ الاحتياطي الافتراضي.");
        });
    }
    private void Denied() => Notice("ليست لديك صلاحية تنفيذ هذه العملية.");
    private void Notice(string message) => MessageBox.Show(this,message,"الصقر لأنظمة المحاسبة",MessageBoxButtons.OK,MessageBoxIcon.Information);
    private void SetStatus(string message) => _status.Text=message;
    private void Guard(Action action) { try { action(); } catch(Exception ex) { MessageBox.Show(this,ex.Message,"تعذر إكمال العملية",MessageBoxButtons.OK,MessageBoxIcon.Warning); } }
    private sealed record Choice(string Value,string Text);
}

internal sealed class JournalEntryForm : Form
{
    private readonly LicenseService _licensing = new();
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
            if (!_licensing.CheckAccess().HasAccess)
                throw new InvalidOperationException("الترخيص غير صالح أو انتهت صلاحيته؛ لا يمكن ترحيل القيد.");
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
    private readonly LicenseService _licensing = new();
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
            if (!_licensing.CheckAccess().HasAccess)
                throw new InvalidOperationException("الترخيص غير صالح أو انتهت صلاحيته؛ لا يمكن ترحيل الفاتورة.");
            if(_party.SelectedValue is null) throw new InvalidOperationException("أضف الطرف المناسب واختره أولًا.");
            _service.CreateInvoice(_kind,_date.Value.Date,Convert.ToInt32(_party.SelectedValue),_credit.Checked,_lines.Select(l=>l.Input).ToList());
            DialogResult=DialogResult.OK; Close();
        }
        catch(Exception ex){MessageBox.Show(this,ex.Message,"تعذر ترحيل الفاتورة",MessageBoxButtons.OK,MessageBoxIcon.Warning);}
    }

    private sealed record PartyChoice(int Id,string Name,string Kind) { public string Text => Name; }
    private sealed record ProductChoice(int Id,string Text,decimal Price,decimal Cost);
}
