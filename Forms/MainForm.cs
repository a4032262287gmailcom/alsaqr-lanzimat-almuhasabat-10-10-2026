using System.Data;
using AlSaqr.Accounting.Data;

namespace AlSaqr.Accounting.Forms;

public sealed class MainForm : Form
{
    private readonly DataGridView _grid = new();
    private readonly Label _title = new();
    private readonly Label _status = new();
    private readonly TextBox _search = new();
    private readonly Button _add = new() { Text = "إضافة", Width = 85, Height = 30 };
    private readonly Button _edit = new() { Text = "تعديل", Width = 85, Height = 30 };
    private readonly Button _refresh = new() { Text = "تحديث", Width = 85, Height = 30 };
    private string _view = "dashboard";

    public MainForm()
    {
        Text = "الصقر لأنظمة المحاسبة";
        StartPosition = FormStartPosition.CenterScreen;
        WindowState = FormWindowState.Maximized;
        MinimumSize = new Size(1100, 680);
        Font = new Font("Segoe UI", 10F);
        RightToLeft = RightToLeft.Yes;
        RightToLeftLayout = true;
        BuildLayout();
        Navigate("dashboard");
    }

    private void BuildLayout()
    {
        var header = new Panel { Dock = DockStyle.Top, Height = 66, BackColor = Color.FromArgb(24, 42, 68) };
        header.Controls.Add(new Label { Text = "الصقر | نظام المحاسبة", Dock = DockStyle.Right, Width = 340, ForeColor = Color.White, Font = new Font("Segoe UI", 17F, FontStyle.Bold), TextAlign = ContentAlignment.MiddleCenter });
        header.Controls.Add(new Label { Text = "الحسابات • المبيعات • المشتريات • المخزون • التقارير", Dock = DockStyle.Fill, ForeColor = Color.WhiteSmoke, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(18, 0, 0, 0) });

        var nav = new FlowLayoutPanel { Dock = DockStyle.Right, Width = 225, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, BackColor = Color.FromArgb(33, 52, 79), Padding = new Padding(8) };
        AddNav(nav, "الرئيسية", "dashboard");
        AddNav(nav, "دليل الحسابات", "accounts");
        AddNav(nav, "العملاء والموردون", "parties");
        AddNav(nav, "الأصناف والمخزون", "products");
        AddNav(nav, "القيود اليومية", "journals");
        AddNav(nav, "فواتير البيع والشراء", "invoices");
        AddNav(nav, "سندات القبض والصرف", "payments");
        AddNav(nav, "التقارير المالية", "reports");
        AddNav(nav, "سجل التدقيق", "audit");

        var content = new Panel { Dock = DockStyle.Fill, Padding = new Padding(20), BackColor = Color.FromArgb(245, 247, 250) };
        _title.Dock = DockStyle.Top;
        _title.Height = 48;
        _title.Font = new Font("Segoe UI", 16F, FontStyle.Bold);
        _title.ForeColor = Color.FromArgb(28, 43, 64);
        var tools = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 44, FlowDirection = FlowDirection.RightToLeft, WrapContents = false };
        _search.Width = 220;
        _search.PlaceholderText = "بحث في البيانات...";
        _search.TextChanged += (_, _) => ApplySearch();
        _refresh.Click += (_, _) => Navigate(_view);
        _add.Click += (_, _) => AddRecord();
        _edit.Click += (_, _) => EditRecord();
        var invoice = new Button { Text = "فاتورة جديدة", Width = 110, Height = 30 };
        invoice.Click += (_, _) => { using var form = new InvoiceForm(); if (form.ShowDialog(this) == DialogResult.OK) Navigate(_view); };
        var journal = new Button { Text = "قيد يومي", Width = 95, Height = 30 };
        journal.Click += (_, _) => { using var form = new JournalEntryForm(); if (form.ShowDialog(this) == DialogResult.OK) Navigate(_view); };
        var reports = new Button { Text = "التقارير", Width = 85, Height = 30 };
        reports.Click += (_, _) => { using var form = new ReportsForm(); form.ShowDialog(this); };
        var payment = new Button { Text = "سند قبض/صرف", Width = 115, Height = 30 };
        payment.Click += (_, _) => { using var form = new PartyPaymentForm(); if (form.ShowDialog(this) == DialogResult.OK) Navigate(_view); };
        tools.Controls.Add(_refresh);
        tools.Controls.Add(_add);
        tools.Controls.Add(_edit);
        tools.Controls.Add(invoice);
        tools.Controls.Add(journal);
        tools.Controls.Add(payment);
        tools.Controls.Add(reports);
        tools.Controls.Add(_search);

