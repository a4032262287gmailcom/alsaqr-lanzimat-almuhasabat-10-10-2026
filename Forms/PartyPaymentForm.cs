using System.Data;
using AlSaqr.Accounting.Data;
using AlSaqr.Accounting.Services;

namespace AlSaqr.Accounting.Forms;

internal sealed class PartyPaymentForm : Form
{
    private readonly ComboBox _kind = new();
    private readonly TextBox _number = new();
    private readonly DateTimePicker _date = new() { Format = DateTimePickerFormat.Short, Value = DateTime.Today };
    private readonly ComboBox _party = new();
    private readonly NumericUpDown _amount = new() { DecimalPlaces = 2, Maximum = 999999999, Minimum = 0.01m, ThousandsSeparator = true, Width = 220 };
    private readonly ComboBox _method = new();
    private readonly TextBox _memo = new();

    public PartyPaymentForm()
    {
        Text = "سند قبض / سند صرف";
        Width = 560;
        Height = 430;
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        RightToLeft = RightToLeft.Yes;
        RightToLeftLayout = true;
        Font = new Font("Segoe UI", 10F);
        BuildLayout();
        LoadParties();
    }

    private void BuildLayout()
    {
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(18), ColumnCount = 2, RowCount = 7 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 34));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 66));
        Controls.Add(layout);
        AddField(layout, "نوع السند", _kind);
        AddField(layout, "رقم السند", _number);
        AddField(layout, "التاريخ", _date);
        AddField(layout, "العميل / المورد", _party);
        AddField(layout, "المبلغ بالريال", _amount);
        AddField(layout, "طريقة الدفع", _method);
        AddField(layout, "البيان", _memo);
        _kind.DropDownStyle = ComboBoxStyle.DropDownList;
        _kind.Items.Add(new KeyValuePair<string, string>("RECEIPT", "سند قبض من عميل"));
        _kind.Items.Add(new KeyValuePair<string, string>("PAYMENT", "سند صرف لمورد"));
        _kind.DisplayMember = "Value";
        _kind.ValueMember = "Key";
        _kind.SelectedIndex = 0;
        _kind.SelectedIndexChanged += (_, _) => LoadParties();
        _number.Text = "PV-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
        _method.DropDownStyle = ComboBoxStyle.DropDownList;
        _method.Items.AddRange(new object[] { "نقدًا", "تحويل بنكي", "شبكة", "شيك" });
        _method.SelectedIndex = 0;

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 55, Padding = new Padding(12), FlowDirection = FlowDirection.LeftToRight };
        var save = new Button { Text = "حفظ وترحيل", Width = 125, Height = 32 };
        save.Click += (_, _) => Save();
        var cancel = new Button { Text = "إلغاء", Width = 90, Height = 32, DialogResult = DialogResult.Cancel };
        buttons.Controls.Add(save);
        buttons.Controls.Add(cancel);
        Controls.Add(buttons);
    }

    private static void AddField(TableLayoutPanel layout, string label, Control input)
    {
        int row = layout.Controls.Count / 2;
        layout.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Right, Margin = new Padding(4, 9, 4, 5) }, 0, row);
        input.Dock = DockStyle.Fill;
        input.Margin = new Padding(4, 4, 4, 4);
        layout.Controls.Add(input, 1, row);
    }

    private void LoadParties()
    {
        if (_kind.SelectedIndex < 0) return;
        using var db = Database.Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = _kind.SelectedIndex == 1
            ? "SELECT Id,Name FROM Parties WHERE IsActive=1 AND Kind IN ('Supplier','Both') ORDER BY Name"
            : "SELECT Id,Name FROM Parties WHERE IsActive=1 AND Kind IN ('Customer','Both') ORDER BY Name";
        using var reader = cmd.ExecuteReader();
        var table = new DataTable();
        table.Load(reader);
        _party.DataSource = table;
        _party.DisplayMember = "Name";
        _party.ValueMember = "Id";
        _party.DropDownStyle = ComboBoxStyle.DropDownList;
    }

    private void Save()
    {
        try
        {
            if (_party.SelectedValue is null) throw new InvalidOperationException("لا توجد أطراف مطابقة. أضف العميل أو المورد أولًا.");
            var kind = Convert.ToString(_kind.SelectedValue) ?? "RECEIPT";
            var id = AccountingService.PostPartyPayment(kind, _number.Text.Trim(), _date.Value.Date,
                Convert.ToInt32(_party.SelectedValue), _amount.Value,
                Convert.ToString(_method.SelectedItem) ?? "نقدًا", _memo.Text.Trim());
            MessageBox.Show(this, $"تم ترحيل السند وربطه بالقيد المحاسبي. رقم السجل: {id}", "تم", MessageBoxButtons.OK, MessageBoxIcon.Information);
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "تعذر ترحيل السند", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }
}
