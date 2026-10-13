using System.Data;
using System.Globalization;
using AlSaqr.Accounting.Services;

namespace AlSaqr.Accounting;

internal sealed partial class MainForm
{
    private void ShowReturns(string kind)
    {
        var title = kind == "SALE_RETURN" ? "مردودات البيع المرحلة" : "مردودات الشراء المرحلة";
        var actions = new List<(string Text, Action Run)>();
        if (CanPost)
            actions.Add((kind == "SALE_RETURN" ? "إشعار دائن / مردود بيع جديد" : "مردود شراء جديد", () => AddReturn(kind)));
        actions.Add(("تحديث", () => ShowReturns(kind)));
        ShowTablePage(title, _returns.Register(kind, new DateTime(2000, 1, 1), DateTime.Today.AddDays(1)), actions.ToArray());
    }

    private void AddReturn(string kind)
    {
        if (!CanPost) { Denied(); return; }
        using var form = new ReturnEntryForm(_service, _returns, kind);
        if (form.ShowDialog(this) == DialogResult.OK)
        {
            ShowReturns(kind);
            SetStatus("تم ترحيل المردود مع القيد الضريبي وتحديث المخزون داخل معاملة قاعدة بيانات واحدة.");
        }
    }
}

internal sealed class ReturnEntryForm : Form
{
    private readonly AccountingService _accounting;
    private readonly ReturnService _returns;
    private readonly string _kind;
    private readonly ComboBox _party = new() { Width = 330, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox _product = new() { Width = 330, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly NumericUpDown _quantity = new() { Width = 120, Minimum = 0.001m, Maximum = 10000000m, DecimalPlaces = 3, Value = 1m, Increment = 1m, ThousandsSeparator = true };
    private readonly NumericUpDown _unitPrice = new() { Width = 140, Minimum = 0, Maximum = 100000000m, DecimalPlaces = 2, Increment = 1m, ThousandsSeparator = true };
    private readonly DateTimePicker _date = new() { Width = 150, Format = DateTimePickerFormat.Custom, CustomFormat = "yyyy-MM-dd", Value = DateTime.Today };
    private readonly CheckBox _onAccount = new() { AutoSize = true, Checked = true };
    private readonly DataGridView _grid = new();
    private readonly List<ReturnLineInput> _lines = new();
    private readonly List<ProductInfo> _products;

    public ReturnEntryForm(AccountingService accounting, ReturnService returns, string kind)
    {
        if (kind is not ("SALE_RETURN" or "PURCHASE_RETURN")) throw new ArgumentException("نوع المردود غير صالح.");
        _accounting = accounting;
        _returns = returns;
        _kind = kind;
        Text = kind == "SALE_RETURN" ? "مردود بيع / إشعار دائن" : "مردود شراء";
        Width = 1060;
        Height = 690;
        MinimumSize = new Size(900, 600);
        StartPosition = FormStartPosition.CenterParent;
        RightToLeft = RightToLeft.Yes;
        RightToLeftLayout = true;
        Font = new Font("Segoe UI", 10F);
        BackColor = Color.FromArgb(244, 246, 249);

        var partyTable = accounting.Parties(kind == "SALE_RETURN" ? "Customer" : "Supplier");
        var parties = partyTable.Rows.Cast<DataRow>()
            .Where(r => Convert.ToString(r[5]) == "نشط")
            .Select(r => new PartyChoice(Convert.ToInt32(r[0]), Convert.ToString(r[1]) ?? ""))
            .ToList();
        _party.DisplayMember = nameof(PartyChoice.Text);
        _party.ValueMember = nameof(PartyChoice.Id);
        _party.DataSource = parties;

        _products = accounting.ActiveProducts();
        _product.DisplayMember = nameof(ProductChoice.Text);
        _product.ValueMember = nameof(ProductChoice.Id);
        _product.DataSource = _products.Select(p => new ProductChoice(p.Id, $"{p.Code} - {p.Name}", p.Price, p.Cost)).ToList();
        _product.SelectedIndexChanged += (_, _) => SetDefaultPrice();

        _onAccount.Text = kind == "SALE_RETURN" ? "إصدار رصيد دائن للعميل بدل النقد" : "خصم المردود من حساب المورد";
        if (kind == "PURCHASE_RETURN")
        {
            _unitPrice.Enabled = false;
            _unitPrice.AccessibleDescription = "يُقيّم مردود الشراء بمتوسط تكلفة المخزون الحالي.";
        }

        var header = new Label
        {
            Text = kind == "SALE_RETURN" ? "تسجيل مردود بيع: تخفيض الإيراد والضريبة وإعادة الكمية للمخزون" : "تسجيل مردود شراء: تخفيض المورد والضريبة وإخراج الكمية من المخزون",
            Dock = DockStyle.Top,
            Height = 52,
            Padding = new Padding(12, 0, 12, 0),
            TextAlign = ContentAlignment.MiddleRight,
            Font = new Font("Segoe UI", 13F, FontStyle.Bold),
            ForeColor = Color.FromArgb(18, 32, 52)
        };
        var top = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 150,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = true,
            Padding = new Padding(10),
            BackColor = Color.White
        };
        top.Controls.Add(FieldLabel("العميل / المورد")); top.Controls.Add(_party);
        top.Controls.Add(FieldLabel("التاريخ")); top.Controls.Add(_date);
        top.Controls.Add(_onAccount);
        top.Controls.Add(FieldLabel("الصنف")); top.Controls.Add(_product);
        top.Controls.Add(FieldLabel("الكمية")); top.Controls.Add(_quantity);
        top.Controls.Add(FieldLabel(kind == "SALE_RETURN" ? "سعر رد الوحدة" : "متوسط تكلفة الوحدة")); top.Controls.Add(_unitPrice);
        var add = new Button { Text = "إضافة سطر", Width = 120, Height = 34, BackColor = Color.FromArgb(37, 66, 101), ForeColor = Color.White, FlatStyle = FlatStyle.Flat };
        add.Click += AddLine;
        top.Controls.Add(add);

        _grid.Dock = DockStyle.Fill;
        _grid.ReadOnly = true;
        _grid.AllowUserToAddRows = false;
        _grid.AllowUserToDeleteRows = false;
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _grid.RowHeadersVisible = false;
        _grid.RightToLeft = RightToLeft.Yes;
        _grid.BackgroundColor = Color.White;
        _grid.EnableHeadersVisualStyles = false;
        _grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(18, 32, 52);
        _grid.ColumnHeadersDefaultCellStyle.ForeColor = Color.White;
        _grid.Columns.Add("product", "الصنف");
        _grid.Columns.Add("qty", "الكمية");
        _grid.Columns.Add("price", "سعر الوحدة");
        _grid.Columns.Add("vat", "الضريبة");
        _grid.Columns.Add("total", "الإجمالي");

        var footer = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 62, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(8), BackColor = Color.White };
        var total = new Label { Name = "returnTotal", Text = "الإجمالي: 0.00 ر.س", Width = 330, Height = 38, TextAlign = ContentAlignment.MiddleRight, Font = new Font("Segoe UI", 12F, FontStyle.Bold), ForeColor = Color.FromArgb(18, 32, 52) };
        var post = new Button { Text = "ترحيل المردود", Width = 150, Height = 36, BackColor = Color.FromArgb(18, 32, 52), ForeColor = Color.White, FlatStyle = FlatStyle.Flat };
        var cancel = new Button { Text = "إلغاء", Width = 100, Height = 36, DialogResult = DialogResult.Cancel };
        post.Click += Post;
        footer.Controls.Add(post); footer.Controls.Add(cancel); footer.Controls.Add(total);
        Controls.Add(_grid); Controls.Add(footer); Controls.Add(top); Controls.Add(header);
        AcceptButton = post;
        CancelButton = cancel;
        SetDefaultPrice();
    }

