using System.Data;
using System.Drawing;
using System.Text;
using AlSaqr.Accounting.Services;

namespace AlSaqr.Accounting.Forms;

internal sealed class LocalSqlServerConnectionForm : Form
{
    private readonly TextBox _server = new() { Width = 330 };
    private readonly TextBox _database = new() { Width = 330 };
    private readonly DataGridView _tables = new();
    private readonly Label _status = new() { Dock = DockStyle.Bottom, Height = 42, TextAlign = ContentAlignment.MiddleRight, Padding = new Padding(8) };

    public LocalSqlServerConnectionForm()
    {
        Text = "الصقر لأنظمة المحاسبة | اتصال قاعدة البيانات";
        Width = 900;
        Height = 680;
        MinimumSize = new Size(760, 560);
        StartPosition = FormStartPosition.CenterParent;
        RightToLeft = RightToLeft.Yes;
        RightToLeftLayout = true;
        Font = new Font("Segoe UI", 10F);
        BackColor = Color.FromArgb(244, 246, 249);

        var settings = LocalSqlServerConnection.Load();
        _server.Text = settings.Server;
        _database.Text = settings.Database;

        var header = new Label
        {
            Text = "إعداد الاتصال المحلي بقاعدة نظام الصقر الحالية",
            Dock = DockStyle.Top, Height = 54, Padding = new Padding(12),
            Font = new Font("Segoe UI", 15F, FontStyle.Bold),
            ForeColor = Color.FromArgb(18, 32, 52), TextAlign = ContentAlignment.MiddleRight
        };
        var hint = new Label
        {
            Text = "الإعداد المبدئي مستخلص من ملفات المستودع. الاتصال يستخدم مصادقة Windows، ولا تُحفظ كلمات مرور في ملفات البرنامج.",
            Dock = DockStyle.Top, Height = 48, Padding = new Padding(12, 4, 12, 4),
            ForeColor = Color.DimGray, TextAlign = ContentAlignment.MiddleRight
        };
        var fields = new FlowLayoutPanel
        {
            Dock = DockStyle.Top, Height = 105, FlowDirection = FlowDirection.RightToLeft,
            WrapContents = true, Padding = new Padding(12), BackColor = Color.White
        };
        fields.Controls.Add(Field("الخادم / المثيل", _server));
        fields.Controls.Add(Field("قاعدة البيانات", _database));

        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Top, Height = 52, FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false, Padding = new Padding(8)
        };
        var test = MakeButton("اختبار الاتصال وعرض الجداول");
        test.Click += (_, _) => RunTest(false);
        var save = MakeButton("اختبار الاتصال وحفظ الإعداد");
        save.Click += (_, _) => RunTest(true);
        var export = MakeButton("تصدير مخطط الجداول");
        export.Click += (_, _) => ExportSchema();
        var close = MakeButton("إغلاق");
        close.Click += (_, _) => Close();
        actions.Controls.Add(test); actions.Controls.Add(save); actions.Controls.Add(export); actions.Controls.Add(close);

        _tables.Dock = DockStyle.Fill;
        _tables.ReadOnly = true;
        _tables.AllowUserToAddRows = false;
        _tables.AllowUserToDeleteRows = false;
        _tables.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _tables.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _tables.RowHeadersVisible = false;
        _tables.BackgroundColor = Color.White;
        _tables.RightToLeft = RightToLeft.Yes;
        _tables.EnableHeadersVisualStyles = false;
        _tables.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(18, 32, 52);
        _tables.ColumnHeadersDefaultCellStyle.ForeColor = Color.White;

        Controls.Add(_tables);
        Controls.Add(actions);
        Controls.Add(fields);
        Controls.Add(hint);
        Controls.Add(header);
        Controls.Add(_status);
        _status.Text = "لم يتم اختبار الاتصال في هذه الجلسة.";
    }

    private Control Field(string labelText, Control input)
    {
        var panel = new Panel { Width = 375, Height = 70, Margin = new Padding(5) };
        panel.Controls.Add(new Label { Text = labelText, Dock = DockStyle.Top, Height = 25, TextAlign = ContentAlignment.MiddleRight });
        input.Dock = DockStyle.Top;
        panel.Controls.Add(input);
        return panel;
    }

    private static Button MakeButton(string text)
    {
        return new Button
        {
            Text = text, Width = Math.Max(130, TextRenderer.MeasureText(text, SystemFonts.DefaultFont).Width + 32),
            Height = 35, BackColor = Color.FromArgb(18, 32, 52), ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat, Margin = new Padding(4), Cursor = Cursors.Hand
        };
    }

    private void ExportSchema()
    {
        if (_tables.DataSource is not DataTable table || table.Rows.Count == 0)
        {
            MessageBox.Show(this, "اختبر الاتصال واعرض مخطط الجداول أولًا.", "الصقر لأنظمة المحاسبة", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        using var dialog = new SaveFileDialog
        {
            Filter = "ملف CSV بترميز UTF-8 (*.csv)|*.csv",
            FileName = "AlSaqr-SqlServer-Schema-" + DateTime.Now.ToString("yyyyMMdd-HHmm") + ".csv",
            AddExtension = true,
            DefaultExt = "csv"
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        using var writer = new StreamWriter(dialog.FileName, false, new UTF8Encoding(true));
        writer.WriteLine(string.Join(",", table.Columns.Cast<DataColumn>().Select(x => Csv(x.ColumnName))));
        foreach (DataRow row in table.Rows)
            writer.WriteLine(string.Join(",", row.ItemArray.Select(x => Csv(Convert.ToString(x, CultureInfo.InvariantCulture) ?? ""))));
        _status.Text = "تم تصدير مخطط قاعدة البيانات (أسماء الجداول والأعمدة فقط) إلى: " + dialog.FileName;
    }

    private static string Csv(string value) => "\"" + value.Replace("\"", "\"\"").Replace("\\r", " ").Replace("\\n", " ") + "\"";

    private void RunTest(bool save)
    {
        try
        {
            var settings = new LocalSqlServerSettings(_server.Text.Trim(), _database.Text.Trim());
            var report = LocalSqlServerConnection.Test(settings);
            _tables.DataSource = report.Tables;
            var tableCount = report.Tables.DefaultView.ToTable(true, "المخطط", "الجدول").Rows.Count;
            var status = $"اتصال ناجح — الخادم: {report.Server} — قاعدة البيانات: {report.Database} — إصدار SQL Server: {report.Version} — المستخدم: {report.Login} — عدد الجداول: {tableCount}";
            if (save)
            {
                LocalSqlServerConnection.Save(settings);
                status = "تم حفظ الإعداد محليًا. " + status;
            }
            _status.Text = status;
        }
        catch (Exception ex)
        {
            _status.Text = "فشل الاتصال: " + ex.Message;
            MessageBox.Show(this, ex.Message, "الصقر لأنظمة المحاسبة | تعذر الاتصال", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }
}
