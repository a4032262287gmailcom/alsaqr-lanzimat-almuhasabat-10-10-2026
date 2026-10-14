using System.Data;
using System.Globalization;
using AlSaqr.Accounting.Services;

namespace AlSaqr.Accounting;

internal sealed class LegacyInvoiceEntryForm : Form
{
    private readonly LicenseService _licensing = new();
    private readonly AccountingService _service;
    private readonly string _kind;
    private readonly List<ProductInfo> _products;
    private readonly List<PartyChoice> _parties;
    private readonly ComboBox _party = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox _currency = new() { DropDownStyle = ComboBoxStyle.DropDownList, Enabled = false };
    private readonly TextBox _documentNumber = new() { ReadOnly = true, Text = "يصدر آليًا عند الاعتماد" };
    private readonly TextBox _purchaseOrder = new();
    private readonly TextBox _representative = new();
    private readonly TextBox _notes = new();
    private readonly TextBox _warehouse = new() { ReadOnly = true, Text = "المستودع الرئيسي" };
    private readonly DateTimePicker _date = new() { Format = DateTimePickerFormat.Custom, CustomFormat = "yyyy/MM/dd", Value = DateTime.Today };
    private readonly NumericUpDown _exchangeRate = new() { Minimum = 0.0001m, Maximum = 100000000m, DecimalPlaces = 4, Value = 1m, ReadOnly = true, Width = 90 };
    private readonly CheckBox _credit = new() { Text = "على الحساب (آجل)", AutoSize = true, Checked = true };
    private readonly DataGridView _linesGrid = new();
    private readonly Label _grossValue = new();
    private readonly Label _discountValue = new();
    private readonly Label _vatValue = new();
    private readonly Label _netValue = new();
    private readonly Label _statusText = new();
    private bool _updatingGrid;

    public string? PostedInvoiceNumber { get; private set; }

    public LegacyInvoiceEntryForm(AccountingService service, string kind)
    {
        _service = service;
        _kind = kind;
        var partyKind = kind == "SALE" ? "Customer" : "Supplier";
        var partyTable = service.Parties(partyKind);
        _parties = partyTable.Rows.Cast<DataRow>()
            .Select(r => new PartyChoice(Convert.ToInt32(r[0]), Convert.ToString(r[1]) ?? "", Convert.ToString(r[2]) ?? ""))
            .ToList();
        _products = service.ActiveProducts();

        Text = kind == "SALE" ? "بيانات المبيعات - الصقر لأنظمة المحاسبة" : "بيانات المشتريات - الصقر لأنظمة المحاسبة";
        Width = 1366;
        Height = 760;
        MinimumSize = new Size(1160, 680);
        StartPosition = FormStartPosition.CenterParent;
        RightToLeft = RightToLeft.Yes;
        RightToLeftLayout = true;
        KeyPreview = true;
        Font = new Font("Segoe UI", 9F);
        BackColor = Color.FromArgb(242, 244, 247);
        BuildLayout();
    }

