using System.Data;
using AlSaqr.Accounting.Data;
using AlSaqr.Accounting.Services;

namespace AlSaqr.Accounting.Forms;

internal sealed class InvoiceForm : Form
{
    private readonly ComboBox _kind = new();
    private readonly TextBox _number = new();
    private readonly DateTimePicker _date = new();
    private readonly ComboBox _party = new();
    private readonly CheckBox _credit = new() { Text = "على الحساب", Checked = true, AutoSize = true };
    private readonly DataGridView _lines = new();
    private readonly Label _total = new();
    private DataTable _products = new();

    public InvoiceForm()
    {
        Text = "فاتورة بيع / شراء";
        Width = 1000;
        Height = 650;
        MinimumSize = new Size(850, 520);
        StartPosition = FormStartPosition.CenterParent;
        RightToLeft = RightToLeft.Yes;
        RightToLeftLayout = true;
        Font = new Font("Segoe UI", 10F);
        BuildLayout();
        LoadLookups();
    }

    private void BuildLayout()
    {
        var header = new TableLayoutPanel { Dock = DockStyle.Top, Height = 100, Padding = new Padding(12), ColumnCount = 6, RowCount = 2 };
        for (int i = 0; i < 6; i++) header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 16.66F));
        AddHeaderField(header, "نوع الفاتورة", _kind, 0, 0);
        AddHeaderField(header, "رقم الفاتورة", _number, 1, 0);
        AddHeaderField(header, "التاريخ", _date, 2, 0);
        AddHeaderField(header, "العميل / المورد", _party, 3, 0);
        header.Controls.Add(_credit, 4, 1);
        _kind.DropDownStyle = ComboBoxStyle.DropDownList;
        _kind.Items.Add(new KeyValuePair<string, string>("SALE", "مبيعات"));
        _kind.Items.Add(new KeyValuePair<string, string>("PURCHASE", "مشتريات"));
        _kind.DisplayMember = "Value";
        _kind.ValueMember = "Key";
        _kind.SelectedIndex = 0;
        _kind.SelectedIndexChanged += (_, _) => ReloadParties();
        _date.Format = DateTimePickerFormat.Short;
        _date.Value = DateTime.Today;

        _lines.Dock = DockStyle.Fill;
        _lines.AllowUserToAddRows = true;
        _lines.AllowUserToDeleteRows = true;
        _lines.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _lines.RowHeadersVisible = false;
        _lines.BackgroundColor = Color.White;
        _lines.Columns.Add(new DataGridViewComboBoxColumn { Name = "ProductId", HeaderText = "الصنف", DisplayMember = "Name", ValueMember = "Id", FlatStyle = FlatStyle.Flat });
        _lines.Columns.Add(new DataGridViewTextBoxColumn { Name = "Quantity", HeaderText = "الكمية", ValueType = typeof(decimal) });
        _lines.Columns.Add(new DataGridViewTextBoxColumn { Name = "Price", HeaderText = "سعر الوحدة", ValueType = typeof(decimal) });
        _lines.Columns.Add(new DataGridViewTextBoxColumn { Name = "Vat", HeaderText = "الضريبة %", ValueType = typeof(decimal) });
        _lines.Columns.Add(new DataGridViewTextBoxColumn { Name = "LineTotal", HeaderText = "الإجمالي", ReadOnly = true });
        _lines.CellValueChanged += (_, _) => Recalculate();
        _lines.RowsAdded += (_, _) => Recalculate();
        _lines.RowsRemoved += (_, _) => Recalculate();
        _lines.CurrentCellDirtyStateChanged += (_, _) => { if (_lines.IsCurrentCellDirty) _lines.CommitEdit(DataGridViewDataErrorContexts.Commit); };
        _lines.EditingControlShowing += (_, e) =>
        {
            if (_lines.CurrentCell?.OwningColumn.Name == "ProductId" && e.Control is ComboBox combo)
                combo.SelectedIndexChanged += ProductSelected;
        };

        var footer = new Panel { Dock = DockStyle.Bottom, Height = 58, Padding = new Padding(12) };
        _total.Dock = DockStyle.Right;
        _total.Width = 360;
        _total.Font = new Font("Segoe UI", 13F, FontStyle.Bold);
        _total.TextAlign = ContentAlignment.MiddleRight;
        var save = new Button { Text = "ترحيل الفاتورة", Width = 145, Height = 34, Dock = DockStyle.Left };
        save.Click += (_, _) => Post();
        var cancel = new Button { Text = "إلغاء", Width = 90, Height = 34, Dock = DockStyle.Left, DialogResult = DialogResult.Cancel };
        footer.Controls.Add(_total);
        footer.Controls.Add(cancel);
        footer.Controls.Add(save);
        Controls.Add(_lines);
        Controls.Add(footer);
        Controls.Add(header);
    }

    private static void AddHeaderField(TableLayoutPanel layout, string caption, Control input, int column, int row)
    {
        var panel = new Panel { Dock = DockStyle.Fill };
        panel.Controls.Add(new Label { Text = caption, Dock = DockStyle.Top, Height = 25 });
        input.Dock = DockStyle.Bottom;
        panel.Controls.Add(input);
        layout.Controls.Add(panel, column, row);
    }

    private void LoadLookups()
    {
        using var db = Database.Open();
        using (var cmd = db.CreateCommand())
        {
            cmd.CommandText = "SELECT Id,Name,PriceHalalas/100.0 AS Price,VatRate FROM Products WHERE IsActive=1 ORDER BY Name";
            using var reader = cmd.ExecuteReader();
            _products = new DataTable();
            _products.Load(reader);
        }
        ((DataGridViewComboBoxColumn)_lines.Columns["ProductId"]!).DataSource = _products;
        ReloadParties();
        _lines.Rows.Add(1);
    }

    private void ReloadParties()
    {
        if (!IsHandleCreated && _party.IsDisposed) return;
        using var db = Database.Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = _kind.SelectedIndex == 1
            ? "SELECT Id,Name FROM Parties WHERE IsActive=1 AND Kind IN ('Supplier','Both') ORDER BY Name"
            : "SELECT Id,Name FROM Parties WHERE IsActive=1 AND Kind IN ('Customer','Both') ORDER BY Name";
        using var reader = cmd.ExecuteReader();
        var parties = new DataTable();
        parties.Load(reader);
        _party.DataSource = parties;
        _party.DisplayMember = "Name";
        _party.ValueMember = "Id";
        _party.DropDownStyle = ComboBoxStyle.DropDownList;
    }

    private void ProductSelected(object? sender, EventArgs e)
    {
        if (_lines.CurrentRow is null || _lines.CurrentCell?.OwningColumn.Name != "ProductId") return;
        if (_lines.CurrentCell.Value is not object id || id == DBNull.Value) return;
        var row = _products.Rows.Cast<DataRow>().FirstOrDefault(x => Convert.ToInt32(x["Id"]) == Convert.ToInt32(id));
        if (row is null) return;
        _lines.CurrentRow.Cells["Price"].Value = Convert.ToDecimal(row["Price"]);
        _lines.CurrentRow.Cells["Vat"].Value = Convert.ToDecimal(row["VatRate"]);
        Recalculate();
    }

    private void Recalculate()
    {
        decimal total = 0;
        foreach (DataGridViewRow row in _lines.Rows)
        {
            if (row.IsNewRow) continue;
            try
            {
                var qty = Convert.ToDecimal(row.Cells["Quantity"].Value ?? 0);
                var price = Convert.ToDecimal(row.Cells["Price"].Value ?? 0);
                var vat = Convert.ToDecimal(row.Cells["Vat"].Value ?? 0);
                var lineTotal = Math.Round(qty * price * (1 + vat / 100m), 2);
                row.Cells["LineTotal"].Value = lineTotal;
                total += lineTotal;
            }
            catch { row.Cells["LineTotal"].Value = "—"; }
        }
        _total.Text = "الإجمالي شامل الضريبة: " + total.ToString("N2") + " ر.س";
    }

    private void Post()
    {
        try
        {
            if (_party.SelectedValue is null) throw new InvalidOperationException("أضف العميل أو المورد أولًا.");
            var inputs = new List<InvoiceLineInput>();
            foreach (DataGridViewRow row in _lines.Rows)
            {
                if (row.IsNewRow) continue;
                if (row.Cells["ProductId"].Value is null) continue;
                inputs.Add(new InvoiceLineInput(
                    Convert.ToInt32(row.Cells["ProductId"].Value),
                    Convert.ToDecimal(row.Cells["Quantity"].Value ?? 0),
                    Convert.ToDecimal(row.Cells["Price"].Value ?? 0),
                    Convert.ToDecimal(row.Cells["Vat"].Value ?? 0)));
            }
            var kind = _kind.SelectedIndex == 1 ? "PURCHASE" : "SALE";
            var id = AccountingService.PostInvoice(kind, _number.Text.Trim(), _date.Value.Date, Convert.ToInt32(_party.SelectedValue), _credit.Checked, inputs);
            MessageBox.Show(this, $"تم ترحيل الفاتورة رقم {_number.Text} وربطها بالقيد المحاسبي. رقم السجل: {id}", "تم الترحيل", MessageBoxButtons.OK, MessageBoxIcon.Information);
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "تعذر ترحيل الفاتورة", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }
}
