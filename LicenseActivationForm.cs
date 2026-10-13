using AlSaqr.Accounting.Services;

namespace AlSaqr.Accounting;

internal sealed class LicenseActivationForm : Form
{
    private readonly LicenseService _service;
    private readonly TextBox _filePath = new() { Dock = DockStyle.Fill };
    private readonly LicenseStatus _currentStatus;

    public LicenseActivationForm(LicenseService service, LicenseStatus status)
    {
        _service = service;
        _currentStatus = status;
        Text = "ترخيص الصقر لأنظمة المحاسبة";
        StartPosition = FormStartPosition.CenterParent;
        Width = 730; Height = 390; MinimumSize = new Size(650, 350);
        RightToLeft = RightToLeft.Yes; RightToLeftLayout = true;
        Font = new Font("Segoe UI", 10F);
        BackColor = Color.FromArgb(244, 246, 249);

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(18), ColumnCount = 2, RowCount = 6 };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 140));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 62));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var heading = new Label { Text = "إدارة ترخيص الصقر", Dock = DockStyle.Fill, Font = new Font("Segoe UI", 17, FontStyle.Bold), ForeColor = Color.FromArgb(18, 32, 52), TextAlign = ContentAlignment.MiddleRight };
        var statusLabel = new Label { Text = _currentStatus.Message, Dock = DockStyle.Fill, ForeColor = _currentStatus.Allowed ? Color.DarkGreen : Color.DarkRed, TextAlign = ContentAlignment.MiddleRight, AutoEllipsis = true };
        var machineLabel = new Label { Text = "معرّف الجهاز المرتبط بالرخصة", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleRight };
        var machineId = new TextBox { Text = status.MachineId, Dock = DockStyle.Fill, ReadOnly = true, Font = new Font("Consolas", 9F) };
        machineId.Click += (_, _) => machineId.SelectAll();

        var pathLabel = new Label { Text = "ملف الترخيص الموقّع (JSON)", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleRight };
        var browse = new Button { Text = "استعراض…", Dock = DockStyle.Fill, Height = 34 };
        browse.Click += (_, _) =>
        {
            using var dialog = new OpenFileDialog { Title = "اختيار ملف ترخيص الصقر", Filter = "ملفات الترخيص (*.json)|*.json|كل الملفات (*.*)|*.*", CheckFileExists = true };
            if (dialog.ShowDialog(this) == DialogResult.OK) _filePath.Text = dialog.FileName;
        };
        var choosePanel = new Panel { Dock = DockStyle.Fill };
        choosePanel.Controls.Add(_filePath);
        var activate = new Button { Text = "التحقق والتفعيل", Dock = DockStyle.Fill, Height = 38, BackColor = Color.FromArgb(190, 151, 61), ForeColor = Color.FromArgb(18, 32, 52), FlatStyle = FlatStyle.Flat };
        var cancel = new Button { Text = "إغلاق", Dock = DockStyle.Fill, Height = 38, DialogResult = DialogResult.Cancel };
        activate.Click += (_, _) =>
        {
            try
            {
                _service.ActivateFromFile(_filePath.Text);
                MessageBox.Show(this, "تم التحقق من التوقيع وربط الترخيص بهذا الجهاز.", "تم التفعيل", MessageBoxButtons.OK, MessageBoxIcon.Information);
                DialogResult = DialogResult.OK;
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "تعذر تفعيل الترخيص", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        };

        root.Controls.Add(heading, 0, 0); root.SetColumnSpan(heading, 2);
        root.Controls.Add(statusLabel, 0, 1); root.SetColumnSpan(statusLabel, 2);
        root.Controls.Add(machineLabel, 0, 2); root.SetColumnSpan(machineLabel, 2);
        root.Controls.Add(machineId, 0, 3); root.SetColumnSpan(machineId, 2);
        root.Controls.Add(pathLabel, 0, 4);
        root.Controls.Add(browse, 1, 4);
        root.Controls.Add(choosePanel, 0, 5);
        root.Controls.Add(activate, 1, 5);
        Controls.Add(root);
        AcceptButton = activate;
        CancelButton = cancel;
        var bottom = new Panel { Dock = DockStyle.Bottom, Height = 48, Padding = new Padding(12, 4, 12, 4) };
        cancel.Width = 110; cancel.Dock = DockStyle.Right; bottom.Controls.Add(cancel);
        Controls.Add(bottom);
    }
}