    private void BuildLayout()
    {
        var shell = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, BackColor = Color.FromArgb(242, 244, 247), Margin = Padding.Empty, Padding = Padding.Empty };
        shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 66));
        shell.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));

        var tabs = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(249, 249, 249), Padding = new Padding(8, 1, 8, 1) };
        tabs.Controls.Add(MakeTab("عامة", false));
        tabs.Controls.Add(MakeTab(_kind == "PURCHASE" ? "فاتورة المشتريات  ×" : "فاتورة المبيعات  ×", true));

        var titlePanel = new Panel { Dock = DockStyle.Fill, BackColor = Color.White, Padding = new Padding(18, 7, 20, 5) };
        var title = new Label { Text = _kind == "PURCHASE" ? "بيانات المشتريات" : "بيانات المبيعات", Dock = DockStyle.Right, Width = 320, TextAlign = ContentAlignment.MiddleRight, ForeColor = Color.FromArgb(61, 115, 151), Font = new Font("Segoe UI", 21F, FontStyle.Bold) };
        var subtitle = new Label { Text = "الرجاء إدخال البيانات في الشاشة. تؤثر هذه البيانات على الكمية والتكلفة والحسابات", Dock = DockStyle.Bottom, Height = 25, TextAlign = ContentAlignment.MiddleRight, ForeColor = Color.FromArgb(101, 148, 169), Font = new Font("Segoe UI", 9F) };
        titlePanel.Controls.Add(title);
        titlePanel.Controls.Add(subtitle);

        var workspace = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, RightToLeft = RightToLeft.Yes, BackColor = Color.FromArgb(237, 239, 242), Margin = Padding.Empty, Padding = Padding.Empty };
        workspace.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 42));
        workspace.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 158));
        workspace.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        workspace.Controls.Add(BuildIconRail(), 0, 0);
        workspace.Controls.Add(BuildActionsPanel(), 1, 0);
        workspace.Controls.Add(BuildDocumentPanel(), 2, 0);

        _statusText.Dock = DockStyle.Fill;
        _statusText.TextAlign = ContentAlignment.MiddleRight;
        _statusText.ForeColor = Color.FromArgb(74, 87, 97);
        _statusText.Text = $"النشاط: {(_kind == "PURCHASE" ? "المشتريات" : "المبيعات")}   |   العملة: الريال السعودي   |   عدد السطور: 0   |   F5: تكرار السطر   |   F6: سطر جديد   |   Ctrl+S: اعتماد";
        var status = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(235, 237, 239), Padding = new Padding(10, 2, 10, 2) };
        status.Controls.Add(_statusText);

        shell.Controls.Add(tabs, 0, 0);
        shell.Controls.Add(titlePanel, 0, 1);
        shell.Controls.Add(workspace, 0, 2);
        shell.Controls.Add(status, 0, 3);
        Controls.Add(shell);
        RefreshTotals();
    }

    private static Control MakeTab(string caption, bool active)
    {
        var tab = new Button { Text = caption, Dock = DockStyle.Right, Width = active ? 150 : 80, Height = 27, FlatStyle = FlatStyle.Flat, BackColor = active ? Color.White : Color.FromArgb(244, 244, 244), ForeColor = active ? Color.FromArgb(42, 94, 132) : Color.FromArgb(90, 90, 90), Font = new Font("Segoe UI", 8.5F), Margin = Padding.Empty };
        tab.FlatAppearance.BorderColor = Color.FromArgb(214, 218, 222);
        tab.FlatAppearance.BorderSize = 1;
        return tab;
    }

    private Control BuildDocumentPanel()
    {
        var panel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, BackColor = Color.FromArgb(242, 244, 247), Padding = new Padding(10, 6, 10, 6), Margin = Padding.Empty };
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 142));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 120));

        var fields = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 3, Padding = new Padding(2), BackColor = Color.FromArgb(242, 244, 247) };
        for (var i = 0; i < 4; i++) fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25F));
        fields.RowStyles.Add(new RowStyle(SizeType.Absolute, 47));
        fields.RowStyles.Add(new RowStyle(SizeType.Absolute, 47));
        fields.RowStyles.Add(new RowStyle(SizeType.Absolute, 43));

        var docKind = new TextBox { ReadOnly = true, Text = _kind == "PURCHASE" ? "مورد محلي" : "بيع محلي" };
        _currency.Items.Add(new CurrencyChoice("SAR", "الريال السعودي"));
        _currency.DisplayMember = "Text";
        _currency.ValueMember = "Code";
        _currency.SelectedIndex = 0;

        fields.Controls.Add(MakeField("الرمز:", _documentNumber), 0, 0);
        fields.Controls.Add(MakeField("التاريخ:", _date), 1, 0);
        fields.Controls.Add(MakeField("مرجع الفاتورة:", ReadOnlyText("يصدر عند الاعتماد")), 2, 0);
        fields.Controls.Add(MakeField("نوع الطلب:", docKind), 3, 0);

        _party.DisplayMember = "Text";
        _party.ValueMember = "Id";
        _party.DataSource = _parties;
        fields.Controls.Add(MakeField(_kind == "PURCHASE" ? "المورد:" : "العميل:", _party), 0, 1);
        fields.Controls.Add(MakeField(_kind == "PURCHASE" ? "مندوب شراء:" : "مندوب مبيعات:", _representative), 1, 1);
        fields.Controls.Add(MakeField("المستودع:", _warehouse), 2, 1);

        var currencyPanel = new Panel { Dock = DockStyle.Fill, Margin = new Padding(4, 2, 4, 2) };
        var currencyLabel = new Label { Text = "العملة:", Dock = DockStyle.Right, Width = 45, TextAlign = ContentAlignment.MiddleRight, ForeColor = Color.FromArgb(44, 88, 117) };
        _currency.Width = 132;
        _exchangeRate.Width = 78;
        _currency.Dock = DockStyle.Left;
        _exchangeRate.Dock = DockStyle.Right;
        currencyPanel.Controls.Add(_currency);
        currencyPanel.Controls.Add(_exchangeRate);
        currencyPanel.Controls.Add(currencyLabel);
        fields.Controls.Add(currencyPanel, 3, 1);

        fields.Controls.Add(MakeField("طلب الشراء / المرجع:", _purchaseOrder), 0, 2);
        var creditPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8, 10, 8, 0), Margin = new Padding(4) };
        _credit.Dock = DockStyle.Right;
        creditPanel.Controls.Add(_credit);
        fields.Controls.Add(creditPanel, 1, 2);
        _notes.Multiline = true;
        _notes.ScrollBars = ScrollBars.Vertical;
        fields.Controls.Add(MakeField("الملاحظات:", _notes), 2, 2);
        fields.SetColumnSpan(fields.GetControlFromPosition(2, 2)!, 2);

        BuildLinesGrid();
        var gridPanel = new Panel { Dock = DockStyle.Fill, BackColor = Color.White, Padding = new Padding(1), Margin = new Padding(0, 3, 0, 3) };
        gridPanel.Controls.Add(_linesGrid);

        panel.Controls.Add(fields, 0, 0);
        panel.Controls.Add(gridPanel, 0, 1);
        panel.Controls.Add(BuildTotalsPanel(), 0, 2);
        return panel;
    }

    private void BuildLinesGrid()
    {
        _linesGrid.Dock = DockStyle.Fill;
        _linesGrid.AllowUserToAddRows = true;
        _linesGrid.AllowUserToDeleteRows = true;
        _linesGrid.MultiSelect = false;
        _linesGrid.SelectionMode = DataGridViewSelectionMode.CellSelect;
        _linesGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.DisplayedCells;
        _linesGrid.RowHeadersVisible = false;
        _linesGrid.RightToLeft = RightToLeft.Yes;
        _linesGrid.BackgroundColor = Color.White;
        _linesGrid.BorderStyle = BorderStyle.FixedSingle;
        _linesGrid.GridColor = Color.FromArgb(210, 214, 218);
        _linesGrid.RowTemplate.Height = 25;
        _linesGrid.Font = new Font("Segoe UI", 8.5F);
        _linesGrid.EnableHeadersVisualStyles = false;
        _linesGrid.ColumnHeadersHeight = 29;
        _linesGrid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(250, 250, 250);
        _linesGrid.ColumnHeadersDefaultCellStyle.ForeColor = Color.FromArgb(70, 81, 91);
        _linesGrid.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI", 8F, FontStyle.Bold);
        _linesGrid.DefaultCellStyle.BackColor = Color.White;
        _linesGrid.DefaultCellStyle.ForeColor = Color.FromArgb(47, 54, 59);
        _linesGrid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(213, 231, 243);
        _linesGrid.DefaultCellStyle.SelectionForeColor = Color.FromArgb(27, 49, 64);
        _linesGrid.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(249, 250, 251);
        _linesGrid.DataError += (_, e) => { e.ThrowException = false; };

        AddGridText("LineNo", "سطر", 42, true);
        AddGridText("Barcode", "الباركود", 72, true);
        AddGridText("ProductCode", "رمز الصنف", 88, true);
        _linesGrid.Columns.Add(new DataGridViewComboBoxColumn
        {
            Name = "ProductId",
            HeaderText = "الصنف",
            Width = 190,
            FlatStyle = FlatStyle.Flat,
            DisplayMember = "Text",
            ValueMember = "Id",
            DataSource = _products.Select(p => new ProductChoice(p.Id, $"{p.Code} - {p.Name}", p.Code, p.Unit, p.Price, p.Cost, p.VatRate)).ToList()
        });
        AddGridText("Unit", "رمز الوحدة", 65, true);
        AddGridNumber("Quantity", "الكمية", 70, false, 3);
        AddGridNumber("UnitPrice", "السعر", 82, false, 2);
        AddGridNumber("DiscountValue", "خصم مبلغ", 78, true, 2);
        AddGridNumber("DiscountPercent", "خصم نسبة %", 78, false, 2);
        AddGridNumber("VatRate", "الضريبة %", 75, true, 2);
        AddGridNumber("Net", "الصافي", 85, true, 2);
        AddGridNumber("Vat", "الضريبة", 78, true, 2);
        AddGridNumber("Total", "الإجمالي", 88, true, 2);

        _linesGrid.CurrentCellDirtyStateChanged += (_, _) => { if (_linesGrid.IsCurrentCellDirty) _linesGrid.CommitEdit(DataGridViewDataErrorContexts.Commit); };
        _linesGrid.CellValueChanged += (_, e) =>
        {
            if (_updatingGrid || e.RowIndex < 0 || e.ColumnIndex < 0) return;
            var row = _linesGrid.Rows[e.RowIndex];
            if (_linesGrid.Columns[e.ColumnIndex].Name == "ProductId") PopulateProduct(row);
            else RecalculateLine(row);
            RefreshTotals();
        };
        _linesGrid.RowsRemoved += (_, _) => RefreshTotals();
        _linesGrid.CellEndEdit += (_, _) => RefreshTotals();
    }

    private void AddGridText(string name, string caption, int width, bool readOnly)
    {
        _linesGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = name, HeaderText = caption, Width = width, ReadOnly = readOnly, SortMode = DataGridViewColumnSortMode.NotSortable });
    }

    private void AddGridNumber(string name, string caption, int width, bool readOnly, int decimals)
    {
        _linesGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = name, HeaderText = caption, Width = width, ReadOnly = readOnly, ValueType = typeof(decimal),
            DefaultCellStyle = { Format = decimals == 3 ? "N3" : "N2", Alignment = DataGridViewContentAlignment.MiddleRight },
            SortMode = DataGridViewColumnSortMode.NotSortable
        });
    }

    private Control BuildTotalsPanel()
    {
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, BackColor = Color.FromArgb(242, 244, 247), Padding = new Padding(0, 4, 0, 2) };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 64F));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 36F));
        var numbers = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 1, BackColor = Color.White, Padding = new Padding(5) };
        for (var i = 0; i < 4; i++) numbers.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25F));
        AddTotalCell(numbers, "الإجمالي:", _grossValue, 0);
        AddTotalCell(numbers, "الخصم:", _discountValue, 1);
        AddTotalCell(numbers, "إجمالي الضريبة:", _vatValue, 2);
        AddTotalCell(numbers, "الصافي بعد الخصم:", _netValue, 3);
        var hint = new Label
        {
            Dock = DockStyle.Fill,
            Text = "بيانات الأصناف  ▣     مصروفات غير مخزنة  ◈\r\n\r\nF5: تكرار السطر المحدد   |   F6: سطر جديد\r\nF7: تعديل السطر   |   F8: حذف السطر",
            TextAlign = ContentAlignment.MiddleRight,
            ForeColor = Color.FromArgb(72, 121, 139),
            Font = new Font("Segoe UI", 8.5F),
            Padding = new Padding(8),
            BackColor = Color.White
        };
        root.Controls.Add(numbers, 0, 0);
        root.Controls.Add(hint, 1, 0);
        return root;
    }

    private static void AddTotalCell(TableLayoutPanel parent, string caption, Label value, int column)
    {
        var cell = new Panel { Dock = DockStyle.Fill, Padding = new Padding(5), Margin = new Padding(1), BackColor = Color.White };
        var title = new Label { Text = caption, Dock = DockStyle.Top, Height = 25, TextAlign = ContentAlignment.MiddleRight, ForeColor = Color.FromArgb(72, 98, 116) };
        value.Dock = DockStyle.Fill;
        value.Text = "0.00";
        value.TextAlign = ContentAlignment.MiddleRight;
        value.Font = new Font("Segoe UI", 11F, FontStyle.Bold);
        value.ForeColor = Color.FromArgb(45, 82, 105);
        cell.Controls.Add(value);
        cell.Controls.Add(title);
        parent.Controls.Add(cell, column, 0);
    }

    private Control BuildActionsPanel()
    {
        var root = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(242, 244, 247), Padding = new Padding(3, 6, 3, 3) };
        var navigation = new TableLayoutPanel { Dock = DockStyle.Top, Height = 76, ColumnCount = 2, RowCount = 2, RightToLeft = RightToLeft.Yes, BackColor = Color.FromArgb(242, 244, 247) };
        navigation.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
        navigation.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
        navigation.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));
        navigation.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));
        AddSmallAction(navigation, "الأخير  »", null, 0, 0);
        AddSmallAction(navigation, "الأول  «", null, 1, 0);
        AddSmallAction(navigation, "التالي  ›", null, 0, 1);
        AddSmallAction(navigation, "السابق  ‹", null, 1, 1);

        var grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 7, RightToLeft = RightToLeft.Yes, BackColor = Color.FromArgb(242, 244, 247), Padding = new Padding(0, 4, 0, 0) };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
        for (var i = 0; i < 7; i++) grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 41));
        AddSmallAction(grid, "إلغاء", ResetEntry, 0, 0);
        AddSmallAction(grid, "اعتماد", Post, 1, 0, true);
        AddSmallAction(grid, "بحث", null, 0, 1);
        AddSmallAction(grid, "طباعة", null, 1, 1);
        AddSmallAction(grid, "إضافة", AddBlankLine, 0, 2);
        AddSmallAction(grid, "تعديل", EditCurrentCell, 1, 2);
        AddSmallAction(grid, "حفظ", Post, 0, 3, true);
        AddSmallAction(grid, "تراجع", UndoCurrentLine, 1, 3);
        AddSmallAction(grid, "إغلاق", CloseEntry, 0, 4);
        AddSmallAction(grid, "حذف", DeleteCurrentLine, 1, 4);
        AddSmallAction(grid, "F5 سطر", DuplicateCurrentLine, 0, 5);
        AddSmallAction(grid, "F6 جديد", AddBlankLine, 1, 5);
        AddSmallAction(grid, "مسح", ClearCurrentSelection, 0, 6);
        AddSmallAction(grid, "تحديث", RefreshTotals, 1, 6);
        root.Controls.Add(grid);
        root.Controls.Add(navigation);
        return root;
    }

    private static void AddSmallAction(TableLayoutPanel parent, string caption, Action? action, int column, int row, bool primary = false)
    {
        var button = new Button
        {
            Text = caption, Dock = DockStyle.Fill, Height = 35, Margin = new Padding(2), Padding = new Padding(1),
            FlatStyle = FlatStyle.Flat,
            BackColor = primary ? Color.FromArgb(219, 230, 237) : Color.FromArgb(248, 248, 248),
            ForeColor = Color.FromArgb(65, 77, 85),
            Font = new Font("Segoe UI", 8.2F, primary ? FontStyle.Bold : FontStyle.Regular),
            Cursor = action is null ? Cursors.Default : Cursors.Hand,
            Enabled = action is not null
        };
        button.FlatAppearance.BorderColor = Color.FromArgb(207, 213, 218);
        button.FlatAppearance.BorderSize = 1;
        if (action is not null) button.Click += (_, _) => action();
        parent.Controls.Add(button, column, row);
    }

    private Control BuildIconRail()
    {
        var rail = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, BackColor = Color.FromArgb(247, 248, 249), Padding = new Padding(2, 6, 2, 2) };
        var tips = new ToolTip();
        foreach (var item in new[] { ("★", "المفضلة"), ("▣", "المستندات"), ("⚖", "الحسابات"), ("♟", "المستخدمون"), ("⚙", "الإعدادات"), ("⌕", "البحث"), ("⚒", "الأدوات"), ("?", "المساعدة") })
        {
            var button = new Button { Text = item.Item1, Width = 34, Height = 36, Margin = new Padding(0, 2, 0, 2), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(247, 248, 249), ForeColor = Color.FromArgb(74, 85, 94), Font = new Font("Segoe UI Symbol", 13F), Enabled = false };
            button.FlatAppearance.BorderSize = 0;
            tips.SetToolTip(button, item.Item2);
            rail.Controls.Add(button);
        }
        return rail;
    }

    private static Control MakeField(string caption, Control input)
    {
        var group = new Panel { Dock = DockStyle.Fill, Margin = new Padding(4, 2, 4, 2), Padding = new Padding(2), BackColor = Color.FromArgb(242, 244, 247) };
        input.Dock = DockStyle.Bottom;
        input.Height = Math.Max(25, input.Height);
        input.Font = new Font("Segoe UI", 8.5F);
        input.Margin = Padding.Empty;
        var label = new Label { Text = caption, Dock = DockStyle.Top, Height = 20, ForeColor = Color.FromArgb(52, 91, 116), TextAlign = ContentAlignment.MiddleRight, Font = new Font("Segoe UI", 8.5F) };
        group.Controls.Add(input);
        group.Controls.Add(label);
        return group;
    }

    private static TextBox ReadOnlyText(string text) => new() { Text = text, ReadOnly = true, BorderStyle = BorderStyle.FixedSingle };

    private void PopulateProduct(DataGridViewRow row)
    {
        if (_updatingGrid || row.IsNewRow) return;
        if (!int.TryParse(Convert.ToString(row.Cells["ProductId"].Value, CultureInfo.InvariantCulture), out var id))
        {
            RecalculateLine(row);
            return;
        }
        var product = _products.FirstOrDefault(p => p.Id == id);
        if (product is null) return;
        _updatingGrid = true;
        try
        {
            row.Cells["ProductCode"].Value = product.Code;
            row.Cells["Barcode"].Value = "";
            row.Cells["Unit"].Value = product.Unit;
            var quantity = TryDecimal(row.Cells["Quantity"].Value, 1m);
            row.Cells["Quantity"].Value = quantity > 0m ? quantity : 1m;
            row.Cells["UnitPrice"].Value = _kind == "PURCHASE" ? product.Cost : product.Price;
            row.Cells["DiscountPercent"].Value = TryDecimal(row.Cells["DiscountPercent"].Value, 0m);
            row.Cells["VatRate"].Value = product.VatRate;
            var number = _linesGrid.Rows.Cast<DataGridViewRow>().Count(r => !r.IsNewRow && r.Cells["ProductId"].Value is not null);
            row.Cells["LineNo"].Value = number;
        }
        finally { _updatingGrid = false; }
        RecalculateLine(row);
    }

    private void RecalculateLine(DataGridViewRow row)
    {
        if (row.IsNewRow) return;
        if (!int.TryParse(Convert.ToString(row.Cells["ProductId"].Value, CultureInfo.InvariantCulture), out _))
        {
            _updatingGrid = true;
            try { row.Cells["DiscountValue"].Value = 0m; row.Cells["Net"].Value = 0m; row.Cells["Vat"].Value = 0m; row.Cells["Total"].Value = 0m; }
            finally { _updatingGrid = false; }
            return;
        }
        var quantity = TryDecimal(row.Cells["Quantity"].Value, 0m);
        var price = TryDecimal(row.Cells["UnitPrice"].Value, 0m);
        var discountPercent = TryDecimal(row.Cells["DiscountPercent"].Value, 0m);
        var vatPercent = TryDecimal(row.Cells["VatRate"].Value, 0m);
        if (quantity < 0m || price < 0m || discountPercent < 0m || discountPercent >= 100m || vatPercent < 0m) return;
        var gross = AccountingService.ToHalalas(quantity * price);
        var discount = AccountingService.ToHalalas(AccountingService.FromHalalas(gross) * discountPercent / 100m);
        var net = Math.Max(0L, gross - discount);
        var vat = AccountingService.ToHalalas(AccountingService.FromHalalas(net) * vatPercent / 100m);
        _updatingGrid = true;
        try
        {
            row.Cells["DiscountValue"].Value = AccountingService.FromHalalas(discount);
            row.Cells["Net"].Value = AccountingService.FromHalalas(net);
            row.Cells["Vat"].Value = AccountingService.FromHalalas(vat);
            row.Cells["Total"].Value = AccountingService.FromHalalas(net + vat);
        }
        finally { _updatingGrid = false; }
    }

    private void RefreshTotals()
    {
        decimal gross = 0m, discount = 0m, net = 0m, vat = 0m;
        var count = 0;
        foreach (DataGridViewRow row in _linesGrid.Rows)
        {
            if (row.IsNewRow || !int.TryParse(Convert.ToString(row.Cells["ProductId"].Value, CultureInfo.InvariantCulture), out _)) continue;
            var quantity = TryDecimal(row.Cells["Quantity"].Value, 0m);
            var price = TryDecimal(row.Cells["UnitPrice"].Value, 0m);
            var disc = TryDecimal(row.Cells["DiscountPercent"].Value, 0m);
            var rate = TryDecimal(row.Cells["VatRate"].Value, 0m);
            if (quantity <= 0m || price < 0m || disc < 0m || disc >= 100m) continue;
            var rowGross = AccountingService.FromHalalas(AccountingService.ToHalalas(quantity * price));
            var rowDiscount = AccountingService.FromHalalas(AccountingService.ToHalalas(rowGross * disc / 100m));
            var rowNet = rowGross - rowDiscount;
            var rowVat = AccountingService.FromHalalas(AccountingService.ToHalalas(rowNet * rate / 100m));
            gross += rowGross; discount += rowDiscount; net += rowNet; vat += rowVat; count++;
        }
        _grossValue.Text = FormatMoney(gross);
        _discountValue.Text = FormatMoney(discount);
        _vatValue.Text = FormatMoney(vat);
        _netValue.Text = FormatMoney(net + vat);
        _statusText.Text = $"النشاط: {(_kind == "PURCHASE" ? "المشتريات" : "المبيعات")}   |   العملة: الريال السعودي   |   عدد السطور: {count}   |   F5: تكرار السطر   |   F6: سطر جديد   |   Ctrl+S: اعتماد";
    }

    private static decimal TryDecimal(object? value, decimal fallback)
    {
        if (value is null || value == DBNull.Value) return fallback;
        if (decimal.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed)) return parsed;
        if (decimal.TryParse(Convert.ToString(value), NumberStyles.Any, CultureInfo.CurrentCulture, out parsed)) return parsed;
        return fallback;
    }

    private static string FormatMoney(decimal value) => value.ToString("N2", CultureInfo.GetCultureInfo("ar-SA")) + " ر.س";

    private void AddBlankLine()
    {
        _linesGrid.EndEdit();
        var index = _linesGrid.Rows.Add();
        _linesGrid.Rows[index].Cells["Quantity"].Value = 1m;
        _linesGrid.Rows[index].Cells["DiscountPercent"].Value = 0m;
        _linesGrid.CurrentCell = _linesGrid.Rows[index].Cells["ProductId"];
        _linesGrid.BeginEdit(true);
    }

    private void EditCurrentCell()
    {
        if (_linesGrid.CurrentCell is not null) _linesGrid.BeginEdit(true);
    }

    private void DeleteCurrentLine()
    {
        if (_linesGrid.CurrentRow is null || _linesGrid.CurrentRow.IsNewRow) return;
        _linesGrid.Rows.Remove(_linesGrid.CurrentRow);
        RenumberRows();
        RefreshTotals();
    }

    private void UndoCurrentLine()
    {
        if (_linesGrid.IsCurrentCellInEditMode) _linesGrid.CancelEdit();
        else DeleteCurrentLine();
    }

    private void DuplicateCurrentLine()
    {
        if (_linesGrid.CurrentRow is null || _linesGrid.CurrentRow.IsNewRow) { AddBlankLine(); return; }
        var source = _linesGrid.CurrentRow;
        _linesGrid.EndEdit();
        var index = _linesGrid.Rows.Add();
        var target = _linesGrid.Rows[index];
        foreach (var name in new[] { "ProductId", "Quantity", "UnitPrice", "DiscountPercent" })
            target.Cells[name].Value = source.Cells[name].Value;
        PopulateProduct(target);
        RenumberRows();
        RefreshTotals();
    }

    private void ClearCurrentSelection()
    {
        if (_linesGrid.CurrentRow is null || _linesGrid.CurrentRow.IsNewRow) return;
        var row = _linesGrid.CurrentRow;
        _updatingGrid = true;
        try { foreach (DataGridViewCell cell in row.Cells) cell.Value = null; row.Cells["Quantity"].Value = 1m; row.Cells["DiscountPercent"].Value = 0m; }
        finally { _updatingGrid = false; }
        RefreshTotals();
    }

    private void ResetEntry()
    {
        if (MessageBox.Show(this, "هل تريد مسح السطور الحالية والبدء من جديد؟", "بيانات المستند", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        for (var i = _linesGrid.Rows.Count - 1; i >= 0; i--)
            if (!_linesGrid.Rows[i].IsNewRow) _linesGrid.Rows.RemoveAt(i);
        _purchaseOrder.Clear();
        _representative.Clear();
        _notes.Clear();
        RefreshTotals();
    }

    private void CloseEntry() { DialogResult = DialogResult.Cancel; Close(); }

    private void RenumberRows()
    {
        var counter = 0;
        _updatingGrid = true;
        try { foreach (DataGridViewRow row in _linesGrid.Rows) if (!row.IsNewRow && row.Cells["ProductId"].Value is not null) row.Cells["LineNo"].Value = ++counter; }
        finally { _updatingGrid = false; }
    }

    private List<InvoiceLineInput> CollectLines()
    {
        _linesGrid.EndEdit();
        var result = new List<InvoiceLineInput>();
        foreach (DataGridViewRow row in _linesGrid.Rows)
        {
            if (row.IsNewRow) continue;
            if (!int.TryParse(Convert.ToString(row.Cells["ProductId"].Value, CultureInfo.InvariantCulture), out var id))
            {
                if (TryDecimal(row.Cells["Quantity"].Value, 0m) != 0m)
                    throw new InvalidOperationException("اختر الصنف في كل سطر يحتوي على كمية.");
                continue;
            }
            var quantity = TryDecimal(row.Cells["Quantity"].Value, 0m);
            var price = TryDecimal(row.Cells["UnitPrice"].Value, 0m);
            var discount = TryDecimal(row.Cells["DiscountPercent"].Value, 0m);
            if (quantity <= 0m) throw new InvalidOperationException("الكمية يجب أن تكون أكبر من صفر.");
            if (price < 0m) throw new InvalidOperationException("السعر لا يمكن أن يكون سالبًا.");
            if (discount < 0m || discount >= 100m) throw new InvalidOperationException("نسبة خصم السطر يجب أن تكون من صفر إلى أقل من 100%.");
            result.Add(new InvoiceLineInput(id, quantity, price, discount));
        }
        if (result.Count == 0) throw new InvalidOperationException("أضف صنفًا واحدًا على الأقل إلى الفاتورة.");
        return result;
    }

    private string BuildNotes()
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(_purchaseOrder.Text)) parts.Add("مرجع طلب الشراء: " + _purchaseOrder.Text.Trim());
        if (!string.IsNullOrWhiteSpace(_representative.Text)) parts.Add((_kind == "PURCHASE" ? "مندوب الشراء: " : "مندوب المبيعات: ") + _representative.Text.Trim());
        if (!string.IsNullOrWhiteSpace(_notes.Text)) parts.Add("ملاحظات: " + _notes.Text.Trim());
        return string.Join(" | ", parts);
    }

    private void Post()
    {
        try
        {
            if (!_licensing.CheckAccess().HasAccess)
                throw new InvalidOperationException("الترخيص غير صالح أو انتهت صلاحيته؛ لا يمكن ترحيل الفاتورة.");
            if (_party.SelectedValue is null)
                throw new InvalidOperationException(_kind == "PURCHASE" ? "أضف المورد واختره أولًا." : "أضف العميل واختره أولًا.");
            var lines = CollectLines();
            _service.CreateInvoice(_kind, _date.Value.Date, Convert.ToInt32(_party.SelectedValue, CultureInfo.InvariantCulture), _credit.Checked, lines, notes: BuildNotes());
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "تعذر اعتماد الفاتورة", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.F5) { DuplicateCurrentLine(); return true; }
        if (keyData == Keys.F6) { AddBlankLine(); return true; }
        if (keyData == Keys.F7) { EditCurrentCell(); return true; }
        if (keyData == Keys.F8 || keyData == Keys.Delete) { DeleteCurrentLine(); return true; }
        if (keyData == (Keys.Control | Keys.S)) { Post(); return true; }
        if (keyData == Keys.Escape) { CloseEntry(); return true; }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    private sealed record PartyChoice(int Id, string Name, string Kind) { public string Text => Name; }
    private sealed record ProductChoice(int Id, string Text, string Code, string Unit, decimal Price, decimal Cost, decimal VatRate);
    private sealed record CurrencyChoice(string Code, string Text);
}