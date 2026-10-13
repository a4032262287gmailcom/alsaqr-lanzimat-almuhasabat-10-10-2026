using System.Data;
using AlSaqr.Accounting.Data;

namespace AlSaqr.Accounting.Forms;

public sealed class MainForm : Form
{
    private readonly DataGridView _grid = new();
    private readonly Label _title = new();
    private readonly Label _status = new();
    private readonly TextBox _search = new();
    private string _view = "dashboard";

    public MainForm()
    {
        Text = "الصقر لأنظمة المحاسبة";
        StartPosition = FormStartPosition.CenterScreen;
        WindowState = FormWindowState.Maximized;
        MinimumSize = new Size(1000, 620);
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
        header.Controls.Add(new Label { Text = "إدارة الحسابات والعمليات المالية", Dock = DockStyle.Fill, ForeColor = Color.WhiteSmoke, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(18, 0, 0, 0) });

        var nav = new FlowLayoutPanel { Dock = DockStyle.Right, Width = 210, FlowDirection = FlowDirection.TopDown, WrapContents = false, BackColor = Color.FromArgb(33, 52, 79), Padding = new Padding(8) };
        AddNav(nav, "الرئيسية", "dashboard");
        AddNav(nav, "دليل الحسابات", "accounts");
        AddNav(nav, "العملاء والموردون", "parties");
        AddNav(nav, "الأصناف والمخزون", "products");
        AddNav(nav, "القيود اليومية", "journals");
        AddNav(nav, "فواتير البيع والشراء", "invoices");
        AddNav(nav, "سجل التدقيق", "audit");

        var content = new Panel { Dock = DockStyle.Fill, Padding = new Padding(20), BackColor = Color.FromArgb(245, 247, 250) };
        _title.Dock = DockStyle.Top;
        _title.Height = 48;
        _title.Font = new Font("Segoe UI", 16F, FontStyle.Bold);
        _title.ForeColor = Color.FromArgb(28, 43, 64);
        var tools = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 42, FlowDirection = FlowDirection.RightToLeft, WrapContents = false };
        _search.Width = 260;
        _search.PlaceholderText = "بحث في البيانات...";
        _search.TextChanged += (_, _) => ApplySearch();
        var refresh = new Button { Text = "تحديث", Width = 90, Height = 30 };
        refresh.Click += (_, _) => Navigate(_view);
        tools.Controls.Add(refresh);
        tools.Controls.Add(_search);

        _grid.Dock = DockStyle.Fill;
        _grid.ReadOnly = true;
        _grid.AllowUserToAddRows = false;
        _grid.AllowUserToDeleteRows = false;
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
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
        var button = new Button { Text = text, Tag = view, Width = 190, Height = 46, FlatStyle = FlatStyle.Flat, ForeColor = Color.White, BackColor = Color.FromArgb(33, 52, 79), TextAlign = ContentAlignment.MiddleRight, Padding = new Padding(0, 0, 12, 0), Cursor = Cursors.Hand };
        button.FlatAppearance.BorderSize = 0;
        button.Click += (sender, _) => ((MainForm)((Control)sender!).FindForm()!).Navigate(view);
        parent.Controls.Add(button);
    }

    private void Navigate(string view)
    {
        _view = view;
        _search.Clear();
        string? sql = view switch
        {
            "accounts" => "SELECT Code AS 'رمز الحساب', Name AS 'اسم الحساب', Type AS 'نوع الحساب', IsActive AS 'نشط' FROM Accounts ORDER BY Code",
            "parties" => "SELECT Id AS 'الرقم', Name AS 'الاسم', Kind AS 'التصنيف', Phone AS 'الهاتف', VatNumber AS 'الرقم الضريبي', IsActive AS 'نشط' FROM Parties ORDER BY Name",
            "products" => "SELECT Id AS 'الرقم', Code AS 'رمز الصنف', Name AS 'اسم الصنف', Unit AS 'الوحدة', Quantity AS 'الكمية', CostHalalas / 100.0 AS 'التكلفة بالريال', PriceHalalas / 100.0 AS 'السعر بالريال', VatRate AS 'الضريبة %' FROM Products ORDER BY Name",
            "journals" => "SELECT Id AS 'الرقم', Number AS 'رقم القيد', EntryDate AS 'التاريخ', Memo AS 'البيان', Source AS 'المصدر', Status AS 'الحالة' FROM Journals ORDER BY EntryDate DESC, Id DESC",
            "invoices" => "SELECT Id AS 'الرقم', Kind AS 'النوع', Number AS 'رقم الفاتورة', InvoiceDate AS 'التاريخ', SubtotalHalalas / 100.0 AS 'قبل الضريبة', VatHalalas / 100.0 AS 'الضريبة', TotalHalalas / 100.0 AS 'الإجمالي' FROM Invoices ORDER BY InvoiceDate DESC, Id DESC",
            "audit" => "SELECT Id AS 'الرقم', EventAt AS 'الوقت', Actor AS 'المستخدم', Action AS 'العملية', Entity AS 'الكيان', EntityId AS 'معرف السجل', Details AS 'التفاصيل' FROM AuditLog ORDER BY Id DESC",
            _ => null
        };
        if (view == "dashboard")
        {
            _title.Text = "الرئيسية";
            var table = new DataTable();
            table.Columns.Add("المؤشر");
            table.Columns.Add("القيمة");
            table.Rows.Add("الحسابات المسجلة", CountRows("Accounts"));
            table.Rows.Add("العملاء والموردون", CountRows("Parties"));
            table.Rows.Add("الأصناف", CountRows("Products"));
            table.Rows.Add("القيود المرحلة", CountRows("Journals"));
            table.Rows.Add("الفواتير المرحلة", CountRows("Invoices"));
            _grid.DataSource = table;
            _status.Text = "قاعدة البيانات المحلية: " + Database.DatabasePath + " | لا تظهر نافذة ترخيص عند بدء التشغيل.";
            return;
        }
        _title.Text = view switch { "accounts" => "دليل الحسابات", "parties" => "العملاء والموردون", "products" => "الأصناف والمخزون", "journals" => "القيود اليومية", "invoices" => "فواتير البيع والشراء", "audit" => "سجل التدقيق", _ => "النظام" };
        if (sql is not null) LoadTable(sql);
    }

    private long CountRows(string table)
    {
        if (!new[] { "Accounts", "Parties", "Products", "Journals", "Invoices" }.Contains(table)) throw new ArgumentOutOfRangeException(nameof(table));
        using var db = Database.Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM " + table;
        return (long)(cmd.ExecuteScalar() ?? 0L);
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
        _status.Text = "عدد السجلات: " + table.Rows.Count + " | قاعدة بيانات محلية مستمرة";
    }

    private void ApplySearch()
    {
        if (_grid.DataSource is not DataTable table) return;
        var text = _search.Text.Trim().Replace("'", "''");
        if (text.Length == 0) { table.DefaultView.RowFilter = string.Empty; return; }
        var filters = table.Columns.Cast<DataColumn>().Where(c => c.DataType == typeof(string)).Select(c => "[" + c.ColumnName.Replace("]", "]]" ) + "] LIKE '%" + text + "%'").ToArray();
        table.DefaultView.RowFilter = filters.Length == 0 ? string.Empty : string.Join(" OR ", filters);
    }
}
