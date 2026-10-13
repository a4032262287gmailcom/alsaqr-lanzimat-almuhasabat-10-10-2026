using AlSaqr.Accounting.Services;

namespace AlSaqr.Accounting.Forms;

internal sealed class RecordEditorForm : Form
{
    private readonly string _view;
    private readonly long? _id;
    private readonly Dictionary<string, Control> _fields = new();

    private RecordEditorForm(string view, DataGridViewRow? row)
    {
        _view = view;
        _id = view switch
        {
            "parties" or "products" when row is not null => Convert.ToInt64(row.Cells[0].Value),
            _ => null
        };
        Text = row is null ? "إضافة سجل" : "تعديل سجل";
        Width = 470;
        Height = view == "products" ? 480 : 410;
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        RightToLeft = RightToLeft.Yes;
        RightToLeftLayout = true;
        Font = new Font("Segoe UI", 10F);

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(18), ColumnCount = 2, AutoScroll = true };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 36));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 64));
        Controls.Add(layout);

        if (view == "accounts")
        {
            AddText(layout, "code", "رمز الحساب", row?.Cells[0].Value?.ToString());
            AddText(layout, "name", "اسم الحساب", row?.Cells[1].Value?.ToString());
            AddCombo(layout, "type", "نوع الحساب", new[] { ("Asset", "أصول"), ("Liability", "التزامات"), ("Equity", "حقوق ملكية"), ("Revenue", "إيرادات"), ("Expense", "مصروفات") }, MapType(row?.Cells[2].Value?.ToString()));
            AddCheck(layout, "active", "نشط", row is null || Convert.ToString(row.Cells[3].Value) is "1" or "True");
        }
        else if (view == "parties")
        {
            AddText(layout, "name", "الاسم", row?.Cells[1].Value?.ToString());
            AddCombo(layout, "kind", "التصنيف", new[] { ("Customer", "عميل"), ("Supplier", "مورد"), ("Both", "عميل ومورد") }, MapKind(row?.Cells[2].Value?.ToString()));
            AddText(layout, "phone", "الهاتف", row?.Cells[3].Value?.ToString());
            AddText(layout, "vat", "الرقم الضريبي", row?.Cells[4].Value?.ToString());
            AddCheck(layout, "active", "نشط", row is null || Convert.ToString(row.Cells[5].Value) is "1" or "True");
        }
        else
        {
            AddText(layout, "code", "رمز الصنف", row?.Cells[1].Value?.ToString());
            AddText(layout, "name", "اسم الصنف", row?.Cells[2].Value?.ToString());
            AddText(layout, "unit", "الوحدة", row?.Cells[3].Value?.ToString() ?? "حبة");
            AddText(layout, "cost", "التكلفة بالريال", row is null ? "0" : Convert.ToString(row.Cells[5].Value));
            AddText(layout, "price", "سعر البيع بالريال", row is null ? "0" : Convert.ToString(row.Cells[6].Value));
            AddText(layout, "vat", "الضريبة %", row is null ? "15" : Convert.ToString(row.Cells[7].Value));
            AddCheck(layout, "active", "نشط", row is null || Convert.ToString(row.Cells[8].Value) is "1" or "True");
            Height = 520;
        }

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 55, FlowDirection = FlowDirection.LeftToRight, Padding = new Padding(12) };
        var save = new Button { Text = "حفظ", Width = 100, Height = 32 };
        save.Click += (_, _) => Save();
        var cancel = new Button { Text = "إلغاء", Width = 100, Height = 32, DialogResult = DialogResult.Cancel };
        buttons.Controls.Add(save);
        buttons.Controls.Add(cancel);
        Controls.Add(buttons);
        AcceptButton = save;
        CancelButton = cancel;
    }

    public static bool ShowEditor(IWin32Window owner, string view, DataGridViewRow? row = null)
    {
        using var form = new RecordEditorForm(view, row);
        return form.ShowDialog(owner) == DialogResult.OK;
    }

    private void AddText(TableLayoutPanel layout, string key, string caption, string? value)
    {
        AddLabel(layout, caption);
        var box = new TextBox { Dock = DockStyle.Fill, Text = value ?? string.Empty, Margin = new Padding(4, 5, 4, 5) };
        _fields[key] = box;
        layout.Controls.Add(box);
    }

    private void AddCombo(TableLayoutPanel layout, string key, string caption, (string Value, string Label)[] options, string? selected)
    {
        AddLabel(layout, caption);
        var combo = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList, DisplayMember = "Label", ValueMember = "Value", Margin = new Padding(4, 5, 4, 5) };
        combo.DataSource = options.Select(x => new { x.Value, x.Label }).ToList();
        if (selected is not null) combo.SelectedValue = selected;
        _fields[key] = combo;
        layout.Controls.Add(combo);
    }

    private void AddCheck(TableLayoutPanel layout, string key, string caption, bool value)
    {
        AddLabel(layout, caption);
        var check = new CheckBox { Checked = value, Text = "نعم", AutoSize = true, Margin = new Padding(4, 7, 4, 5) };
        _fields[key] = check;
        layout.Controls.Add(check);
    }

    private static void AddLabel(TableLayoutPanel layout, string text)
    {
        layout.Controls.Add(new Label { Text = text, AutoSize = true, Anchor = AnchorStyles.Right, Margin = new Padding(4, 8, 4, 5) });
    }

    private string TextValue(string key) => ((TextBox)_fields[key]).Text.Trim();
    private string ComboValue(string key) => Convert.ToString(((ComboBox)_fields[key]).SelectedValue) ?? string.Empty;
    private bool CheckValue(string key) => ((CheckBox)_fields[key]).Checked;

    private void Save()
    {
        try
        {
            if (_view == "accounts")
                AccountingService.SaveAccount(TextValue("code"), TextValue("name"), ComboValue("type"), CheckValue("active"));
            else if (_view == "parties")
                AccountingService.SaveParty(_id, TextValue("name"), ComboValue("kind"), TextValue("phone"), TextValue("vat"), CheckValue("active"));
            else
                AccountingService.SaveProduct(_id, TextValue("code"), TextValue("name"), TextValue("unit"),
                    decimal.Parse(TextValue("cost")), decimal.Parse(TextValue("price")), decimal.Parse(TextValue("vat")), CheckValue("active"));
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "تعذر الحفظ", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private static string? MapType(string? value) => value switch
    {
        "Asset" or "أصول" => "Asset", "Liability" or "التزامات" => "Liability",
        "Equity" or "حقوق ملكية" => "Equity", "Revenue" or "إيرادات" => "Revenue",
        "Expense" or "مصروفات" => "Expense", _ => null
    };

    private static string? MapKind(string? value) => value switch
    {
        "Customer" or "عميل" => "Customer", "Supplier" or "مورد" => "Supplier",
        "Both" or "عميل ومورد" => "Both", _ => null
    };
}
