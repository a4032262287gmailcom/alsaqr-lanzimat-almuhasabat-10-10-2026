using System.Drawing.Printing;
using AlSaqr.Accounting.Services;

namespace AlSaqr.Accounting;

internal sealed class SystemSettingsForm : Form
{
    private readonly SystemSettingsService _service;
    private readonly TextBox _company = new() { Width = 350 };
    private readonly TextBox _cr = new() { Width = 350 };
    private readonly TextBox _vat = new() { Width = 350 };
    private readonly TextBox _address = new() { Width = 350 };
    private readonly TextBox _phone = new() { Width = 350 };
    private readonly TextBox _footer = new() { Width = 350 };
    private readonly ComboBox _theme = new() { Width = 350, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox _printer = new() { Width = 350, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly CheckBox _autoPrint = new() { Text = "طباعة الإيصال تلقائيًا بعد ترحيل البيع", AutoSize = true };

    public SystemSettingsForm(SystemSettingsService service)
    {
        _service = service;
        Text = "إعدادات المنشأة والسمات والطباعة";
        StartPosition = FormStartPosition.CenterParent;
        Width = 820; Height = 690; MinimumSize = new Size(720, 620);
        RightToLeft = RightToLeft.Yes; RightToLeftLayout = true;
        Font = new Font("Segoe UI", 10F);
        BackColor = Color.FromArgb(244, 246, 249);

        var settings = service.Load();
        _company.Text = settings.CompanyName; _cr.Text = settings.CommercialRegistration;
        _vat.Text = settings.VatNumber; _address.Text = settings.Address;
        _phone.Text = settings.Phone; _footer.Text = settings.ReceiptFooter;
        _theme.Items.AddRange(new object[] { new Choice("Classic", "الكحلي والذهبي"), new Choice("Light", "فاتح"), new Choice("Dark", "داكن") });
        _theme.SelectedItem = _theme.Items.Cast<Choice>().FirstOrDefault(c => c.Value == settings.Theme) ?? (Choice)_theme.Items[0];

        _printer.Items.Add(new Choice("", "الطابعة الافتراضية"));
        foreach (string name in PrinterSettings.InstalledPrinters)
            _printer.Items.Add(new Choice(name, name));
        if (!string.IsNullOrWhiteSpace(settings.PrinterName) && !_printer.Items.Cast<Choice>().Any(p => p.Value == settings.PrinterName))
            _printer.Items.Add(new Choice(settings.PrinterName, settings.PrinterName));
        _printer.SelectedItem = _printer.Items.Cast<Choice>().FirstOrDefault(p => p.Value == settings.PrinterName) ?? _printer.Items[0];
        _autoPrint.Checked = settings.AutoPrintReceipt;

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(20), ColumnCount = 2, RowCount = 10 };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 190));
        for (var i = 0; i < 8; i++) root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 45));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        AddField(root, 0, "اسم المنشأة", _company);
        AddField(root, 1, "السجل التجاري", _cr);
        AddField(root, 2, "الرقم الضريبي", _vat);
        AddField(root, 3, "العنوان", _address);
        AddField(root, 4, "الهاتف", _phone);
        AddField(root, 5, "تذييل الإيصال", _footer);
        AddField(root, 6, "سمة الواجهة", _theme);
        AddField(root, 7, "الطابعة", _printer);
        root.Controls.Add(_autoPrint, 0, 8); root.SetColumnSpan(_autoPrint, 2);

        var bottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 54, FlowDirection = FlowDirection.LeftToRight, Padding = new Padding(12, 7, 12, 7) };
        var save = new Button { Text = "حفظ الإعدادات", Width = 150, Height = 36 };
        var cancel = new Button { Text = "إلغاء", Width = 100, Height = 36, DialogResult = DialogResult.Cancel };
        save.Click += (_, _) =>
        {
            try
            {
                var selectedTheme = _theme.SelectedItem as Choice ?? new Choice("Classic", "الكلاسيكي");
                var selectedPrinter = _printer.SelectedItem as Choice ?? new Choice("", "");
                _service.Save(new SystemSettings
                {
                    CompanyName = _company.Text, CommercialRegistration = _cr.Text, VatNumber = _vat.Text,
                    Address = _address.Text, Phone = _phone.Text, ReceiptFooter = _footer.Text,
                    Theme = selectedTheme.Value, PrinterName = selectedPrinter.Value, AutoPrintReceipt = _autoPrint.Checked
                });
                DialogResult = DialogResult.OK;
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "تعذر حفظ الإعدادات", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        };
        bottom.Controls.Add(save); bottom.Controls.Add(cancel);
        Controls.Add(root); Controls.Add(bottom);
        AcceptButton = save; CancelButton = cancel;
    }

    private static void AddField(TableLayoutPanel root, int row, string label, Control input)
    {
        var caption = new Label { Text = label, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleRight };
        input.Dock = DockStyle.Fill;
        input.Margin = new Padding(3, 6, 3, 6);
        root.Controls.Add(input, 0, row);
        root.Controls.Add(caption, 1, row);
    }

    private sealed record Choice(string Value, string Text)
    {
        public override string ToString() => Text;
    }
}
