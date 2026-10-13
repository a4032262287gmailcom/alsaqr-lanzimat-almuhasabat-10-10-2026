using System.Data;
using System.Globalization;
using AlSaqr.Accounting.Services;

namespace AlSaqr.Accounting;

internal sealed class ReturnEntryForm : Form
{
    private readonly ReturnService _returns;
    private readonly string _kind;
    private readonly ComboBox _party = new() { Width = 300, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox _invoice = new() { Width = 330, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox _productLine = new() { Width = 430, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly NumericUpDown _quantity = new() { Width = 125, Minimum = 0.001m, Maximum = 10000000m, DecimalPlaces = 3, Value = 1m, Increment = 1m, ThousandsSeparator = true };
    private readonly DateTimePicker _date = new() { Width = 150, Format = DateTimePickerFormat.Custom, CustomFormat = "yyyy-MM-dd", Value = DateTime.Today };
    private readonly CheckBox _onAccount = new() { AutoSize = true, Checked = true };
    private readonly DataGridView _grid = new();
    private readonly Label _total = new() { Name = "returnTotal", Text = "الإجمالي: 0.00 ر.س", Width = 280, Height = 38, TextAlign = ContentAlignment.MiddleRight, Font = new Font("Segoe UI", 12F, FontStyle.Bold), ForeColor = Color.FromArgb(18, 32, 52) };
    private readonly List<ProductReturnChoice> _productLines = new();
    private readonly List<ReturnLineInput> _lines = new();
    private bool _bindingParty;
    private bool _bindingInvoice;

    public ReturnEntryForm(AccountingService accounting, ReturnService returns, string kind)
    {
        if (kind is not ("SALE_RETURN" or "PURCHASE_RETURN")) throw new ArgumentException("نوع المردود غير صالح.");
        _returns = returns;
        _kind = kind;
        Text = kind == "SALE_RETURN" ? "مردود بيع / إشعار دائن" : "مردود شراء";
        Width = 1120;
        Height = 720;
        MinimumSize = new Size(950, 620);
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
        _bindingParty = true;
        _party.DataSource = parties;
        _bindingParty = false;
        _party.SelectedIndexChanged += (_, _) => LoadInvoices();

        _invoice.DisplayMember = nameof(InvoiceChoice.Text);
        _invoice.ValueMember = nameof(InvoiceChoice.Id);
        _invoice.SelectedIndexChanged += (_, _) => LoadReturnableLines();

        _productLine.DisplayMember = nameof(ProductReturnChoice.Text);
        _productLine.ValueMember = nameof(ProductReturnChoice.SourceInvoiceLineId);

        _onAccount.Text = kind == "SALE_RETURN"
            ? "تسوية على حساب العميل (أزل العلامة لردّ المبلغ نقدًا)"
            : "تسوية على حساب المورد (أزل العلامة لاسترداد المبلغ نقدًا)";

        var header = new Label
        {
            Text = kind == "SALE_RETURN"
                ? "مردود البيع مرتبط بالفاتورة الأصلية — سيُعكس الإيراد والضريبة والتكلفة وتُعاد الكمية للمخزون"
                : "مردود الشراء مرتبط بالفاتورة الأصلية — سيُخفض رصيد المورد والضريبة ويُحدّث المخزون",
            Dock = DockStyle.Top,
            Height = 55,
            Padding = new Padding(12, 0, 12, 0),
            TextAlign = ContentAlignment.MiddleRight,
            Font = new Font("Segoe UI", 12F, FontStyle.Bold),
            ForeColor = Color.FromArgb(18, 32, 52)
        };

        var top = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 190,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = true,
            Padding = new Padding(10),
            BackColor = Color.White
        };
        top.Controls.Add(FieldLabel("العميل / المورد")); top.Controls.Add(_party);
        top.Controls.Add(FieldLabel("الفاتورة الأصلية")); top.Controls.Add(_invoice);
        top.Controls.Add(FieldLabel("تاريخ المردود")); top.Controls.Add(_date);
        top.Controls.Add(_onAccount);
        top.Controls.Add(FieldLabel("سطر الفاتورة / الصنف")); top.Controls.Add(_productLine);
        top.Controls.Add(FieldLabel("الكمية المرتجعة")); top.Controls.Add(_quantity);
        var add = new Button { Text = "إضافة سطر", Width = 120, Height = 34, BackColor = Color.FromArgb(37, 66, 101), ForeColor = Color.White, FlatStyle = FlatStyle.Flat };
        add.Click += AddLine;
        var remove = new Button { Text = "حذف السطر المحدد", Width = 145, Height = 34 };
        remove.Click += RemoveLine;
        top.Controls.Add(add);
        top.Controls.Add(remove);

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
        _grid.Columns.Add("sourceLine", "سطر المصدر");
        _grid.Columns["sourceLine"]!.Visible = false;
        _grid.Columns.Add("product", "الصنف");
        _grid.Columns.Add("qty", "الكمية");
        _grid.Columns.Add("price", "سعر الوحدة من الفاتورة");
        _grid.Columns.Add("vat", "الضريبة");
        _grid.Columns.Add("total", "إجمالي السطر");

        var footer = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 62, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(8), BackColor = Color.White };
        var post = new Button { Text = "ترحيل المردود", Width = 150, Height = 36, BackColor = Color.FromArgb(18, 32, 52), ForeColor = Color.White, FlatStyle = FlatStyle.Flat };
        var cancel = new Button { Text = "إلغاء", Width = 100, Height = 36, DialogResult = DialogResult.Cancel };
        post.Click += Post;
        footer.Controls.Add(post); footer.Controls.Add(cancel); footer.Controls.Add(_total);
        Controls.Add(_grid); Controls.Add(footer); Controls.Add(top); Controls.Add(header);
        AcceptButton = post;
        CancelButton = cancel;

        if (_party.Items.Count > 0) LoadInvoices();
        else SetStatusLabel("لا توجد أطراف نشطة مناسبة. أضف العميل أو المورد أولًا.");
    }

    private static Label FieldLabel(string text) =>
        new() { Text = text, AutoSize = true, Padding = new Padding(0, 8, 0, 0) };

    private void SetStatusLabel(string text)
    {
        _total.Text = text;
        _total.Width = 430;
    }

    private void LoadInvoices()
    {
        if (_bindingParty) return;
        _bindingInvoice = true;
        try
        {
            _productLines.Clear();
            _productLine.DataSource = null;
            _lines.Clear();
            _grid.Rows.Clear();
            UpdateTotal();

            if (_party.SelectedValue is null)
            {
                _invoice.DataSource = new List<InvoiceChoice>();
                return;
            }

            var table = _returns.ReturnableInvoices(_kind, Convert.ToInt32(_party.SelectedValue, CultureInfo.InvariantCulture));
            var choices = table.Rows.Cast<DataRow>()
                .Select(r => new InvoiceChoice(
                    Convert.ToInt64(r["المعرف"], CultureInfo.InvariantCulture),
                    Convert.ToString(r["رقم الفاتورة"]) ?? "",
                    Convert.ToString(r["التاريخ"]) ?? "",
                    Convert.ToDecimal(r["إجمالي الفاتورة"], CultureInfo.InvariantCulture),
                    Convert.ToString(r["طريقة السداد"]) ?? ""))
                .ToList();
            _invoice.DataSource = choices;
            if (choices.Count == 0) SetStatusLabel("لا توجد فواتير أصلية متاحة للمردود لهذا الطرف.");
            else _total.Width = 280;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "تحميل الفواتير القابلة للمردود", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            _bindingInvoice = false;
        }
        LoadReturnableLines();
    }

    private void LoadReturnableLines()
    {
        if (_bindingInvoice || _invoice.SelectedValue is null) return;
        try
        {
            _productLines.Clear();
            _productLine.DataSource = null;
            _lines.Clear();
            _grid.Rows.Clear();
            UpdateTotal();

            var table = _returns.ReturnableLines(_kind, Convert.ToInt64(_invoice.SelectedValue, CultureInfo.InvariantCulture));
            foreach (DataRow row in table.Rows)
            {
                _productLines.Add(new ProductReturnChoice(
                    Convert.ToInt64(row["SourceInvoiceLineId"], CultureInfo.InvariantCulture),
                    Convert.ToInt32(row["ProductId"], CultureInfo.InvariantCulture),
                    Convert.ToString(row["رمز الصنف"]) ?? "",
                    Convert.ToString(row["الصنف"]) ?? "",
                    Convert.ToDecimal(row["الكمية المتاحة للمردود"], CultureInfo.InvariantCulture),
                    Convert.ToDecimal(row["سعر الوحدة الأصلي"], CultureInfo.InvariantCulture),
                    Convert.ToDecimal(row["الضريبة %"], CultureInfo.InvariantCulture),
                    Convert.ToDecimal(row["التكلفة وقت الفاتورة"], CultureInfo.InvariantCulture),
                    Convert.ToDecimal(row["متوسط التكلفة الحالي"], CultureInfo.InvariantCulture),
                    Convert.ToDecimal(row["المخزون الحالي"], CultureInfo.InvariantCulture)));
            }
            _productLine.DataSource = _productLines.ToList();
            _productLine.DisplayMember = nameof(ProductReturnChoice.Text);
            _productLine.ValueMember = nameof(ProductReturnChoice.SourceInvoiceLineId);
            if (_productLines.Count == 0) SetStatusLabel("لا توجد كميات متبقية في الفاتورة المحددة.");
            else _total.Width = 280;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "تحميل تفاصيل الفاتورة", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void AddLine(object? sender, EventArgs e)
    {
        try
        {
            if (_invoice.SelectedValue is null) throw new InvalidOperationException("حدد الفاتورة الأصلية أولًا.");
            if (_productLine.SelectedItem is not ProductReturnChoice selected)
                throw new InvalidOperationException("لا يوجد سطر فاتورة متاح للإرجاع.");
            if (_lines.Any(x => x.SourceInvoiceLineId == selected.SourceInvoiceLineId))
                throw new InvalidOperationException("أُضيف سطر الفاتورة بالفعل. احذفه من الجدول قبل إضافته مرة أخرى.");
            var remaining = selected.AvailableQuantity;
            if (_quantity.Value > remaining + 0.0000001m)
                throw new InvalidOperationException($"الكمية المتاحة في هذا السطر بعد احتساب الأسطر المضافة: {Math.Max(remaining, 0):0.###}.");
            var net = AccountingService.ToHalalas(_quantity.Value * selected.UnitPrice);
            var vat = AccountingService.ToHalalas(AccountingService.FromHalalas(net) * selected.VatRate / 100m);
            var total = checked(net + vat);
            if (total <= 0) throw new InvalidOperationException("قيمة المردود يجب أن تكون أكبر من صفر.");
            _lines.Add(new ReturnLineInput(selected.SourceInvoiceLineId, selected.ProductId, _quantity.Value));
            _grid.Rows.Add(selected.SourceInvoiceLineId, selected.Name, _quantity.Value.ToString("0.###", CultureInfo.InvariantCulture),
                selected.UnitPrice.ToString("N2", CultureInfo.InvariantCulture),
                AccountingService.FromHalalas(vat).ToString("N2", CultureInfo.InvariantCulture),
                AccountingService.FromHalalas(total).ToString("N2", CultureInfo.InvariantCulture));
            UpdateTotal();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "سطر المردود", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void RemoveLine(object? sender, EventArgs e)
    {
        if (_grid.CurrentRow is null || _grid.CurrentRow.IsNewRow) return;
        var sourceId = Convert.ToInt64(_grid.CurrentRow.Cells["sourceLine"].Value, CultureInfo.InvariantCulture);
        var index = _lines.FindIndex(x => x.SourceInvoiceLineId == sourceId);
        if (index >= 0) _lines.RemoveAt(index);
        _grid.Rows.Remove(_grid.CurrentRow);
        UpdateTotal();
    }

    private void UpdateTotal()
    {
        decimal total = 0m;
        foreach (var line in _lines)
        {
            var source = _productLines.FirstOrDefault(x => x.SourceInvoiceLineId == line.SourceInvoiceLineId);
            if (source is null) continue;
            var net = AccountingService.ToHalalas(line.Quantity * source.UnitPrice);
            var vat = AccountingService.ToHalalas(AccountingService.FromHalalas(net) * source.VatRate / 100m);
            total += AccountingService.FromHalalas(checked(net + vat));
        }
        _total.Text = "الإجمالي: " + total.ToString("N2", CultureInfo.InvariantCulture) + " ر.س";
        _total.Width = 280;
    }

    private void Post(object? sender, EventArgs e)
    {
        try
        {
            if (_invoice.SelectedValue is null) throw new InvalidOperationException("حدد الفاتورة الأصلية.");
            _returns.CreateReturn(_kind, _date.Value.Date,
                Convert.ToInt64(_invoice.SelectedValue, CultureInfo.InvariantCulture), _onAccount.Checked, _lines);
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "تعذر ترحيل المردود", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private sealed record PartyChoice(int Id, string Name) { public string Text => Name; }
    private sealed record InvoiceChoice(long Id, string Number, string Date, decimal Total, string Payment)
    {
        public string Text => $"{Number} — {Date} — {Total:N2} ر.س ({Payment})";
    }
    private sealed record ProductReturnChoice(long SourceInvoiceLineId, int ProductId, string Code, string Name,
        decimal AvailableQuantity, decimal UnitPrice, decimal VatRate, decimal OriginalCost, decimal CurrentCost, decimal StockQuantity)
    {
        public string Text => $"{Code} - {Name} | المتاح: {AvailableQuantity:0.###} | السعر: {UnitPrice:N2}";
    }
}