        _grid.Dock = DockStyle.Fill;
        _grid.ReadOnly = true;
        _grid.AllowUserToAddRows = false;
        _grid.AllowUserToDeleteRows = false;
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.MultiSelect = false;
        _grid.RowHeadersVisible = false;
        _grid.BackgroundColor = Color.White;
        _grid.BorderStyle = BorderStyle.None;
        _grid.EnableHeadersVisualStyles = false;
        _grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(231, 237, 245);
        _grid.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI", 10F, FontStyle.Bold);
        _grid.ColumnHeadersHeight = 38;
        _status.Dock = DockStyle.Bottom;
        _status.Height = 28;
        _status.ForeColor = Color.DimGray;
        _status.TextAlign = ContentAlignment.MiddleRight;

        content.Controls.Add(_grid);
        content.Controls.Add(tools);
        content.Controls.Add(_title);
        content.Controls.Add(_status);
        Controls.Add(content);
        Controls.Add(nav);
        Controls.Add(header);
    }

    private static void AddNav(Control parent, string text, string view)
    {
        var button = new Button { Text = text, Tag = view, Width = 205, Height = 46, FlatStyle = FlatStyle.Flat, ForeColor = Color.White, BackColor = Color.FromArgb(33, 52, 79), TextAlign = ContentAlignment.MiddleRight, Padding = new Padding(0, 0, 12, 0), Cursor = Cursors.Hand };
        button.FlatAppearance.BorderSize = 0;
        button.Click += (sender, _) => ((MainForm)((Control)sender!).FindForm()!).Navigate(view);
        parent.Controls.Add(button);
    }

    private void Navigate(string view)
    {
        _view = view;
        _search.Clear();
        _grid.DataSource = null;
        _add.Visible = view is "accounts" or "parties" or "products";
        _edit.Visible = _add.Visible;
        string? sql = view switch
        {
            "accounts" => "SELECT Code AS 'رمز الحساب', Name AS 'اسم الحساب', Type AS 'نوع الحساب', IsActive AS 'نشط' FROM Accounts ORDER BY Code",
            "parties" => "SELECT Id AS 'الرقم', Name AS 'الاسم', Kind AS 'التصنيف', Phone AS 'الهاتف', VatNumber AS 'الرقم الضريبي', IsActive AS 'نشط' FROM Parties ORDER BY Name",
            "products" => "SELECT Id AS 'الرقم', Code AS 'رمز الصنف', Name AS 'اسم الصنف', Unit AS 'الوحدة', Quantity AS 'الكمية', CostHalalas / 100.0 AS 'التكلفة بالريال', PriceHalalas / 100.0 AS 'السعر بالريال', VatRate AS 'الضريبة %', IsActive AS 'نشط' FROM Products ORDER BY Name",
            "journals" => "SELECT Id AS 'الرقم', Number AS 'رقم القيد', EntryDate AS 'التاريخ', Memo AS 'البيان', Source AS 'المصدر', Status AS 'الحالة' FROM Journals ORDER BY EntryDate DESC, Id DESC",
            "invoices" => "SELECT i.Id AS 'الرقم', CASE i.Kind WHEN 'SALE' THEN 'مبيعات' ELSE 'مشتريات' END AS 'النوع', i.Number AS 'رقم الفاتورة', i.InvoiceDate AS 'التاريخ', p.Name AS 'العميل / المورد', i.SubtotalHalalas / 100.0 AS 'قبل الضريبة', i.VatHalalas / 100.0 AS 'الضريبة', i.TotalHalalas / 100.0 AS 'الإجمالي' FROM Invoices i JOIN Parties p ON p.Id=i.PartyId ORDER BY i.InvoiceDate DESC, i.Id DESC",
            "payments" => "SELECT pp.Id AS 'الرقم', CASE pp.Kind WHEN 'RECEIPT' THEN 'قبض' ELSE 'صرف' END AS 'النوع', pp.Number AS 'رقم السند', pp.PaymentDate AS 'التاريخ', p.Name AS 'الطرف', pp.AmountHalalas/100.0 AS 'المبلغ', pp.Method AS 'الطريقة', pp.Memo AS 'البيان' FROM PartyPayments pp JOIN Parties p ON p.Id=pp.PartyId ORDER BY pp.PaymentDate DESC,pp.Id DESC",
            "audit" => "SELECT Id AS 'الرقم', EventAt AS 'الوقت', Actor AS 'المستخدم', Action AS 'العملية', Entity AS 'الكيان', EntityId AS 'معرف السجل', Details AS 'التفاصيل' FROM AuditLog ORDER BY Id DESC",
            _ => null
        };
        _title.Text = view switch
        {
            "dashboard" => "الرئيسية", "accounts" => "دليل الحسابات", "parties" => "العملاء والموردون",
            "products" => "الأصناف والمخزون", "journals" => "القيود اليومية", "invoices" => "فواتير البيع والشراء",
            "audit" => "سجل التدقيق", "payments" => "سندات القبض والصرف", "reports" => "التقارير المالية", _ => "النظام"
        };
        if (view == "reports")
        {
            using var form = new ReportsForm();
            form.ShowDialog(this);
            Navigate("dashboard");
            return;
        }
        if (view == "dashboard")
        {
            var table = new DataTable();
            table.Columns.Add("المؤشر");
            table.Columns.Add("القيمة");
            table.Rows.Add("الحسابات المسجلة", CountRows("Accounts"));
            table.Rows.Add("العملاء والموردون", CountRows("Parties"));
            table.Rows.Add("الأصناف", CountRows("Products"));
            table.Rows.Add("القيود المرحلة", CountRows("Journals"));
            table.Rows.Add("الفواتير المرحلة", CountRows("Invoices"));
            table.Rows.Add("سندات القبض والصرف", CountRows("PartyPayments"));
            table.Rows.Add("إجمالي المبيعات (ر.س)", ScalarDecimal("SELECT COALESCE(SUM(TotalHalalas),0)/100.0 FROM Invoices WHERE Kind='SALE'"));
            table.Rows.Add("إجمالي المشتريات (ر.س)", ScalarDecimal("SELECT COALESCE(SUM(TotalHalalas),0)/100.0 FROM Invoices WHERE Kind='PURCHASE'"));
            _grid.DataSource = table;
            _status.Text = "قاعدة بيانات محلية مستمرة: " + Database.DatabasePath + " | عمليات الفواتير والقيود تُرحّل بقيد محاسبي متوازن.";
            return;
        }
        if (sql is not null) LoadTable(sql);
    }

    private void AddRecord()
    {
        if (RecordEditorForm.ShowEditor(this, _view)) Navigate(_view);
    }

    private void EditRecord()
    {
        if (_grid.CurrentRow is null) { MessageBox.Show(this, "حدد سجلًا أولًا."); return; }
        if (RecordEditorForm.ShowEditor(this, _view, _grid.CurrentRow)) Navigate(_view);
    }

    private long CountRows(string table)
    {
        if (!new[] { "Accounts", "Parties", "Products", "Journals", "Invoices", "PartyPayments" }.Contains(table)) throw new ArgumentOutOfRangeException(nameof(table));
        using var db = Database.Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM " + table;
        return (long)(cmd.ExecuteScalar() ?? 0L);
    }

    private decimal ScalarDecimal(string sql)
    {
        using var db = Database.Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = sql;
        return Convert.ToDecimal(cmd.ExecuteScalar());
    }

    private void LoadTable(string sql)
    {
        using var db = Database.Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = sql;
        using var reader = cmd.ExecuteReader();
        var table = new DataTable();
        table.Load(reader);
        _grid.DataSource = table;
        _status.Text = $"عدد السجلات: {table.Rows.Count} | قاعدة بيانات محلية مستمرة";
    }

    private void ApplySearch()
    {
        if (_grid.DataSource is not DataTable table) return;
        var text = _search.Text.Trim().Replace("'", "''");
        if (text.Length == 0) { table.DefaultView.RowFilter = string.Empty; return; }
        var filters = table.Columns.Cast<DataColumn>().Where(c => c.DataType == typeof(string))
            .Select(c => "[" + c.ColumnName.Replace("]", "]]") + "] LIKE '%" + text + "%'").ToArray();
        table.DefaultView.RowFilter = filters.Length == 0 ? string.Empty : string.Join(" OR ", filters);
    }
}