    private static Label FieldLabel(string text) => new() { Text = text, AutoSize = true, Padding = new Padding(0, 8, 0, 0) };

    private void SetDefaultPrice()
    {
        if (_product.SelectedItem is not ProductChoice item) return;
        var value = _kind == "SALE_RETURN" ? item.Price : item.Cost;
        _unitPrice.Value = Math.Min(_unitPrice.Maximum, Math.Max(_unitPrice.Minimum, value));
    }

    private void AddLine(object? sender, EventArgs e)
    {
        try
        {
            if (_product.SelectedItem is not ProductChoice selected)
                throw new InvalidOperationException("أضف أصنافًا إلى دليل الأصناف أولًا.");
            var product = _products.First(p => p.Id == selected.Id);
            var net = AccountingService.ToHalalas(_quantity.Value * _unitPrice.Value);
            if (net <= 0) throw new InvalidOperationException("قيمة السطر يجب أن تكون أكبر من صفر.");
            var vat = AccountingService.ToHalalas(AccountingService.FromHalalas(net) * product.VatRate / 100m);
            var total = checked(net + vat);
            _lines.Add(new ReturnLineInput(product.Id, _quantity.Value, _unitPrice.Value));
            _grid.Rows.Add(product.Name, _quantity.Value.ToString("0.###", CultureInfo.InvariantCulture),
                _unitPrice.Value.ToString("N2", CultureInfo.InvariantCulture),
                AccountingService.FromHalalas(vat).ToString("N2", CultureInfo.InvariantCulture),
                AccountingService.FromHalalas(total).ToString("N2", CultureInfo.InvariantCulture));
            var totalValue = _lines.Sum(line =>
            {
                var p = _products.First(x => x.Id == line.ProductId);
                var unitNet = AccountingService.ToHalalas(line.Quantity * (_kind == "PURCHASE_RETURN" ? p.Cost : line.UnitPrice));
                var lineVat = AccountingService.ToHalalas(AccountingService.FromHalalas(unitNet) * p.VatRate / 100m);
                return AccountingService.FromHalalas(checked(unitNet + lineVat));
            });
            Controls.OfType<FlowLayoutPanel>().SelectMany(p => p.Controls.OfType<Label>())
                .FirstOrDefault(l => l.Name == "returnTotal")!.Text = "الإجمالي: " + totalValue.ToString("N2", CultureInfo.InvariantCulture) + " ر.س";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "سطر المردود", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void Post(object? sender, EventArgs e)
    {
        try
        {
            if (_party.SelectedValue is null) throw new InvalidOperationException("أضف الطرف المناسب واختره أولًا.");
            _returns.CreateReturn(_kind, _date.Value.Date, Convert.ToInt32(_party.SelectedValue), _onAccount.Checked, _lines);
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "تعذر ترحيل المردود", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private sealed record PartyChoice(int Id, string Name) { public string Text => Name; }
    private sealed record ProductChoice(int Id, string Text, decimal Price, decimal Cost);
}