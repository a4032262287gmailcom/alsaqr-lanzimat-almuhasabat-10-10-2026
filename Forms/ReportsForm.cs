using System.Data;
using System.Text;
using AlSaqr.Accounting.Data;

namespace AlSaqr.Accounting.Forms;

internal sealed class ReportsForm : Form
{
    private readonly ComboBox _report = new();
    private readonly DateTimePicker _from = new() { Format = DateTimePickerFormat.Short };
    private readonly DateTimePicker _to = new() { Format = DateTimePickerFormat.Short };
    private readonly DataGridView _grid = new();
    private readonly Label _totals = new();

    public ReportsForm()
    {
        Text = "التقارير المالية";
        Width = 1100;
        Height = 700;
        StartPosition = FormStartPosition.CenterParent;
        RightToLeft = RightToLeft.Yes;
        RightToLeftLayout = true;
        Font = new Font("Segoe UI", 10F);
        BuildLayout();
        LoadReport();
    }

    private void BuildLayout()
    {
        var bar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 55, Padding = new Padding(10), FlowDirection = FlowDirection.RightToLeft, WrapContents = false };
        _report.DropDownStyle = ComboBoxStyle.DropDownList;
        _report.Width = 230;
        _report.Items.AddRange(new object[] { "ميزان المراجعة", "قائمة الدخل", "المركز المالي", "تقرير ضريبة القيمة المضافة", "أرصدة العملاء والموردين", "حركة المخزون" });
        _report.SelectedIndex = 0;
        _from.Value = new DateTime(DateTime.Today.Year, 1, 1);
        _to.Value = DateTime.Today;
        var run = new Button { Text = "عرض التقرير", Width = 110, Height = 30 };
        run.Click += (_, _) => LoadReport();
        var export = new Button { Text = "تصدير CSV", Width = 100, Height = 30 };
        export.Click += (_, _) => ExportCsv();
        bar.Controls.Add(_report);
        bar.Controls.Add(new Label { Text = "من", AutoSize = true, Padding = new Padding(4, 7, 4, 0) });
        bar.Controls.Add(_from);
        bar.Controls.Add(new Label { Text = "إلى", AutoSize = true, Padding = new Padding(4, 7, 4, 0) });
        bar.Controls.Add(_to);
        bar.Controls.Add(run);
        bar.Controls.Add(export);

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
        _totals.Dock = DockStyle.Bottom;
        _totals.Height = 35;
        _totals.TextAlign = ContentAlignment.MiddleRight;
        _totals.Font = new Font("Segoe UI", 11F, FontStyle.Bold);
        Controls.Add(_grid);
        Controls.Add(_totals);
        Controls.Add(bar);
    }

    private void LoadReport()
    {
        if (_to.Value.Date < _from.Value.Date)
        {
            MessageBox.Show(this, "تاريخ النهاية يجب أن يكون بعد تاريخ البداية.", "تنبيه", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var start = _from.Value.ToString("yyyy-MM-dd");
        var end = _to.Value.ToString("yyyy-MM-dd");
        string sql = _report.SelectedIndex switch
        {
            0 => @"SELECT a.Code AS 'رمز الحساب',a.Name AS 'اسم الحساب',
COALESCE(SUM(CASE WHEN j.EntryDate BETWEEN $from AND $to THEN l.DebitHalalas ELSE 0 END),0)/100.0 AS 'إجمالي المدين',
COALESCE(SUM(CASE WHEN j.EntryDate BETWEEN $from AND $to THEN l.CreditHalalas ELSE 0 END),0)/100.0 AS 'إجمالي الدائن',
(COALESCE(SUM(CASE WHEN j.EntryDate BETWEEN $from AND $to THEN l.DebitHalalas ELSE 0 END),0)-COALESCE(SUM(CASE WHEN j.EntryDate BETWEEN $from AND $to THEN l.CreditHalalas ELSE 0 END),0))/100.0 AS 'الرصيد'
FROM Accounts a LEFT JOIN JournalLines l ON l.AccountCode=a.Code
LEFT JOIN Journals j ON j.Id=l.JournalId AND j.Status='POSTED'
GROUP BY a.Code,a.Name ORDER BY a.Code",
            1 => @"SELECT a.Code AS 'رمز الحساب',a.Name AS 'البند',
CASE WHEN a.Type='Revenue' THEN 'إيراد' ELSE 'مصروف' END AS 'التصنيف',
CASE WHEN a.Type='Revenue' THEN SUM(l.CreditHalalas-l.DebitHalalas)/100.0 ELSE SUM(l.DebitHalalas-l.CreditHalalas)/100.0 END AS 'صافي الحساب'
FROM Accounts a JOIN JournalLines l ON l.AccountCode=a.Code JOIN Journals j ON j.Id=l.JournalId
WHERE j.Status='POSTED' AND j.EntryDate BETWEEN $from AND $to AND a.Type IN ('Revenue','Expense')
GROUP BY a.Code,a.Name,a.Type ORDER BY a.Type,a.Code",
            2 => @"SELECT a.Code AS 'رمز الحساب',a.Name AS 'البند',a.Type AS 'التصنيف',
CASE WHEN a.Type='Asset' THEN SUM(l.DebitHalalas-l.CreditHalalas)/100.0 ELSE SUM(l.CreditHalalas-l.DebitHalalas)/100.0 END AS 'الرصيد المحاسبي'
FROM Accounts a JOIN JournalLines l ON l.AccountCode=a.Code JOIN Journals j ON j.Id=l.JournalId
WHERE j.Status='POSTED' AND j.EntryDate <= $to AND a.Type IN ('Asset','Liability','Equity')
GROUP BY a.Code,a.Name,a.Type ORDER BY a.Type,a.Code",
            3 => @"SELECT Kind AS 'نوع الفاتورة',Number AS 'رقم الفاتورة',InvoiceDate AS 'التاريخ',
SubtotalHalalas/100.0 AS 'قبل الضريبة',VatHalalas/100.0 AS 'الضريبة',TotalHalalas/100.0 AS 'الإجمالي'
FROM Invoices WHERE Status='POSTED' AND InvoiceDate BETWEEN $from AND $to ORDER BY InvoiceDate,Number",
            4 => @"SELECT p.Id AS 'رقم الطرف',p.Name AS 'العميل أو المورد',
CASE i.Kind WHEN 'SALE' THEN 'مبيعات' ELSE 'مشتريات' END AS 'نوع الحركة',
SUM(i.TotalHalalas)/100.0 AS 'إجمالي الفواتير',
SUM(CASE WHEN i.IsCredit=1 THEN i.TotalHalalas ELSE 0 END)/100.0 AS 'على الحساب'
FROM Parties p JOIN Invoices i ON i.PartyId=p.Id
WHERE i.Status='POSTED' AND i.InvoiceDate BETWEEN $from AND $to
GROUP BY p.Id,p.Name,i.Kind ORDER BY p.Name",
            _ => @"SELECT p.Code AS 'رمز الصنف',p.Name AS 'الصنف',p.Unit AS 'الوحدة',
COALESCE(SUM(CASE WHEN mv.Kind='PURCHASE' THEN mv.Quantity ELSE 0 END),0) AS 'كمية واردة',
COALESCE(SUM(CASE WHEN mv.Kind='SALE' THEN mv.Quantity ELSE 0 END),0) AS 'كمية منصرفة',
p.Quantity AS 'الرصيد الحالي',p.CostHalalas/100.0 AS 'تكلفة الوحدة',p.Quantity*p.CostHalalas/100.0 AS 'قيمة المخزون'
FROM Products p LEFT JOIN
(SELECT il.ProductId,i.Kind,il.Quantity FROM InvoiceLines il JOIN Invoices i ON i.Id=il.InvoiceId
 WHERE i.Status='POSTED' AND i.InvoiceDate BETWEEN $from AND $to) mv ON mv.ProductId=p.Id
GROUP BY p.Id,p.Code,p.Name,p.Unit,p.Quantity,p.CostHalalas ORDER BY p.Name"
        };

        using var db = Database.Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("$from", start);
        cmd.Parameters.AddWithValue("$to", end);
        using var reader = cmd.ExecuteReader();
        var table = new DataTable();
        table.Load(reader);
        _grid.DataSource = table;
        _totals.Text = $"التقرير: {_report.SelectedItem}    |    عدد الصفوف: {table.Rows.Count}";
    }

    private void ExportCsv()
    {
        if (_grid.DataSource is not DataTable table) return;
        using var dialog = new SaveFileDialog { Filter = "CSV UTF-8 (*.csv)|*.csv", FileName = "تقرير_الصقر.csv" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        var sb = new StringBuilder();
        sb.AppendLine(string.Join(",", table.Columns.Cast<DataColumn>().Select(c => Quote(c.ColumnName))));
        foreach (DataRow row in table.Rows)
            sb.AppendLine(string.Join(",", row.ItemArray.Select(v => Quote(Convert.ToString(v) ?? string.Empty))));
        File.WriteAllText(dialog.FileName, "\uFEFF" + sb, new UTF8Encoding(true));
        MessageBox.Show(this, "تم تصدير التقرير.", "اكتمل", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private static string Quote(string value) => "\"" + value.Replace("\"", "\"\"") + "\"";
}
