using System.Data;
using System.Drawing.Printing;
using System.Globalization;
using AlSaqr.Accounting.Services;

namespace AlSaqr.Accounting.Forms;

internal sealed class CashierForm : Form
{
    private sealed class CartLine
    {
        public CashierProduct Product { get; }
        public decimal Quantity { get; set; }
        public decimal UnitPrice { get; set; }
        public CartLine(CashierProduct product, decimal quantity, decimal unitPrice)
        {
            Product = product;
            Quantity = quantity;
            UnitPrice = unitPrice;
        }
    }

    private readonly CashierService _cashier = new();
    private readonly LicenseService _licensing = new();
    private readonly AuthUser _user;
    private readonly List<CartLine> _cart = new();
    private readonly List<CashierProduct> _searchResults = new();
    private readonly DataGridView _productsGrid = new();
    private readonly DataGridView _cartGrid = new();
    private readonly TextBox _search = new() { Width = 260, PlaceholderText = "اسم الصنف أو الرمز أو الباركود" };
    private readonly TextBox _barcode = new() { Width = 190, PlaceholderText = "امسح الباركود ثم Enter" };
    private readonly NumericUpDown _quantity = new() { Width = 90, Minimum = 0.001m, Maximum = 10000000m, DecimalPlaces = 3, Value = 1m, Increment = 1m };
    private readonly NumericUpDown _unitPrice = new() { Width = 120, Minimum = 0m, Maximum = 100000000m, DecimalPlaces = 2 };
    private readonly NumericUpDown _discount = new() { Width = 85, Minimum = 0m, Maximum = 99.99m, DecimalPlaces = 2, Increment = 1m };
    private readonly NumericUpDown _tendered = new() { Width = 145, Minimum = 0m, Maximum = 100000000m, DecimalPlaces = 2 };
    private readonly ComboBox _customer = new() { Width = 230, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox _payment = new() { Width = 140, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly Label _subtotalLabel = new() { Width = 180, Height = 30 };
    private readonly Label _discountLabel = new() { Width = 145, Height = 30 };
    private readonly Label _vatLabel = new() { Width = 145, Height = 30 };
    private readonly Label _totalLabel = new() { Width = 210, Height = 36, Font = new Font("Segoe UI", 15F, FontStyle.Bold) };
    private readonly Label _changeLabel = new() { Width = 180, Height = 30 };
    private readonly Label _status = new() { Dock = DockStyle.Bottom, Height = 27, TextAlign = ContentAlignment.MiddleRight };
    private readonly Label _sessionStatus = new() { Width = 390, Height = 30, TextAlign = ContentAlignment.MiddleRight };
    private CashierSessionSummary? _activeSession;
    private Button? _sessionButton;
    private Button? _checkoutButton;
    private int _cashCustomerId;
    private CashReceipt? _lastReceipt;
    private PrintDocument? _printDocument;
    private int _printLineIndex;

    private AccountingAppSettings _settings = AccountingAppSettings.Defaults();
    private UiThemePalette _palette = UiThemePalette.All[0];
    private Color _navy = Color.FromArgb(18, 32, 52);
    private Color _blue = Color.FromArgb(37, 66, 101);
    private Color _gold = Color.FromArgb(190, 151, 61);
    private Color _paper = Color.FromArgb(244, 246, 249);

    public CashierForm(AuthUser user)
    {
        _user = user;
        _settings = AppSettingsService.Load();
        _palette = AppSettingsService.GetPalette(_settings.Theme);
        _navy = Color.FromArgb(_palette.PrimaryR, _palette.PrimaryG, _palette.PrimaryB);
        _blue = Color.FromArgb(_palette.SecondaryR, _palette.SecondaryG, _palette.SecondaryB);
        _gold = Color.FromArgb(_palette.AccentR, _palette.AccentG, _palette.AccentB);
        _paper = Color.FromArgb(_palette.BackgroundR, _palette.BackgroundG, _palette.BackgroundB);
        Text = "خالد للأنظمة المحاسبية | شاشة الكاشير";
        WindowState = FormWindowState.Maximized;
        MinimumSize = new Size(1050, 700);
        StartPosition = FormStartPosition.CenterParent;
        RightToLeft = RightToLeft.Yes;
        RightToLeftLayout = true;
        Font = new Font("Segoe UI", 10F);
        BackColor = _paper;
        _status.BackColor = Color.FromArgb(_palette.SurfaceR, _palette.SurfaceG, _palette.SurfaceB);
        _status.ForeColor = Color.FromArgb(_palette.ForegroundR, _palette.ForegroundG, _palette.ForegroundB);
        _subtotalLabel.ForeColor = _discountLabel.ForeColor = _vatLabel.ForeColor =
            _totalLabel.ForeColor = _changeLabel.ForeColor =
            Color.FromArgb(_palette.ForegroundR, _palette.ForegroundG, _palette.ForegroundB);

        BuildLayout();
        LoadReferenceData();
        RefreshSessionUi();
        RefreshProducts();
        RefreshCart();
        UpdateTotals();
        _barcode.Focus();
    }

    private void BuildLayout()
    {
        var header = new Panel { Dock = DockStyle.Top, Height = 56, BackColor = _navy, Padding = new Padding(14, 4, 14, 4) };
        header.Controls.Add(new Label
        {
            Text = "خالد للأنظمة المحاسبية | نقطة البيع والكاشير",
            Dock = DockStyle.Right, Width = 520, ForeColor = Color.White,
            Font = new Font("Segoe UI", 16F, FontStyle.Bold), TextAlign = ContentAlignment.MiddleRight
        });
        header.Controls.Add(new Label
        {
            Text = "المستخدم: " + _user.UserName,
            Dock = DockStyle.Left, Width = 220, ForeColor = Color.FromArgb(235, 220, 177),
            TextAlign = ContentAlignment.MiddleLeft
        });

        var scanPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Top, Height = 108, FlowDirection = FlowDirection.RightToLeft,
            WrapContents = true, Padding = new Padding(8), BackColor = Color.FromArgb(_palette.SurfaceR, _palette.SurfaceG, _palette.SurfaceB)
        };
        scanPanel.Controls.Add(FieldLabel("الباركود")); scanPanel.Controls.Add(_barcode);
        scanPanel.Controls.Add(FieldLabel("بحث الصنف")); scanPanel.Controls.Add(_search);
        scanPanel.Controls.Add(FieldLabel("الكمية")); scanPanel.Controls.Add(_quantity);
        scanPanel.Controls.Add(FieldLabel("سعر الوحدة")); scanPanel.Controls.Add(_unitPrice);
        scanPanel.Controls.Add(MakeButton("إضافة للسلة", AddSelectedProduct, true));
        scanPanel.Controls.Add(MakeButton("تحديث الأصناف", RefreshProducts, false));
        _sessionButton = MakeButton("فتح الوردية", ToggleCashierSession, false);
        scanPanel.Controls.Add(_sessionButton);
        _sessionStatus.ForeColor = Color.FromArgb(_palette.ForegroundR, _palette.ForegroundG, _palette.ForegroundB);
        scanPanel.Controls.Add(_sessionStatus);

        _barcode.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; ScanBarcode(); }
        };
        _search.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                RefreshProducts();
                if (_searchResults.Count == 1) { SelectProduct(_searchResults[0]); AddProductToCart(_searchResults[0]); }
            }
        };
        _search.TextChanged += (_, _) => RefreshProducts();
        _productsGrid.DoubleClick += (_, _) => AddSelectedProduct();
        _productsGrid.SelectionChanged += (_, _) =>
        {
            var selected = SelectedProduct();
            if (selected is not null)
                _unitPrice.Value = Math.Min(_unitPrice.Maximum, Math.Max(_unitPrice.Minimum, selected.Price));
        };

        var split = new SplitContainer
        {
            Dock = DockStyle.Fill, Orientation = Orientation.Vertical, SplitterWidth = 7,
            SplitterDistance = 440, BackColor = _paper, RightToLeft = RightToLeft.Yes
        };
        ConfigureGrid(_productsGrid);
        ConfigureGrid(_cartGrid);
        split.Panel1.Padding = new Padding(4);
        split.Panel2.Padding = new Padding(4);
        split.Panel1.Controls.Add(_productsGrid);
        split.Panel2.Controls.Add(_cartGrid);

        var leftPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(6), BackColor = Color.FromArgb(_palette.SurfaceR, _palette.SurfaceG, _palette.SurfaceB) };
        var productTitle = SectionTitle("الأصناف المتاحة — انقر مرتين للإضافة");
        leftPanel.Controls.Add(_productsGrid);
        leftPanel.Controls.Add(productTitle);
        split.Panel1.Controls.Clear();
        split.Panel1.Controls.Add(leftPanel);

        var cartPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(6), BackColor = Color.FromArgb(_palette.SurfaceR, _palette.SurfaceG, _palette.SurfaceB) };
        var cartTools = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 48, FlowDirection = FlowDirection.RightToLeft, WrapContents = false };
        cartTools.Controls.Add(MakeButton("+ زيادة", () => AdjustSelectedQuantity(1m), false));
        cartTools.Controls.Add(MakeButton("− تقليل", () => AdjustSelectedQuantity(-1m), false));
        cartTools.Controls.Add(MakeButton("حذف السطر", RemoveSelectedLine, false));
        cartTools.Controls.Add(MakeButton("تفريغ السلة", ClearCart, false));
        cartPanel.Controls.Add(_cartGrid);
        cartPanel.Controls.Add(cartTools);
        cartPanel.Controls.Add(SectionTitle("سلة المبيعات"));
        split.Panel2.Controls.Clear();
        split.Panel2.Controls.Add(cartPanel);

        var checkout = new TableLayoutPanel
        {
            Dock = DockStyle.Bottom, Height = 154, ColumnCount = 1, RowCount = 2,
            BackColor = Color.FromArgb(_palette.SurfaceR, _palette.SurfaceG, _palette.SurfaceB), Padding = new Padding(8)
        };
        checkout.RowStyles.Add(new RowStyle(SizeType.Percent, 48));
        checkout.RowStyles.Add(new RowStyle(SizeType.Percent, 52));
        var totals = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, WrapContents = true, Padding = new Padding(4) };
        totals.Controls.Add(_subtotalLabel);
        totals.Controls.Add(_discountLabel);
        totals.Controls.Add(_vatLabel);
        totals.Controls.Add(_totalLabel);
        totals.Controls.Add(new Label { Text = "خصم %", AutoSize = true, Padding = new Padding(0, 7, 0, 0) });
        totals.Controls.Add(_discount);
        var payment = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, WrapContents = true, Padding = new Padding(4) };
        payment.Controls.Add(FieldLabel("العميل")); payment.Controls.Add(_customer);
        payment.Controls.Add(FieldLabel("السداد")); payment.Controls.Add(_payment);
        payment.Controls.Add(FieldLabel("المبلغ المستلم")); payment.Controls.Add(_tendered);
        payment.Controls.Add(_changeLabel);
        _checkoutButton = MakeButton("إتمام البيع وطباعة الفاتورة", Checkout, true);
        _checkoutButton.Enabled = false;
        payment.Controls.Add(_checkoutButton);
        payment.Controls.Add(MakeButton("إغلاق الكاشير", Close, false));
        checkout.Controls.Add(totals, 0, 0);
        checkout.Controls.Add(payment, 0, 1);

        _discount.ValueChanged += (_, _) => UpdateTotals();
        _tendered.ValueChanged += (_, _) => UpdateTotals();
        _payment.SelectedIndexChanged += (_, _) => PaymentChanged();
        _customer.SelectedIndexChanged += (_, _) => PaymentChanged();

        Controls.Add(split);
        Controls.Add(checkout);
        Controls.Add(scanPanel);
        Controls.Add(header);
        Controls.Add(_status);
        _status.Text = "جاهز — المبيعات المعتمدة تُرحّل محاسبيًا وتحدّث المخزون ضمن معاملة واحدة.";
    }

    private Label FieldLabel(string text) => new()
    {
        Text = text, AutoSize = true, Padding = new Padding(0, 7, 0, 0),
        ForeColor = Color.FromArgb(_palette.ForegroundR, _palette.ForegroundG, _palette.ForegroundB)
    };

    private Label SectionTitle(string text) => new()
    {
        Text = text, Dock = DockStyle.Top, Height = 35, ForeColor = Color.FromArgb(_palette.ForegroundR, _palette.ForegroundG, _palette.ForegroundB),
        BackColor = Color.FromArgb(_palette.SurfaceR, _palette.SurfaceG, _palette.SurfaceB), Font = new Font("Segoe UI", 11F, FontStyle.Bold),
        TextAlign = ContentAlignment.MiddleRight
    };

    private Button MakeButton(string text, Action action, bool prominent)
    {
        var button = new Button
        {
            Text = text, Height = 35, Width = Math.Max(105, TextRenderer.MeasureText(text, Font).Width + 25),
            BackColor = prominent ? _navy : _blue, ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand, Margin = new Padding(3)
        };
        button.FlatAppearance.BorderColor = _gold;
        button.Click += (_, _) => Safe(action);
        return button;
    }

    private void ConfigureGrid(DataGridView grid)
    {
        var surface = Color.FromArgb(_palette.SurfaceR, _palette.SurfaceG, _palette.SurfaceB);
        var foreground = Color.FromArgb(_palette.ForegroundR, _palette.ForegroundG, _palette.ForegroundB);
        grid.Dock = DockStyle.Fill;
        grid.ReadOnly = true;
        grid.AllowUserToAddRows = false;
        grid.AllowUserToDeleteRows = false;
        grid.MultiSelect = false;
        grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        grid.BackgroundColor = surface;
        grid.BorderStyle = BorderStyle.None;
        grid.RowHeadersVisible = false;
        grid.RightToLeft = RightToLeft.Yes;
        grid.EnableHeadersVisualStyles = false;
        grid.ColumnHeadersDefaultCellStyle.BackColor = _navy;
        grid.ColumnHeadersDefaultCellStyle.ForeColor = Color.White;
        grid.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
        grid.DefaultCellStyle.BackColor = surface;
        grid.DefaultCellStyle.ForeColor = foreground;
        grid.DefaultCellStyle.SelectionBackColor = _gold;
        grid.DefaultCellStyle.SelectionForeColor = Color.White;
        grid.AlternatingRowsDefaultCellStyle.BackColor = _paper;
    }

    private void LoadReferenceData()
    {
        _cashCustomerId = _cashier.EnsureCashCustomer();
        _activeSession = _cashier.GetActiveSession(_user.UserName);
        _customer.DisplayMember = nameof(CashierCustomer.Name);
        _customer.ValueMember = nameof(CashierCustomer.Id);
        _customer.DataSource = _cashier.Customers();
        _customer.SelectedValue = _cashCustomerId;
        _payment.DisplayMember = "Text";
        _payment.ValueMember = "Value";
        _payment.DataSource = new[]
        {
            new PaymentChoice("CASH", "نقدي"),
            new PaymentChoice("BANK", "شبكة / بنك"),
            new PaymentChoice("CREDIT", "آجل")
        };
        _payment.SelectedValue = "CASH";
        _tendered.Value = 0;
    }

    private void RefreshProducts()
    {
        var oldId = _productsGrid.CurrentRow is null ? -1 :
            Convert.ToInt32(_productsGrid.CurrentRow.Cells["Id"].Value, CultureInfo.InvariantCulture);
        _searchResults.Clear();
        _searchResults.AddRange(_cashier.SearchProducts(_search.Text));
        var table = new DataTable();
        table.Columns.Add("Id", typeof(int));
        table.Columns.Add("رمز الصنف", typeof(string));
        table.Columns.Add("الصنف", typeof(string));
        table.Columns.Add("الوحدة", typeof(string));
        table.Columns.Add("المتوفر", typeof(decimal));
        table.Columns.Add("السعر", typeof(decimal));
        table.Columns.Add("الضريبة %", typeof(decimal));
        table.Columns.Add("Barcode", typeof(string));
        foreach (var product in _searchResults)
            table.Rows.Add(product.Id, product.Code, product.Name, product.Unit, product.Quantity, product.Price, product.VatRate, product.Barcode);
        _productsGrid.DataSource = table;
        _productsGrid.Columns["Id"].Visible = false;
        _productsGrid.Columns["Barcode"].Visible = false;
        _productsGrid.Columns["Id"].HeaderText = "المعرف";
        if (oldId > 0)
            foreach (DataGridViewRow row in _productsGrid.Rows)
                if (Convert.ToInt32(row.Cells["Id"].Value, CultureInfo.InvariantCulture) == oldId) { row.Selected = true; break; }
    }

    private CashierProduct? SelectedProduct()
    {
        if (_productsGrid.CurrentRow is null) return null;
        var id = Convert.ToInt32(_productsGrid.CurrentRow.Cells["Id"].Value, CultureInfo.InvariantCulture);
        return _searchResults.FirstOrDefault(p => p.Id == id);
    }

    private void SelectProduct(CashierProduct product)
    {
        foreach (DataGridViewRow row in _productsGrid.Rows)
            if (Convert.ToInt32(row.Cells["Id"].Value, CultureInfo.InvariantCulture) == product.Id)
            {
                row.Selected = true;
                _productsGrid.CurrentCell = row.Cells["الصنف"];
                _unitPrice.Value = Math.Min(_unitPrice.Maximum, Math.Max(_unitPrice.Minimum, product.Price));
                return;
            }
    }

    private void ScanBarcode()
    {
        var scan = _barcode.Text.Trim();
        if (scan.Length == 0) return;
        var product = _cashier.FindProductByScan(scan);
        if (product is null)
        {
            _status.Text = "لم يُعثر على الصنف بالباركود/الرمز: " + scan;
            _search.Text = scan;
            _barcode.SelectAll();
            _barcode.Focus();
            return;
        }
        _unitPrice.Value = Math.Min(_unitPrice.Maximum, Math.Max(_unitPrice.Minimum, product.Price));
        AddProductToCart(product);
        _barcode.Clear();
        _barcode.Focus();
    }

    private void AddSelectedProduct()
    {
        var product = SelectedProduct();
        if (product is null) { Notice("حدد صنفًا من القائمة أولًا."); return; }
        AddProductToCart(product);
    }

    private void AddProductToCart(CashierProduct product)
    {
        var existing = _cart.FirstOrDefault(x => x.Product.Id == product.Id);
        var requestedQuantity = (existing?.Quantity ?? 0m) + _quantity.Value;
        if (product.Quantity < requestedQuantity)
            throw new InvalidOperationException($"الكمية المطلوبة {requestedQuantity:0.###} من الصنف {product.Name} تتجاوز المتوفر {product.Quantity:0.###}.");
        if (existing is null) _cart.Add(new CartLine(product, _quantity.Value, _unitPrice.Value));
        else
        {
            existing.Quantity = requestedQuantity;
            existing.UnitPrice = _unitPrice.Value;
        }
        _quantity.Value = 1m;
        RefreshCart();
        UpdateTotals();
        _status.Text = "تمت إضافة " + product.Name + " إلى السلة.";
    }

    private void RefreshCart()
    {
        var table = new DataTable();
        table.Columns.Add("Id", typeof(int));
        table.Columns.Add("رمز الصنف", typeof(string));
        table.Columns.Add("الصنف", typeof(string));
        table.Columns.Add("الكمية", typeof(decimal));
        table.Columns.Add("الوحدة", typeof(string));
        table.Columns.Add("السعر", typeof(decimal));
        table.Columns.Add("ضريبة %", typeof(decimal));
        table.Columns.Add("الإجمالي", typeof(decimal));
        foreach (var line in _cart)
            table.Rows.Add(line.Product.Id, line.Product.Code, line.Product.Name, line.Quantity, line.Product.Unit, line.UnitPrice, line.Product.VatRate, LineTotal(line));
        _cartGrid.DataSource = table;
        _cartGrid.Columns["Id"].Visible = false;
        _cartGrid.Columns["Id"].HeaderText = "المعرف";
        if (_cartGrid.Rows.Count > 0) _cartGrid.Rows[_cartGrid.Rows.Count - 1].Selected = true;
    }

    private CartLine? SelectedCartLine()
    {
        if (_cartGrid.CurrentRow is null) return null;
        var id = Convert.ToInt32(_cartGrid.CurrentRow.Cells["Id"].Value, CultureInfo.InvariantCulture);
        return _cart.FirstOrDefault(x => x.Product.Id == id);
    }

    private void AdjustSelectedQuantity(decimal delta)
    {
        var line = SelectedCartLine();
        if (line is null) { Notice("حدد سطرًا من السلة."); return; }
        var next = line.Quantity + delta;
        if (next <= 0m) { _cart.Remove(line); }
        else
        {
            if (next > line.Product.Quantity) throw new InvalidOperationException("الكمية تتجاوز الرصيد المتاح.");
            line.Quantity = next;
        }
        RefreshCart();
        UpdateTotals();
    }

    private void RemoveSelectedLine()
    {
        var line = SelectedCartLine();
        if (line is null) return;
        _cart.Remove(line);
        RefreshCart();
        UpdateTotals();
    }

    private void ClearCart()
    {
        if (_cart.Count == 0) return;
        if (MessageBox.Show(this, "هل تريد تفريغ جميع أصناف السلة؟", "خالد للأنظمة المحاسبية", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        _cart.Clear();
        RefreshCart();
        UpdateTotals();
    }

    private decimal LineNet(CartLine line)
    {
        var original = AccountingService.ToHalalas(line.Quantity * line.UnitPrice);
        return AccountingService.FromHalalas(AccountingService.ToHalalas(AccountingService.FromHalalas(original) * (100m - _discount.Value) / 100m));
    }

    private decimal LineVat(CartLine line) =>
        AccountingService.FromHalalas(AccountingService.ToHalalas(LineNet(line) * line.Product.VatRate / 100m));

    private decimal LineTotal(CartLine line) => LineNet(line) + LineVat(line);

    private (decimal Gross, decimal Discount, decimal Vat, decimal Total) Totals()
    {
        var gross = _cart.Sum(l => AccountingService.FromHalalas(AccountingService.ToHalalas(l.Quantity * l.UnitPrice)));
        var net = _cart.Sum(LineNet);
        var vat = _cart.Sum(LineVat);
        return (gross, gross - net, vat, net + vat);
    }

    private void UpdateTotals()
    {
        var t = Totals();
        _subtotalLabel.Text = "قبل الخصم: " + Money(t.Gross);
        _discountLabel.Text = "الخصم: " + Money(t.Discount);
        _vatLabel.Text = "الضريبة: " + Money(t.Vat);
        _totalLabel.Text = "الإجمالي: " + Money(t.Total);
        var method = Convert.ToString(_payment.SelectedValue) ?? "CASH";
        if (method == "BANK") _tendered.Value = Math.Min(_tendered.Maximum, t.Total);
        if (method == "CREDIT") _tendered.Value = 0;
        _changeLabel.Text = "الباقي: " + Money(method == "CASH" ? Math.Max(0m, _tendered.Value - t.Total) : 0m);
        if (_checkoutButton is not null) _checkoutButton.Enabled = _activeSession is not null;
    }

    private void RefreshSessionUi()
    {
        if (_activeSession is not null)
            _activeSession = _cashier.GetActiveSession(_user.UserName);
        if (_activeSession is null)
        {
            _sessionStatus.Text = "لا توجد وردية مفتوحة — افتح الوردية قبل اعتماد البيع.";
            if (_sessionButton is not null) _sessionButton.Text = "فتح الوردية";
        }
        else
        {
            _sessionStatus.Text = $"الوردية #{_activeSession.Id} | الافتتاح: {Money(_activeSession.OpeningCash)} | مبيعات نقدية: {Money(_activeSession.CashSales)} | فواتير: {_activeSession.InvoiceCount}";
            if (_sessionButton is not null) _sessionButton.Text = "إغلاق الوردية";
        }
        if (_checkoutButton is not null) _checkoutButton.Enabled = _activeSession is not null;
    }

    private void ToggleCashierSession()
    {
        if (_activeSession is null)
        {
            var opening = PromptMoney("فتح وردية الكاشير", "أدخل رصيد النقدية الافتتاحي في الدرج:", 0m);
            if (!opening.HasValue) return;
            _activeSession = _cashier.OpenSession(_user.UserName, opening.Value);
            RefreshSessionUi();
            _status.Text = $"تم فتح الوردية #{_activeSession.Id}. يمكنك الآن البيع.";
            return;
        }

        if (_cart.Count > 0)
            throw new InvalidOperationException("أكمل الفاتورة أو أفرغ السلة قبل إغلاق الوردية.");
        var summary = _cashier.GetSessionSummary(_activeSession.Id);
        var counted = PromptMoney("إغلاق وردية الكاشير",
            $"المتوقع من رصيد البداية والمبيعات النقدية: {Money(summary.ExpectedCash)}\nأدخل المبلغ المعدود فعليًا في الدرج:",
            summary.ExpectedCash);
        if (!counted.HasValue) return;
        var closed = _cashier.CloseSession(summary.Id, _user.UserName, counted.Value);
        _activeSession = null;
        RefreshSessionUi();
        var diffLabel = closed.Difference >= 0m ? "زيادة" : "عجز";
        MessageBox.Show(this,
            $"تم إغلاق الوردية #{closed.Id}\nرصيد البداية: {Money(closed.OpeningCash)}\nالمبيعات النقدية: {Money(closed.CashSales)}\nالمتوقع: {Money(closed.ExpectedCash)}\nالمعدود: {Money(closed.CountedCash ?? 0m)}\n{diffLabel}: {Money(Math.Abs(closed.Difference ?? 0m))}\nعدد الفواتير: {closed.InvoiceCount}",
            "خالد للأنظمة المحاسبية | إقفال الوردية", MessageBoxButtons.OK, MessageBoxIcon.Information);
        _status.Text = $"تم إغلاق الوردية #{closed.Id} وتسجيل فرق الصندوق.";
    }

    private decimal? PromptMoney(string title, string promptText, decimal initialValue)
    {
        using var form = new Form
        {
            Text = "خالد للأنظمة المحاسبية | " + title, Width = 520, Height = 230,
            StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false, MinimizeBox = false, RightToLeft = RightToLeft.Yes,
            RightToLeftLayout = true, Font = Font, BackColor = _paper
        };
        var prompt = new Label { Text = promptText, Dock = DockStyle.Top, Height = 70, TextAlign = ContentAlignment.MiddleRight, Padding = new Padding(12), ForeColor = Color.FromArgb(_palette.ForegroundR, _palette.ForegroundG, _palette.ForegroundB) };
        var amount = new NumericUpDown { Dock = DockStyle.Top, Minimum = 0m, Maximum = 1000000000m, DecimalPlaces = 2, ThousandsSeparator = true, Value = Math.Clamp(initialValue, 0m, 1000000000m), Margin = new Padding(12) };
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 52, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(6) };
        var ok = new Button { Text = "تأكيد", Width = 110, Height = 34, DialogResult = DialogResult.OK, BackColor = _navy, ForeColor = Color.White, FlatStyle = FlatStyle.Flat, Margin = new Padding(4) };
        var cancel = new Button { Text = "إلغاء", Width = 110, Height = 34, DialogResult = DialogResult.Cancel, BackColor = _blue, ForeColor = Color.White, FlatStyle = FlatStyle.Flat, Margin = new Padding(4) };
        buttons.Controls.Add(ok); buttons.Controls.Add(cancel);
        form.Controls.Add(amount); form.Controls.Add(prompt); form.Controls.Add(buttons);
        form.AcceptButton = ok;
        form.CancelButton = cancel;
        return form.ShowDialog(this) == DialogResult.OK ? amount.Value : null;
    }

    private void PaymentChanged()
    {
        var method = Convert.ToString(_payment.SelectedValue) ?? "CASH";
        _customer.Enabled = method == "CREDIT" || _cashCustomerId > 0;
        _discount.Enabled = method != "CREDIT" || _customer.SelectedValue is not null;
        if (method == "CREDIT") _tendered.Value = 0;
        else if (method == "BANK") _tendered.Value = Math.Min(_tendered.Maximum, Totals().Total);
        UpdateTotals();
    }

    private void Checkout()
    {
        if (!_licensing.CheckAccess().HasAccess)
            throw new InvalidOperationException("الترخيص غير صالح أو انتهت صلاحيته؛ لا يمكن اعتماد البيع.");
        if (!_user.Role.Equals("Admin", StringComparison.Ordinal) && !_user.Role.Equals("Accountant", StringComparison.Ordinal))
            throw new InvalidOperationException("ليس لديك صلاحية اعتماد البيع.");
        if (_activeSession is null) throw new InvalidOperationException("افتح وردية الكاشير قبل اعتماد البيع.");
        if (_cart.Count == 0) throw new InvalidOperationException("السلة فارغة.");
        if (_customer.SelectedValue is null) throw new InvalidOperationException("اختر العميل.");
        var method = Convert.ToString(_payment.SelectedValue) ?? "CASH";
        if (method == "CREDIT" && Convert.ToInt32(_customer.SelectedValue, CultureInfo.InvariantCulture) == _cashCustomerId)
            throw new InvalidOperationException("اختر عميلًا مسجلًا عند البيع الآجل.");
        var lines = _cart.Select(x => new InvoiceLineInput(x.Product.Id, x.Quantity, x.UnitPrice)).ToList();
        var invoiceId = _cashier.Checkout(DateTime.Today,
            Convert.ToInt32(_customer.SelectedValue, CultureInfo.InvariantCulture),
            lines, _discount.Value, method, _tendered.Value, _user.UserName, _activeSession.Id);
        _lastReceipt = _cashier.GetReceipt(invoiceId);
        _activeSession = _cashier.GetActiveSession(_user.UserName);
        RefreshSessionUi();
        var answer = MessageBox.Show(this,
            $"تم اعتماد الفاتورة {_lastReceipt.Number}\nالإجمالي: {Money(_lastReceipt.Total)}\nالباقي: {Money(_lastReceipt.Change)}\n\nهل تريد معاينة وطباعة الإيصال؟",
            "خالد للأنظمة المحاسبية", MessageBoxButtons.YesNo, MessageBoxIcon.Information);
        if (answer == DialogResult.Yes) PrintReceipt();
        _cart.Clear();
        RefreshCart();
        _discount.Value = 0;
        _tendered.Value = 0;
        _customer.SelectedValue = _cashCustomerId;
        _payment.SelectedValue = "CASH";
        UpdateTotals();
        _search.Clear();
        _barcode.Clear();
        _barcode.Focus();
        _status.Text = "تم ترحيل الفاتورة وربطها بالقيد والمخزون: " + _lastReceipt.Number;
    }

    private void PrintReceipt()
    {
        if (_lastReceipt is null) { Notice("لا توجد فاتورة جاهزة للطباعة."); return; }
        _printDocument?.Dispose();
        _printDocument = new PrintDocument();
        _printDocument.DocumentName = "خالد للأنظمة المحاسبية - " + _lastReceipt.Number;
        _printDocument.DefaultPageSettings.Margins = new Margins(15, 15, 15, 15);
        _printLineIndex = 0;
        _printDocument.PrintPage += PrintReceiptPage;
        using var preview = new PrintPreviewDialog
        {
            Document = _printDocument, Width = 950, Height = 750,
            StartPosition = FormStartPosition.CenterParent
        };
        preview.ShowDialog(this);
    }

    private void PrintReceiptPage(object? sender, PrintPageEventArgs e)
    {
        if (_lastReceipt is null || e.Graphics is null) { e.HasMorePages = false; return; }
        using var titleFont = new Font("Segoe UI", 12F, FontStyle.Bold);
        using var normalFont = new Font("Segoe UI", 9F);
        using var smallFont = new Font("Segoe UI", 8F);
        using var boldFont = new Font("Segoe UI", 9F, FontStyle.Bold);
        using var brush = new SolidBrush(Color.Black);
        using var fmt = new StringFormat { Alignment = StringAlignment.Far, FormatFlags = StringFormatFlags.DirectionRightToLeft };
        var bounds = e.MarginBounds;
        float y = bounds.Top;
        float lineHeight = normalFont.GetHeight(e.Graphics) + 4;
        void Draw(string text, Font font, float height)
        {
            e.Graphics.DrawString(text, font, brush, new RectangleF(bounds.Left, y, bounds.Width, height), fmt);
            y += height;
        }
        if (_printLineIndex == 0)
        {
            Draw("خالد للأنظمة المحاسبية", titleFont, 28);
            if (!string.IsNullOrWhiteSpace(_settings.LegalEntityName)) Draw(_settings.LegalEntityName, boldFont, lineHeight);
            if (!string.IsNullOrWhiteSpace(_settings.Address)) Draw(_settings.Address, normalFont, lineHeight);
            if (!string.IsNullOrWhiteSpace(_settings.Phone)) Draw("الهاتف: " + _settings.Phone, normalFont, lineHeight);
            if (!string.IsNullOrWhiteSpace(_settings.VatRegistrationNumber)) Draw("الرقم الضريبي: " + _settings.VatRegistrationNumber, normalFont, lineHeight);
            Draw("فاتورة ضريبية مبسطة", boldFont, 19);
            Draw("رقم الفاتورة: " + _lastReceipt.Number, normalFont, lineHeight);
            Draw("التاريخ: " + _lastReceipt.InvoiceDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), normalFont, lineHeight);
            Draw("العميل: " + _lastReceipt.CustomerName, normalFont, lineHeight);
            Draw("------------------------------------------", smallFont, lineHeight);
            Draw("الصنف | الكمية | السعر | الإجمالي", boldFont, lineHeight);
        }
        while (_printLineIndex < _lastReceipt.Lines.Count && y + lineHeight < bounds.Bottom - 105)
        {
            var line = _lastReceipt.Lines[_printLineIndex++];
            Draw($"{line.Name} ({line.Code})", boldFont, lineHeight);
            Draw($"{line.Quantity:0.###} {line.Unit} × {line.UnitPrice:N2} = {line.Total:N2}", smallFont, lineHeight);
        }
        if (_printLineIndex < _lastReceipt.Lines.Count)
        {
            e.HasMorePages = true;
            return;
        }
        y += 6;
        Draw("قبل الخصم: " + Money(_lastReceipt.Subtotal + _lastReceipt.Discount), normalFont, lineHeight);
        Draw("الخصم: " + Money(_lastReceipt.Discount), normalFont, lineHeight);
        Draw("ضريبة القيمة المضافة: " + Money(_lastReceipt.Vat), normalFont, lineHeight);
        Draw("الإجمالي: " + Money(_lastReceipt.Total), titleFont, 26);
        Draw("المبلغ المستلم: " + Money(_lastReceipt.AmountTendered), normalFont, lineHeight);
        Draw("الباقي: " + Money(_lastReceipt.Change), normalFont, lineHeight);
        Draw(string.IsNullOrWhiteSpace(_settings.ReceiptFooter) ? "شكرًا لتعاملكم مع خالد للأنظمة المحاسبية" : _settings.ReceiptFooter, boldFont, lineHeight + 2);
        e.HasMorePages = false;
        _printLineIndex = 0;
    }

    private static string Money(decimal value) => value.ToString("N2", CultureInfo.GetCultureInfo("ar-SA")) + " ر.س";
    private void Safe(Action action) { try { action(); } catch (Exception ex) { Notice(ex.Message, "تعذر إكمال العملية"); } }
    private void Notice(string message, string title = "خالد للأنظمة المحاسبية") =>
        MessageBox.Show(this, message, title, MessageBoxButtons.OK, MessageBoxIcon.Information);

    private sealed record PaymentChoice(string Value, string Text);
}
