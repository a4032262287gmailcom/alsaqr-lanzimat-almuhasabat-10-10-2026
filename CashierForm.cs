using System.Drawing.Printing;
using System.Data;
using AlSaqr.Accounting.Services;

namespace AlSaqr.Accounting;

internal sealed class CashierForm : Form
{
    private readonly AccountingService _service;
    private readonly SystemSettings _settings;
    private readonly List<ProductInfo> _products;
    private readonly List<CartLine> _cart = new();
    private readonly ComboBox _customer = new() { Width = 280, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox _payment = new() { Width = 175, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly TextBox _barcode = new() { Width = 220 };
    private readonly NumericUpDown _quantity = new() { Width = 100, Minimum = 0.001m, Maximum = 1000000m, DecimalPlaces = 3, Increment = 1, Value = 1 };
    private readonly NumericUpDown _discount = new() { Width = 90, Minimum = 0, Maximum = 100, DecimalPlaces = 2, Increment = 1 };
    private readonly NumericUpDown _tendered = new() { Width = 145, Minimum = 0, Maximum = 1000000000m, DecimalPlaces = 2 };
    private readonly DataGridView _grid = new();
    private readonly Label _totals = new() { Width = 650, Height = 58, TextAlign = ContentAlignment.MiddleRight, Font = new Font("Segoe UI", 10, FontStyle.Bold), Margin = new Padding(8) };

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        var key = keyData & Keys.KeyCode;
        var modifiers = keyData & Keys.Modifiers;

        if (modifiers == Keys.Control && key == Keys.Enter)
        {
            Checkout(forcePrint: true);
            return true;
        }

        if (modifiers == Keys.None)
        {
            switch (key)
            {
                case Keys.F2:
                    _barcode.Focus();
                    _barcode.SelectAll();
                    return true;
                case Keys.F3:
                    _quantity.Focus();
                    _quantity.Select(0, _quantity.Text.Length);
                    return true;
                case Keys.F4:
                    _discount.Focus();
                    _discount.Select(0, _discount.Text.Length);
                    return true;
                case Keys.F6:
                    _payment.Focus();
                    return true;
                case Keys.F7:
                    _tendered.Focus();
                    _tendered.Select(0, _tendered.Text.Length);
                    return true;
                case Keys.F8:
                    _grid.Focus();
                    if (_grid.Rows.Count > 0 && _grid.CurrentCell is null)
                        _grid.CurrentCell = _grid.Rows[0].Cells[0];
                    return true;
                case Keys.F9:
                    Checkout();
                    return true;
                case Keys.Delete when _grid.ContainsFocus:
                    RemoveSelectedLine();
                    return true;
            }
        }

        return base.ProcessCmdKey(ref msg, keyData);
    }

    public CashierForm(AccountingService service, SystemSettings settings)
    {
        _service = service; _settings = settings;
        _products = service.ActiveProducts();
        Text = "الصقر | شاشة الكاشير";
        StartPosition = FormStartPosition.CenterParent;
        WindowState = FormWindowState.Maximized;
        MinimumSize = new Size(1000, 680);
        RightToLeft = RightToLeft.Yes; RightToLeftLayout = true;
        Font = new Font("Segoe UI", 10F);
        BackColor = SystemSettingsService.PaletteFor(settings.Theme).Paper;

        var customers = service.Parties("Customer").Rows.Cast<DataRow>()
            .Where(r => string.Equals(Convert.ToString(r[5]), "نشط", StringComparison.Ordinal))
            .Select(r => new CustomerChoice(Convert.ToInt32(r[0]), Convert.ToString(r[1]) ?? ""))
            .ToList();
        _customer.DataSource = customers;
        _customer.DisplayMember = nameof(CustomerChoice.Name);
        _customer.ValueMember = nameof(CustomerChoice.Id);

        _payment.Items.Add(new PaymentMode("نقدي - الصندوق", false, "1010"));
        _payment.Items.Add(new PaymentMode("شبكة / بنك", false, "1020"));
        _payment.Items.Add(new PaymentMode("آجل - ذمم عميل", true, "1100"));
        _payment.SelectedIndex = 0;

        var top = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 125, FlowDirection = FlowDirection.RightToLeft, WrapContents = true, Padding = new Padding(10), BackColor = Color.FromArgb(18, 32, 52) };
        top.Controls.Add(Field("العميل", _customer));
        top.Controls.Add(Field("رمز الصنف / الباركود", _barcode));
        top.Controls.Add(Field("الكمية", _quantity));
        top.Controls.Add(Field("خصم % للصنف التالي", _discount));
        var add = new Button { Text = "إضافة إلى السلة", Width = 145, Height = 34, Margin = new Padding(6, 26, 6, 4) };
        add.Click += (_, _) => AddScannedProduct();
        top.Controls.Add(add);
        _barcode.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; AddScannedProduct(); } };

        ConfigureGrid();
        var bottom = new TableLayoutPanel { Dock = DockStyle.Bottom, Height = 135, ColumnCount = 2, RowCount = 2, Padding = new Padding(12), BackColor = Color.White };
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 190));
        bottom.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        bottom.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        var paymentPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, WrapContents = true };
        paymentPanel.Controls.Add(Field("طريقة الدفع", _payment));
        paymentPanel.Controls.Add(Field("المبلغ المستلم", _tendered));
        paymentPanel.Controls.Add(_totals);
        var checkout = new Button { Text = "ترحيل البيع وطباعة الإيصال", Dock = DockStyle.Fill, BackColor = Color.FromArgb(190, 151, 61), ForeColor = Color.FromArgb(18, 32, 52), FlatStyle = FlatStyle.Flat };
        var remove = new Button { Text = "حذف السطر المحدد", Dock = DockStyle.Fill };
        remove.Click += (_, _) => RemoveSelectedLine();
        _tendered.ValueChanged += (_, _) => UpdateTotals();
        _payment.SelectedIndexChanged += (_, _) => UpdateTotals();
        checkout.Click += (_, _) => Checkout();

        bottom.Controls.Add(paymentPanel, 0, 0);
        bottom.Controls.Add(checkout, 1, 0);
        bottom.Controls.Add(remove, 1, 1);
        bottom.SetColumnSpan(remove, 1);
        var shortcuts = new Label
        {
            Dock = DockStyle.Bottom,
            Height = 28,
            TextAlign = ContentAlignment.MiddleCenter,
            BackColor = Color.FromArgb(235, 239, 244),
            ForeColor = Color.FromArgb(37, 66, 101),
            Text = "اختصارات: F2 باركود | F3 كمية | F4 خصم | F6 الدفع | F7 المستلم | F8 السلة | Delete حذف السطر | F9 إتمام البيع | Ctrl+Enter بيع وطباعة مباشرة"
        };

        Controls.Add(_grid); Controls.Add(bottom); Controls.Add(top); Controls.Add(shortcuts);
        UpdateTotals();
    }

    private void ConfigureGrid()
    {
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
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Code", HeaderText = "الرمز", FillWeight = 65 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Name", HeaderText = "الصنف", FillWeight = 145 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Qty", HeaderText = "الكمية", FillWeight = 55 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Price", HeaderText = "السعر", FillWeight = 70 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Discount", HeaderText = "الخصم %", FillWeight = 65 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Net", HeaderText = "السعر بعد الخصم", FillWeight = 85 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Tax", HeaderText = "الضريبة", FillWeight = 70 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Total", HeaderText = "الإجمالي", FillWeight = 80 });
        _grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(37, 66, 101);
        _grid.ColumnHeadersDefaultCellStyle.ForeColor = Color.White;
        _grid.EnableHeadersVisualStyles = false;
    }

    private void AddScannedProduct()
    {
        var code = _barcode.Text.Trim();
        if (code.Length == 0) { MessageBox.Show(this, "امسح الباركود أو أدخل رمز الصنف.", "الكاشير", MessageBoxButtons.OK, MessageBoxIcon.Information); _barcode.Focus(); return; }
        var product = _products.FirstOrDefault(p => string.Equals(p.Barcode, code, StringComparison.OrdinalIgnoreCase) || string.Equals(p.Code, code, StringComparison.OrdinalIgnoreCase));
        if (product is null)
        {
            MessageBox.Show(this, "لم يُعثر على صنف بهذا الرمز. في هذه النسخة يستخدم رمز الصنف نفسه كقيمة للباركود.", "صنف غير موجود", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            _barcode.SelectAll(); _barcode.Focus(); return;
        }
        if (_quantity.Value <= 0) { MessageBox.Show(this, "الكمية يجب أن تكون أكبر من صفر."); return; }
        if (product.Quantity < _quantity.Value)
        {
            MessageBox.Show(this, $"الكمية المتاحة من {product.Name}: {product.Quantity:0.###}.", "مخزون غير كافٍ", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        var line = new CartLine(product, _quantity.Value, _discount.Value);
        _cart.Add(line);
        RenderCart();
        _barcode.Clear(); _quantity.Value = 1; _discount.Value = 0; _barcode.Focus();
    }

    private void RemoveSelectedLine()
    {
        if (_grid.CurrentRow is null) return;
        var index = _grid.CurrentRow.Index;
        if (index >= 0 && index < _cart.Count) _cart.RemoveAt(index);
        RenderCart();
    }

    private void RenderCart()
    {
        _grid.Rows.Clear();
        foreach (var line in _cart)
        {
            var netUnit = DiscountedPrice(line.Product.Price, line.DiscountPercent);
            var net = RoundMoney(line.Quantity * netUnit);
            var vat = RoundMoney(net * line.Product.VatRate / 100m);
            _grid.Rows.Add(line.Product.Code, line.Product.Name, line.Quantity.ToString("0.###"),
                line.Product.Price.ToString("N2"), line.DiscountPercent.ToString("N2"),
                netUnit.ToString("N2"), vat.ToString("N2"), (net + vat).ToString("N2"));
        }
        UpdateTotals();
    }

    private void UpdateTotals()
    {
        var gross = _cart.Sum(line => RoundMoney(line.Quantity * line.Product.Price));
        var net = _cart.Sum(line => RoundMoney(line.Quantity * DiscountedPrice(line.Product.Price, line.DiscountPercent)));
        var discount = gross - net;
        var vat = _cart.Sum(line => RoundMoney(RoundMoney(line.Quantity * DiscountedPrice(line.Product.Price, line.DiscountPercent)) * line.Product.VatRate / 100m));
        var total = net + vat;
        var payment = _payment.SelectedItem as PaymentMode;
        var isCredit = payment?.IsCredit ?? false;
        var change = isCredit ? 0m : Math.Max(0m, _tendered.Value - total);
        var due = isCredit ? total : Math.Max(0m, total - _tendered.Value);
        _totals.Text = $"قبل الخصم: {gross:N2} ر.س    الخصم: {discount:N2}    الصافي: {net:N2}    الضريبة: {vat:N2}    الإجمالي: {total:N2} ر.س\nالمتبقي: {due:N2}    الباقي للعميل: {change:N2}";
    }

    private void Checkout(bool forcePrint = false)
    {
        if (_cart.Count == 0) { MessageBox.Show(this, "أضف صنفًا واحدًا على الأقل.", "الكاشير", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
        if (_customer.SelectedItem is not CustomerChoice customer) { MessageBox.Show(this, "اختر عميلًا نشطًا. يمكن إنشاء عميل نقدي من شاشة العملاء والموردين.", "الكاشير", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
        var payment = _payment.SelectedItem as PaymentMode;
        if (payment is null) { MessageBox.Show(this, "اختر طريقة الدفع."); return; }

        var net = _cart.Sum(line => RoundMoney(line.Quantity * DiscountedPrice(line.Product.Price, line.DiscountPercent)));
        var vat = _cart.Sum(line => RoundMoney(RoundMoney(line.Quantity * DiscountedPrice(line.Product.Price, line.DiscountPercent)) * line.Product.VatRate / 100m));
        var total = net + vat;
        if (!payment.IsCredit && _tendered.Value < total)
        {
            MessageBox.Show(this, $"المبلغ المستلم أقل من إجمالي الفاتورة ({total:N2} ر.س).", "مبلغ غير كافٍ", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        try
        {
            var inputLines = _cart.Select(line => new InvoiceLineInput(
                line.Product.Id, line.Quantity, DiscountedPrice(line.Product.Price, line.DiscountPercent))).ToList();
            var invoiceId = _service.CreateInvoice("SALE", DateTime.Today, customer.Id, payment.IsCredit, inputLines, payment.AccountCode);
            var receipt = _service.GetInvoiceReceiptInfo(invoiceId);
            var completedLines = _cart.ToList();
            var gross = completedLines.Sum(line => RoundMoney(line.Quantity * line.Product.Price));
            var discount = gross - net;
            var shouldPrint = forcePrint || _settings.AutoPrintReceipt ||
                MessageBox.Show(this, "تم ترحيل الفاتورة بنجاح. هل تريد طباعة الإيصال الآن؟", "تم البيع", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;
            if (shouldPrint)
            {
                try { PrintReceipt(receipt, completedLines, gross, discount, payment, _tendered.Value); }
                catch (Exception ex) { MessageBox.Show(this, "تم ترحيل الفاتورة، لكن تعذرت الطباعة. يمكنك طباعة نسخة من سجل فواتير البيع.\n" + ex.Message, "تعذر الطباعة", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
            }
            MessageBox.Show(this, $"تم ترحيل الفاتورة {receipt.Number} وتسجيل أثرها المحاسبي والمخزني.\nالإجمالي: {receipt.Total:N2} ر.س", "نجاح البيع", MessageBoxButtons.OK, MessageBoxIcon.Information);
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "تعذر ترحيل البيع", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void PrintReceipt(InvoiceReceiptInfo receipt, IReadOnlyList<CartLine> lines, decimal gross, decimal discount, PaymentMode payment, decimal tendered)
    {
        var palette = SystemSettingsService.PaletteFor(_settings.Theme);
        var content = new List<string> { _settings.CompanyName };
        if (!string.IsNullOrWhiteSpace(_settings.Address)) content.Add(_settings.Address);
        if (!string.IsNullOrWhiteSpace(_settings.Phone)) content.Add("هاتف: " + _settings.Phone);
        if (!string.IsNullOrWhiteSpace(_settings.VatNumber)) content.Add("الرقم الضريبي: " + _settings.VatNumber);
        if (!string.IsNullOrWhiteSpace(_settings.CommercialRegistration)) content.Add("السجل التجاري: " + _settings.CommercialRegistration);
        content.Add("-------------------------------");
        content.Add("فاتورة بيع: " + receipt.Number);
        content.Add("التاريخ: " + receipt.Date);
        content.Add("العميل: " + receipt.PartyName);
        content.Add("-------------------------------");
        foreach (var line in lines)
        {
            var netUnit = DiscountedPrice(line.Product.Price, line.DiscountPercent);
            var net = RoundMoney(line.Quantity * netUnit);
            var tax = RoundMoney(net * line.Product.VatRate / 100m);
            content.Add(line.Product.Name);
            content.Add($"{line.Quantity:0.###} × {line.Product.Price:N2}  خصم {line.DiscountPercent:N2}%");
            content.Add($"الصافي {net:N2} + ضريبة {tax:N2} = {net + tax:N2}");
        }
        content.Add("-------------------------------");
        content.Add("قبل الخصم: " + gross.ToString("N2") + " ر.س");
        content.Add("الخصم: " + discount.ToString("N2") + " ر.س");
        content.Add("صافي الفاتورة: " + receipt.Subtotal.ToString("N2") + " ر.س");
        content.Add("ضريبة القيمة المضافة: " + receipt.Vat.ToString("N2") + " ر.س");
        content.Add("الإجمالي: " + receipt.Total.ToString("N2") + " ر.س");
        content.Add("طريقة الدفع: " + payment.Label);
        if (!payment.IsCredit)
        {
            content.Add("المبلغ المستلم: " + tendered.ToString("N2") + " ر.س");
            content.Add("الباقي: " + Math.Max(0m, tendered - receipt.Total).ToString("N2") + " ر.س");
        }
        if (!string.IsNullOrWhiteSpace(_settings.ReceiptFooter)) content.Add(_settings.ReceiptFooter);

        using var document = new PrintDocument();
        if (!string.IsNullOrWhiteSpace(_settings.PrinterName)) document.PrinterSettings.PrinterName = _settings.PrinterName;
        if (!document.PrinterSettings.IsValid) throw new InvalidOperationException("الطابعة المحددة غير متاحة. اختر الطابعة من إعدادات النظام.");
        var index = 0;
        document.PrintPage += (_, e) =>
        {
            using var font = new Font("Segoe UI", 9F);
            using var format = new StringFormat { Alignment = StringAlignment.Far, LineAlignment = StringAlignment.Near, FormatFlags = StringFormatFlags.DirectionRightToLeft };
            var graphics = e.Graphics;
            if (graphics is null) { e.HasMorePages = false; return; }
            float y = e.MarginBounds.Top;
            var lineHeight = font.GetHeight(graphics) + 5;
            while (index < content.Count && y + lineHeight <= e.MarginBounds.Bottom)
            {
                graphics.DrawString(content[index++], font, Brushes.Black,
                    new RectangleF(e.MarginBounds.Left, y, e.MarginBounds.Width, lineHeight + 2), format);
                y += lineHeight;
            }
            e.HasMorePages = index < content.Count;
        };
        document.Print();
    }

    private static Control Field(string caption, Control control)
    {
        var panel = new Panel { Width = Math.Max(165, control.Width + 12), Height = 68, Margin = new Padding(6, 2, 6, 2) };
        panel.Controls.Add(new Label { Text = caption, Dock = DockStyle.Top, Height = 27, ForeColor = Color.White, TextAlign = ContentAlignment.MiddleRight });
        control.Dock = DockStyle.Bottom;
        panel.Controls.Add(control);
        return panel;
    }

    private static decimal DiscountedPrice(decimal price, decimal discountPercent) =>
        decimal.Round(price * (100m - discountPercent) / 100m, 2, MidpointRounding.AwayFromZero);

    private static decimal RoundMoney(decimal value) => decimal.Round(value, 2, MidpointRounding.AwayFromZero);

    private sealed record CartLine(ProductInfo Product, decimal Quantity, decimal DiscountPercent);
    private sealed record CustomerChoice(int Id, string Name);
    private sealed record PaymentMode(string Label, bool IsCredit, string AccountCode)
    {
        public override string ToString() => Label;
    }
}
