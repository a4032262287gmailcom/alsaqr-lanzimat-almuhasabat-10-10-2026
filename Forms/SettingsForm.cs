using System.Drawing;
using AlSaqr.Accounting.Services;

namespace AlSaqr.Accounting.Forms;

internal sealed class SettingsForm : Form
{
    private readonly TextBox _entity = new() { Width = 370 };
    private readonly TextBox _vatNumber = new() { Width = 370 };
    private readonly TextBox _phone = new() { Width = 370 };
    private readonly TextBox _address = new() { Width = 370 };
    private readonly TextBox _footer = new() { Width = 370 };
    private readonly TextBox _backup = new() { Width = 315 };
    private readonly ComboBox _theme = new() { Width = 370, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly NumericUpDown _vatRate = new() { Width = 150, Minimum = 0, Maximum = 100, DecimalPlaces = 2, Increment = 0.5m };
    private readonly Label _status = new() { Dock = DockStyle.Bottom, Height = 28, TextAlign = ContentAlignment.MiddleRight, Padding = new Padding(8) };
    private readonly AccountingAppSettings _original;

    public AccountingAppSettings? SavedSettings { get; private set; }

    public SettingsForm()
    {
        _original = AppSettingsService.Load();
        Text = "الصقر لأنظمة المحاسبة | الإعدادات العامة والثيمات";
        Width = 660;
        Height = 720;
        MinimumSize = new Size(610, 650);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        RightToLeft = RightToLeft.Yes;
        RightToLeftLayout = true;
        Font = new Font("Segoe UI", 10F);
        BackColor = Color.FromArgb(244, 246, 249);

        _entity.Text = _original.LegalEntityName;
        _vatNumber.Text = _original.VatRegistrationNumber;
        _phone.Text = _original.Phone;
        _address.Text = _original.Address;
        _footer.Text = _original.ReceiptFooter;
        _backup.Text = _original.BackupDirectory;
        _vatRate.Value = Math.Clamp(_original.DefaultVatRate, 0m, 100m);
        _theme.DisplayMember = nameof(ThemeChoice.Text);
        _theme.ValueMember = nameof(ThemeChoice.Value);
        _theme.DataSource = UiThemePalette.All.Select(x => new ThemeChoice(x.Name, x.DisplayName)).ToList();
        _theme.SelectedValue = _original.Theme;

        var title = new Label
        {
            Text = "الإعدادات التجارية ومظهر التطبيق",
            Dock = DockStyle.Top, Height = 54, Padding = new Padding(14),
            Font = new Font("Segoe UI", 16F, FontStyle.Bold),
            ForeColor = Color.FromArgb(18, 32, 52), TextAlign = ContentAlignment.MiddleRight
        };
        var hint = new Label
        {
            Text = "تُحفظ الإعدادات محليًا على هذا الجهاز، ولا تُغيّر القيود المرحلة.",
            Dock = DockStyle.Top, Height = 34, Padding = new Padding(14, 0, 14, 0),
            ForeColor = Color.DimGray, TextAlign = ContentAlignment.MiddleRight
        };

        var body = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 2, AutoScroll = true,
            Padding = new Padding(14), BackColor = Color.White
        };
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 180));
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        AddRow(body, "اسم المنشأة في الفواتير", _entity);
        AddRow(body, "الرقم الضريبي", _vatNumber);
        AddRow(body, "الهاتف", _phone);
        AddRow(body, "العنوان", _address);
        AddRow(body, "تذييل الفاتورة", _footer);
        AddRow(body, "الثيم الافتراضي", _theme);
        AddRow(body, "نسبة الضريبة الافتراضية %", _vatRate);

        var backupRow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false, AutoSize = true
        };
        backupRow.Controls.Add(_backup);
        var browse = MakeButton("اختيار مجلد");
        browse.Click += (_, _) =>
        {
            using var dialog = new FolderBrowserDialog { SelectedPath = Directory.Exists(_backup.Text) ? _backup.Text : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments) };
            if (dialog.ShowDialog(this) == DialogResult.OK) _backup.Text = dialog.SelectedPath;
        };
        backupRow.Controls.Add(browse);
        AddRow(body, "مجلد النسخ الاحتياطي", backupRow);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom, Height = 56, FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(10), WrapContents = false, BackColor = Color.FromArgb(235, 239, 244)
        };
        var save = MakeButton("حفظ الإعدادات");
        save.Click += (_, _) => SaveSettings();
        var cancel = MakeButton("إلغاء");
        cancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
        buttons.Controls.Add(save);
        buttons.Controls.Add(cancel);

        Controls.Add(body);
        Controls.Add(buttons);
        Controls.Add(hint);
        Controls.Add(title);
        Controls.Add(_status);
    }

    private static void AddRow(TableLayoutPanel panel, string label, Control input)
    {
        var row = panel.RowCount++;
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.Controls.Add(new Label
        {
            Text = label, AutoSize = true, Anchor = AnchorStyles.Right | AnchorStyles.Top,
            Margin = new Padding(4, 10, 8, 8), TextAlign = ContentAlignment.MiddleRight
        }, 0, row);
        input.Margin = new Padding(4, 5, 4, 6);
        input.Anchor = AnchorStyles.Right | AnchorStyles.Left | AnchorStyles.Top;
        if (input is TextBox text && label.Contains("تذييل")) text.Height = 54;
        panel.Controls.Add(input, 1, row);
    }

    private static Button MakeButton(string text) => new()
    {
        Text = text, Height = 34, Width = Math.Max(110, TextRenderer.MeasureText(text, SystemFonts.DefaultFont).Width + 30),
        BackColor = Color.FromArgb(18, 32, 52), ForeColor = Color.White, FlatStyle = FlatStyle.Flat,
        Margin = new Padding(4), Cursor = Cursors.Hand
    };

    private void SaveSettings()
    {
        try
        {
            var settings = new AccountingAppSettings(
                _entity.Text.Trim(), _vatNumber.Text.Trim(), _phone.Text.Trim(), _address.Text.Trim(),
                string.IsNullOrWhiteSpace(_footer.Text) ? "شكرًا لتعاملكم مع الصقر لأنظمة المحاسبة" : _footer.Text.Trim(),
                Convert.ToString(_theme.SelectedValue) ?? "Navy",
                _vatRate.Value,
                _backup.Text.Trim());
            AppSettingsService.Save(settings);
            SavedSettings = settings;
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception ex)
        {
            _status.Text = "تعذر حفظ الإعدادات: " + ex.Message;
            MessageBox.Show(this, ex.Message, "الصقر لأنظمة المحاسبة | الإعدادات", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private sealed record ThemeChoice(string Value, string Text);
}
