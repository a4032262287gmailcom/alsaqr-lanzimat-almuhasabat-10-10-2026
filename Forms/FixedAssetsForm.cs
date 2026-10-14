using System.Globalization;
using AlSaqr.Accounting.Services;

namespace AlSaqr.Accounting.Forms;

internal sealed class FixedAssetsForm : Form
{
    private readonly AuthUser _user;
    private readonly FixedAssetService _service = new();
    private readonly LicenseService _licensing = new();
    private readonly DataGridView _grid = new();
    private readonly DateTimePicker _period = new()
    {
        Width = 150, Format = DateTimePickerFormat.Custom, CustomFormat = "yyyy-MM",
        Value = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1)
    };
    private readonly Label _status = new()
    {
        Dock = DockStyle.Bottom, Height = 30, TextAlign = ContentAlignment.MiddleRight,
        Padding = new Padding(10)
    };

    private bool CanPost => (_user.Role is "Admin" or "Accountant") && _licensing.CheckAccess().HasAccess;

    public FixedAssetsForm(AuthUser user)
    {
        _user = user;
        Text = "الصقر لأنظمة المحاسبة | سجل الأصول الثابتة والإهلاك";
        WindowState = FormWindowState.Maximized;
        MinimumSize = new Size(1000, 650);
        StartPosition = FormStartPosition.CenterParent;
        RightToLeft = RightToLeft.Yes;
        RightToLeftLayout = true;
        Font = new Font("Segoe UI", 10F);
        BackColor = Color.FromArgb(244, 246, 249);

        var header = new Panel { Dock = DockStyle.Top, Height = 56, BackColor = Color.FromArgb(18, 32, 52), Padding = new Padding(14) };
        header.Controls.Add(new Label
        {
            Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleRight,
            ForeColor = Color.White, Font = new Font("Segoe UI", 16F, FontStyle.Bold),
            Text = "سجل الأصول الثابتة والإهلاك بالقسط الثابت"
        });

        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Top, Height = 58, FlowDirection = FlowDirection.RightToLeft,
            WrapContents = true, Padding = new Padding(8), BackColor = Color.White
        };
        actions.Controls.Add(MakeButton("تحديث السجل", Reload, false));
        actions.Controls.Add(new Label { Text = "فترة الإهلاك:", AutoSize = true, Padding = new Padding(8, 8, 2, 0) });
        actions.Controls.Add(_period);
        actions.Controls.Add(MakeButton("ترحيل إهلاك الشهر", PostDepreciation, true));
        actions.Controls.Add(MakeButton("تسجيل أصل جديد", AddAsset, true));

        _grid.Dock = DockStyle.Fill;
        _grid.ReadOnly = true;
        _grid.AllowUserToAddRows = false;
        _grid.AllowUserToDeleteRows = false;
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.MultiSelect = false;
        _grid.RowHeadersVisible = false;
        _grid.BackgroundColor = Color.White;
        _grid.RightToLeft = RightToLeft.Yes;

        Controls.Add(_grid);
        Controls.Add(_status);
        Controls.Add(actions);
        Controls.Add(header);
        Reload();
    }

    private Button MakeButton(string text, Action action, bool requiresPosting) 
    {
        var button = new Button
        {
            Text = text, Width = text.Length > 15 ? 190 : 145, Height = 36,
            Enabled = !requiresPosting || CanPost,
            BackColor = requiresPosting ? Color.FromArgb(190, 151, 61) : Color.FromArgb(37, 66, 101),
            ForeColor = requiresPosting ? Color.FromArgb(18, 32, 52) : Color.White,
            FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand, Margin = new Padding(4)
        };
        button.Click += (_, _) => Guard(action);
        return button;
    }

    private void Reload()
    {
        try
        {
            _grid.DataSource = _service.Register();
            _status.Text = $"عدد الأصول: {_service.ActiveAssets().Count} — التسجيل والإهلاك يُرحّلان بقيود مزدوجة مع سجل تدقيق.";
        }
        catch (Exception ex) { ShowError(ex); }
    }

    private void AddAsset()
    {
        using var dialog = new Form
        {
            Text = "تسجيل أصل ثابت", Width = 570, Height = 520,
            StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false, MinimizeBox = false, RightToLeft = RightToLeft.Yes, RightToLeftLayout = true,
            Font = Font, BackColor = Color.White
        };
        var body = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 2, Padding = new Padding(16),
            AutoScroll = true, BackColor = Color.White
        };
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        var code = new TextBox { Width = 320 };
        var name = new TextBox { Width = 320 };
        var date = new DateTimePicker { Width = 320, Format = DateTimePickerFormat.Custom, CustomFormat = "yyyy-MM-dd", Value = DateTime.Today };
        var cost = new NumericUpDown { Width = 320, Minimum = 0.01m, Maximum = 1000000000m, DecimalPlaces = 2, Value = 1000m };
        var residual = new NumericUpDown { Width = 320, Minimum = 0m, Maximum = 999999999m, DecimalPlaces = 2, Value = 0m };
        var life = new NumericUpDown { Width = 320, Minimum = 1, Maximum = 1200, Value = 60 };
        var payment = new ComboBox { Width = 320, DropDownStyle = ComboBoxStyle.DropDownList };
        payment.Items.Add(new AccountChoice("1010", "1010 - الصندوق"));
        payment.Items.Add(new AccountChoice("1020", "1020 - البنك"));
        payment.DisplayMember = nameof(AccountChoice.Label);
        payment.ValueMember = nameof(AccountChoice.Code);
        payment.SelectedIndex = 0;

        AddRow(body, "رمز الأصل", code);
        AddRow(body, "اسم الأصل", name);
        AddRow(body, "تاريخ الاقتناء", date);
        AddRow(body, "تكلفة الاقتناء", cost);
        AddRow(body, "القيمة التخريدية", residual);
        AddRow(body, "العمر الإنتاجي (شهر)", life);
        AddRow(body, "حساب السداد", payment);

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 54, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(8), BackColor = Color.FromArgb(235, 239, 244) };
        var save = MakeButton("حفظ وترحيل الاقتناء", () =>
        {
            var id = _service.CreateAsset(code.Text, name.Text, date.Value.Date, cost.Value,
                residual.Value, Decimal.ToInt32(life.Value), Convert.ToString(payment.SelectedValue, CultureInfo.InvariantCulture) ?? "1010", _user.UserName);
            _status.Text = $"تم تسجيل الأصل وترحيل قيد الاقتناء رقم المعرف {id}.";
            dialog.DialogResult = DialogResult.OK;
            dialog.Close();
        }, true);
        var cancel = new Button { Text = "إلغاء", Width = 100, Height = 36, DialogResult = DialogResult.Cancel };
        buttons.Controls.Add(save);
        buttons.Controls.Add(cancel);
        dialog.Controls.Add(body);
        dialog.Controls.Add(buttons);
        dialog.AcceptButton = save;
        dialog.CancelButton = cancel;
        if (dialog.ShowDialog(this) == DialogResult.OK) Reload();
    }

    private void PostDepreciation()
    {
        var period = new DateTime(_period.Value.Year, _period.Value.Month, 1);
        var count = _service.PostMonthlyDepreciation(period, _user.UserName);
        Reload();
        _status.Text = count == 0
            ? $"لا توجد إهلاكات جديدة للترحيل عن {period:yyyy-MM}؛ قد تكون الفترة مرحّلة مسبقًا أو لم يحِن موعد الإهلاك."
            : $"تم ترحيل إهلاك {count} أصل/أصول عن الفترة {period:yyyy-MM} بقيود مزدوجة.";
    }

    private static void AddRow(TableLayoutPanel body, string label, Control input)
    {
        var row = body.RowCount++;
        body.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        body.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Right | AnchorStyles.Top, Margin = new Padding(4, 9, 8, 8) }, 0, row);
        input.Margin = new Padding(4, 5, 4, 5);
        input.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        body.Controls.Add(input, 1, row);
    }

    private void Guard(Action action)
    {
        try { action(); }
        catch (Exception ex) { ShowError(ex); }
    }

    private void ShowError(Exception ex)
    {
        _status.Text = "تعذر إكمال العملية: " + ex.Message;
        MessageBox.Show(this, ex.Message, "الصقر لأنظمة المحاسبة | الأصول الثابتة", MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }

    private sealed record AccountChoice(string Code, string Label);
}
