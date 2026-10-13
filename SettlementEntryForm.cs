using System.Data;
using AlSaqr.Accounting.Services;

namespace AlSaqr.Accounting;

internal sealed class SettlementEntryForm : Form
{
    private readonly LicenseService _licensing = new();
    private readonly AccountingService _service;
    private readonly string _kind;
    private readonly ComboBox _party = new() { Width = 400, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox _cash = new() { Width = 400, DropDownStyle = ComboBoxStyle.DropDownList, DisplayMember = "Text", ValueMember = "Value" };
    private readonly DateTimePicker _date = new() { Width = 180, Format = DateTimePickerFormat.Custom, CustomFormat = "yyyy-MM-dd", Value = DateTime.Today };
    private readonly NumericUpDown _amount = new() { Width = 400, Minimum = 0.01m, Maximum = 1000000000m, DecimalPlaces = 2, Increment = 1, ThousandsSeparator = true };
    private readonly TextBox _memo = new() { Width = 400 };

    public SettlementEntryForm(AccountingService service, string kind)
    {
        _service = service;
        _kind = kind;
        if (kind is not ("RECEIPT" or "PAYMENT")) throw new ArgumentException("نوع السند غير صالح.");
        Text = kind == "RECEIPT" ? "سند قبض" : "سند صرف";
        Width = 540;
        Height = 470;
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        RightToLeft = RightToLeft.Yes;
        RightToLeftLayout = true;
        Font = new Font("Segoe UI", 10F);
        BackColor = Color.FromArgb(244, 246, 249);

        var rows = service.Parties(kind == "RECEIPT" ? "Customer" : "Supplier");
        _party.DisplayMember = "Text";
        _party.ValueMember = "Id";
        _party.DataSource = rows.Rows.Cast<DataRow>()
            .Select(r => new PartyChoice(Convert.ToInt32(r[0]), Convert.ToString(r[1]) ?? ""))
            .ToList();
        _cash.DataSource = new[]
        {
            new Choice("1010", "1010 - الصندوق"),
            new Choice("1020", "1020 - البنك")
        };
        _memo.Text = kind == "RECEIPT" ? "سند قبض من عميل" : "سند صرف لمورد";

        var header = new Label
        {
            Text = kind == "RECEIPT" ? "تسجيل سند قبض وترحيله" : "تسجيل سند صرف وترحيله",
            Dock = DockStyle.Top,
            Height = 54,
            TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font("Segoe UI", 16F, FontStyle.Bold),
            ForeColor = Color.FromArgb(18, 32, 52)
        };
        var body = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 5,
            Padding = new Padding(20, 5, 20, 5),
            RightToLeft = RightToLeft.Yes
        };
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 32));
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 68));
        for (var i = 0; i < 5; i++) body.RowStyles.Add(new RowStyle(SizeType.Percent, 20));
        Add(body, 0, "العميل / المورد", _party);
        Add(body, 1, "التاريخ", _date);
        Add(body, 2, "الصندوق / البنك", _cash);
        Add(body, 3, "المبلغ (ر.س)", _amount);
        Add(body, 4, "البيان", _memo);

        var footer = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 58, Padding = new Padding(12), FlowDirection = FlowDirection.LeftToRight };
        var save = new Button { Text = "ترحيل السند", Width = 145, Height = 36, BackColor = Color.FromArgb(18, 32, 52), ForeColor = Color.White, FlatStyle = FlatStyle.Flat };
        var cancel = new Button { Text = "إلغاء", Width = 100, Height = 36, DialogResult = DialogResult.Cancel };
        save.Click += Save;
        footer.Controls.Add(save);
        footer.Controls.Add(cancel);
        Controls.Add(body);
        Controls.Add(footer);
        Controls.Add(header);
        AcceptButton = save;
        CancelButton = cancel;
    }

    private static void Add(TableLayoutPanel body, int row, string caption, Control control)
    {
        body.Controls.Add(new Label { Text = caption, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleRight, ForeColor = Color.FromArgb(37, 66, 101) }, 0, row);
        control.Dock = DockStyle.Fill;
        body.Controls.Add(control, 1, row);
    }

    private void Save(object? sender, EventArgs e)
    {
        try
        {
            if (!_licensing.CheckAccess().HasAccess)
                throw new InvalidOperationException("الترخيص غير صالح أو انتهت صلاحيته؛ لا يمكن ترحيل السند.");
            if (_party.SelectedValue is null) throw new InvalidOperationException("أضف العميل أو المورد واختره أولًا.");
            var cash = Convert.ToString(_cash.SelectedValue) ?? "";
            _service.CreateSettlement(_kind, _date.Value.Date, Convert.ToInt32(_party.SelectedValue), cash, _amount.Value, _memo.Text);
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "تعذر ترحيل السند", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private sealed record PartyChoice(int Id, string Name) { public string Text => Name; }
    private sealed record Choice(string Value, string Text);
}
