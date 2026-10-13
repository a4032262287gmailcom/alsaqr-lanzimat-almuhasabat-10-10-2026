using AlSaqr.Accounting.Services;

namespace AlSaqr.Accounting;

internal sealed partial class MainForm
{
    private readonly SystemSettingsService _settingsService = new();

    private void ShowCashier()
    {
        if (!CanPost) { Denied(); return; }
        using var form = new CashierForm(_service, _settingsService.Load());
        if (form.ShowDialog(this) == DialogResult.OK)
        {
            ShowInvoices("SALE");
            SetStatus("تم ترحيل عملية الكاشير عبر خدمة الفواتير المحاسبية.");
        }
    }

    private void ShowSystemSettings()
    {
        if (!IsAdmin) { Denied(); return; }
        using var form = new SystemSettingsForm(_settingsService);
        if (form.ShowDialog(this) == DialogResult.OK)
        {
            var settings = _settingsService.Load();
            ApplyThemeColors(settings.Theme);
            Text = settings.CompanyName + " | نظام المحاسبة";
            SetStatus("تم حفظ إعدادات المنشأة والطباعة والسمات. الإعدادات الحساسة محفوظة محليًا بتشفير Windows DPAPI.");
        }
    }

    private void ShowLicenseManagement()
    {
        try
        {
            var service = new LicenseService();
            var status = service.GetStatus();
            using var form = new LicenseActivationForm(service, status);
            if (form.ShowDialog(this) == DialogResult.OK)
                SetStatus(new LicenseService().GetStatus().Message);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "تعذر قراءة الترخيص", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void ApplyThemeColors(string theme)
    {
        var oldNavy = _navy; var oldBlue = _blue; var oldGold = _gold; var oldPaper = _paper;
        var palette = SystemSettingsService.PaletteFor(theme);
        _navy = palette.Navy; _blue = palette.Blue; _gold = palette.Gold; _paper = palette.Paper;
        BackColor = _paper;

        void Recolor(Control control)
        {
            if (control.BackColor == oldNavy) control.BackColor = _navy;
            else if (control.BackColor == oldBlue) control.BackColor = _blue;
            else if (control.BackColor == oldGold) control.BackColor = _gold;
            else if (control.BackColor == oldPaper) control.BackColor = _paper;

            if (control.ForeColor == oldNavy) control.ForeColor = _navy;
            else if (control.ForeColor == oldBlue) control.ForeColor = _blue;
            else if (control.ForeColor == oldGold) control.ForeColor = _gold;

            if (control is Button button)
            {
                button.BackColor = ReferenceEquals(button, _activeNavigationButton) ? _gold : _blue;
                button.ForeColor = ReferenceEquals(button, _activeNavigationButton) ? _navy : Color.White;
            }
            if (control is DataGridView grid)
            {
                grid.BackgroundColor = Color.White;
                grid.ColumnHeadersDefaultCellStyle.BackColor = _blue;
                grid.ColumnHeadersDefaultCellStyle.ForeColor = Color.White;
                grid.EnableHeadersVisualStyles = false;
            }
            foreach (Control child in control.Controls) Recolor(child);
        }
        foreach (Control control in Controls) Recolor(control);
        if (_content is not null) _content.BackColor = _paper;
        _status.BackColor = theme == "Dark" ? Color.FromArgb(46, 56, 72) : Color.FromArgb(230, 234, 240);
        _status.ForeColor = theme == "Dark" ? palette.Text : _navy;
    }
}
