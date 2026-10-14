using System.Drawing;
using System.Text;
using AlSaqr.Accounting.Services;

namespace AlSaqr.Accounting.Forms;

internal sealed class LicenseForm : Form
{
    private readonly LicenseService _service;
    private readonly bool _startupGate;
    private readonly TextBox _machineId = new() { ReadOnly = true, Width = 480 };
    private readonly TextBox _licensee = new() { Width = 480, PlaceholderText = "اسم العميل / المنشأة المرخص لها" };
    private readonly Label _status = new() { Dock = DockStyle.Fill, Padding = new Padding(12), TextAlign = ContentAlignment.MiddleRight };
    private readonly Label _expiry = new() { Dock = DockStyle.Top, Height = 34, Padding = new Padding(12), TextAlign = ContentAlignment.MiddleRight };
    private readonly Button _continue = new() { Text = "متابعة", Width = 135, Height = 36, DialogResult = DialogResult.OK };

    public LicenseForm(LicenseService service, bool startupGate = false)
    {
        _service = service;
        _startupGate = startupGate;
        Text = "خالد للأنظمة المحاسبية | الترخيص والتفعيل";
        Width = 760;
        Height = 430;
        MinimumSize = new Size(680, 390);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        RightToLeft = RightToLeft.Yes;
        RightToLeftLayout = true;
        Font = new Font("Segoe UI", 10F);
        BackColor = Color.FromArgb(244, 246, 249);

        var title = new Label
        {
            Text = "الترخيص الرسمي — خالد للأنظمة المحاسبية",
            Dock = DockStyle.Top, Height = 52, Padding = new Padding(14),
            Font = new Font("Segoe UI", 15F, FontStyle.Bold),
            ForeColor = Color.FromArgb(18, 32, 52), TextAlign = ContentAlignment.MiddleRight
        };
        var body = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 4,
            Padding = new Padding(14), BackColor = Color.White
        };
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        AddRow(body, 0, "حالة الترخيص", _status);
        AddRow(body, 1, "تاريخ الانتهاء", _expiry);
        AddRow(body, 2, "معرّف الجهاز", _machineId);
        AddRow(body, 3, "اسم الجهة", _licensee);

        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom, Height = 58, FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(8), WrapContents = true, BackColor = Color.FromArgb(235, 239, 244)
        };
        var request = MakeButton("تصدير طلب التفعيل");
        request.Click += (_, _) => ExportRequest();
        var copy = MakeButton("نسخ معرّف الجهاز");
        copy.Click += (_, _) =>
        {
            try { Clipboard.SetText(_service.DeviceId); _status.Text = "تم نسخ معرّف الجهاز."; }
            catch (Exception ex) { ShowError(ex.Message); }
        };
        var import = MakeButton("استيراد ملف الترخيص");
        import.Click += (_, _) => ImportLicense();
        _continue.Click += (_, _) =>
        {
            var current = _service.InspectStatus();
            if (!current.HasAccess)
            {
                DialogResult = DialogResult.None;
                ShowError("لا يمكن المتابعة قبل تفعيل الترخيص. " + current.Message);
                return;
            }
            DialogResult = DialogResult.OK;
            Close();
        };
        actions.Controls.Add(request);
        actions.Controls.Add(copy);
        actions.Controls.Add(import);
        actions.Controls.Add(_continue);
        RefreshStatus();

        Controls.Add(body);
        Controls.Add(actions);
        Controls.Add(title);
    }

    private static void AddRow(TableLayoutPanel panel, int row, string caption, Control content)
    {
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.Controls.Add(new Label
        {
            Text = caption, AutoSize = true, Anchor = AnchorStyles.Right | AnchorStyles.Top,
            Margin = new Padding(4, 10, 8, 8), TextAlign = ContentAlignment.MiddleRight
        }, 0, row);
        content.Margin = new Padding(4, 6, 4, 6);
        content.Dock = DockStyle.Fill;
        panel.Controls.Add(content, 1, row);
    }

    private static Button MakeButton(string caption) => new()
    {
        Text = caption, Width = Math.Max(120, TextRenderer.MeasureText(caption, SystemFonts.DefaultFont).Width + 28),
        Height = 36, BackColor = Color.FromArgb(18, 32, 52), ForeColor = Color.White, FlatStyle = FlatStyle.Flat,
        Margin = new Padding(4), Cursor = Cursors.Hand
    };

    private void RefreshStatus()
    {
        var current = _service.InspectStatus();
        _machineId.Text = current.MachineId;
        _status.Text = current.Message;
        _expiry.Text = current.ExpiresAtUtc.HasValue
            ? current.ExpiresAtUtc.Value.ToLocalTime().ToString("yyyy-MM-dd")
            : "غير متوفر";
        _continue.Visible = current.HasAccess;
        if (_startupGate && !current.HasAccess)
            _status.Text += " — يلزم تفعيل الترخيص للمتابعة.";
    }

    private void ExportRequest()
    {
        using var dialog = new SaveFileDialog
        {
            Filter = "طلب ترخيص الصقر (*.alsaqr-request.json)|*.alsaqr-request.json|ملف JSON (*.json)|*.json",
            FileName = "AlSaqr-License-Request-" + DateTime.Now.ToString("yyyyMMdd") + ".alsaqr-request.json",
            AddExtension = true, DefaultExt = "alsaqr-request.json"
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            _service.ExportActivationRequest(dialog.FileName, _licensee.Text);
            _status.Text = "تم حفظ طلب التفعيل. أرسل الملف إلى الجهة التي تصدر تراخيص الصقر.";
        }
        catch (Exception ex) { ShowError(ex.Message); }
    }

    private void ImportLicense()
    {
        using var dialog = new OpenFileDialog
        {
            Filter = "ملف ترخيص الصقر (*.json)|*.json|كل الملفات (*.*)|*.*",
            Title = "اختيار ملف ترخيص موقع رقميًا"
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            var result = _service.ImportAndActivate(dialog.FileName);
            RefreshStatus();
            if (!result.HasAccess)
            {
                ShowError(result.Message);
                return;
            }
            MessageBox.Show(this, result.Message, "خالد للأنظمة المحاسبية | تم التفعيل", MessageBoxButtons.OK, MessageBoxIcon.Information);
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception ex) { ShowError(ex.Message); }
    }

    private void ShowError(string message) =>
        MessageBox.Show(this, message, "خالد للأنظمة المحاسبية | الترخيص", MessageBoxButtons.OK, MessageBoxIcon.Warning);
}
