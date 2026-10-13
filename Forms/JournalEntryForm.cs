using System.Data;
using AlSaqr.Accounting.Data;

namespace AlSaqr.Accounting.Forms;

internal sealed class JournalEntryForm : Form
{
    private readonly TextBox _number = new();
    private readonly DateTimePicker _date = new() { Format = DateTimePickerFormat.Short, Value = DateTime.Today };
    private readonly TextBox _memo = new();
    private readonly DataGridView _grid = new();

    public JournalEntryForm()
    {
        Text = "قيد يومي جديد";
        Width = 1000;
        Height = 620;
        StartPosition = FormStartPosition.CenterParent;
        RightToLeft = RightToLeft.Yes;
        RightToLeftLayout = true;
        Font = new Font("Segoe UI", 10F);
        BuildLayout();
        LoadAccounts();
    }

    private void BuildLayout()
    {
        var header = new TableLayoutPanel { Dock = DockStyle.Top, Height = 92, Padding = new Padding(12), ColumnCount = 6 };
        for (int i = 0; i < 6; i++) header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 16.66F));
        AddField(header, "رقم القيد", _number, 0);
        AddField(header, "التاريخ", _date, 2);
        AddField(header, "البيان", _memo, 4);
        _number.Text = "JE-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");

        _grid.Dock = DockStyle.Fill;
        _grid.AllowUserToAddRows = true;
        _grid.AllowUserToDeleteRows = true;
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _grid.RowHeadersVisible = false;
        _grid.Columns.Add(new DataGridViewComboBoxColumn { Name = "Account", HeaderText = "الحساب", DisplayMember = "Name", ValueMember = "Code", FlatStyle = FlatStyle.Flat });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "LineMemo", HeaderText = "البيان" });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Debit", HeaderText = "مدين", ValueType = typeof(decimal) });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Credit", HeaderText = "دائن", ValueType = typeof(decimal) });

        var footer = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 55, Padding = new Padding(10), FlowDirection = FlowDirection.LeftToRight };
        var save = new Button { Text = "حفظ وترحيل القيد", Width = 150, Height = 32 };
        save.Click += (_, _) => SaveJournal();
        var cancel = new Button { Text = "إلغاء", Width = 90, Height = 32, DialogResult = DialogResult.Cancel };
        footer.Controls.Add(save);
        footer.Controls.Add(cancel);
        Controls.Add(_grid);
        Controls.Add(footer);
        Controls.Add(header);
    }

    private static void AddField(TableLayoutPanel layout, string label, Control input, int col)
    {
        var panel = new Panel { Dock = DockStyle.Fill };
        panel.Controls.Add(new Label { Text = label, Dock = DockStyle.Top, Height = 24 });
        input.Dock = DockStyle.Bottom;
        panel.Controls.Add(input);
        layout.Controls.Add(panel, col, 0);
        layout.SetColumnSpan(panel, 2);
    }

    private void LoadAccounts()
    {
        using var db = Database.Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = "SELECT Code,Name FROM Accounts WHERE IsActive=1 ORDER BY Code";
        using var reader = cmd.ExecuteReader();
        var table = new DataTable();
        table.Load(reader);
        ((DataGridViewComboBoxColumn)_grid.Columns["Account"]!).DataSource = table;
        _grid.Rows.Add(2);
    }

    private void SaveJournal()
    {
        try
        {
            var lines = new List<(string Account, string Memo, long Debit, long Credit)>();
            foreach (DataGridViewRow row in _grid.Rows)
            {
                if (row.IsNewRow || row.Cells["Account"].Value is null) continue;
                decimal debit = Convert.ToDecimal(row.Cells["Debit"].Value ?? 0);
                decimal credit = Convert.ToDecimal(row.Cells["Credit"].Value ?? 0);
                if (debit < 0 || credit < 0 || (debit > 0 && credit > 0) || (debit == 0 && credit == 0))
                    throw new InvalidOperationException("كل سطر يجب أن يحتوي مبلغًا مدينًا أو دائنًا فقط.");
                lines.Add((Convert.ToString(row.Cells["Account"].Value)!, Convert.ToString(row.Cells["LineMemo"].Value) ?? "", ToHalalas(debit), ToHalalas(credit)));
            }
            if (lines.Count < 2) throw new InvalidOperationException("أدخل سطرين محاسبيين على الأقل.");
            if (lines.Sum(x => x.Debit) != lines.Sum(x => x.Credit)) throw new InvalidOperationException("القيد غير متوازن. يجب أن يساوي إجمالي المدين إجمالي الدائن.");
            if (string.IsNullOrWhiteSpace(_number.Text) || string.IsNullOrWhiteSpace(_memo.Text)) throw new InvalidOperationException("رقم القيد والبيان مطلوبان.");

            using var db = Database.Open();
            using var tx = db.BeginTransaction();
            using (var cmd = db.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = "INSERT INTO Journals(Number,EntryDate,Memo,Source,Status,CreatedAt) VALUES($number,$date,$memo,'Manual','POSTED',$at)";
                cmd.Parameters.AddWithValue("$number", _number.Text.Trim());
                cmd.Parameters.AddWithValue("$date", _date.Value.ToString("yyyy-MM-dd"));
                cmd.Parameters.AddWithValue("$memo", _memo.Text.Trim());
                cmd.Parameters.AddWithValue("$at", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                cmd.ExecuteNonQuery();
            }
            using var idCommand = db.CreateCommand();
            idCommand.Transaction = tx;
            idCommand.CommandText = "SELECT last_insert_rowid()";
            long journalId = Convert.ToInt64(idCommand.ExecuteScalar());
            foreach (var line in lines)
            {
                using var cmd = db.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = "INSERT INTO JournalLines(JournalId,AccountCode,Memo,DebitHalalas,CreditHalalas) VALUES($id,$account,$memo,$debit,$credit)";
                cmd.Parameters.AddWithValue("$id", journalId);
                cmd.Parameters.AddWithValue("$account", line.Account);
                cmd.Parameters.AddWithValue("$memo", line.Memo);
                cmd.Parameters.AddWithValue("$debit", line.Debit);
                cmd.Parameters.AddWithValue("$credit", line.Credit);
                cmd.ExecuteNonQuery();
            }
            using (var audit = db.CreateCommand())
            {
                audit.Transaction = tx;
                audit.CommandText = "INSERT INTO AuditLog(EventAt,Actor,Action,Entity,EntityId,Details) VALUES($at,'المستخدم المحلي','ترحيل','قيد يومي',$id,$details)";
                audit.Parameters.AddWithValue("$at", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                audit.Parameters.AddWithValue("$id", journalId.ToString());
                audit.Parameters.AddWithValue("$details", _memo.Text.Trim());
                audit.ExecuteNonQuery();
            }
            tx.Commit();
            MessageBox.Show(this, "تم حفظ القيد وترحيله بعد التحقق من توازن المدين والدائن.", "تم", MessageBoxButtons.OK, MessageBoxIcon.Information);
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "تعذر ترحيل القيد", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private static long ToHalalas(decimal amount) => checked((long)Math.Round(amount * 100m, MidpointRounding.AwayFromZero));
}
