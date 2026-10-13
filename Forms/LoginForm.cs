using AlSaqr.Accounting.Services;

namespace AlSaqr.Accounting.Forms;

internal sealed class LoginForm : Form
{
    private readonly AuthService _auth;
    private readonly bool _setupMode;
    private readonly TextBox _user = new();
    private readonly TextBox _password = new();
    private readonly TextBox _confirm = new();
    public AuthUser? User { get; private set; }

    public LoginForm(AuthService auth)
    {
        _auth = auth;
        _setupMode = !auth.HasUsers();
        Text = _setupMode ? "الصقر لأنظمة المحاسبة | تهيئة مدير النظام" : "الصقر لأنظمة المحاسبة | تسجيل الدخول";
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false; MinimizeBox = false;
        Width = 470; Height = _setupMode ? 390 : 315;
        RightToLeft = RightToLeft.Yes; RightToLeftLayout = true;
        Font = new Font("Segoe UI", 10F);
        BackColor = Color.FromArgb(243,245,248);

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(24), ColumnCount = 1, RowCount = _setupMode ? 8 : 6 };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        var title = new Label { Text = _setupMode ? "الصقر لأنظمة المحاسبة | إنشاء حساب المدير الأول" : "الصقر لأنظمة المحاسبة", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, Font = new Font("Segoe UI",16,FontStyle.Bold), ForeColor = Color.FromArgb(20,34,56) };
        root.Controls.Add(title);
        root.Controls.Add(MakeLabel("اسم المستخدم")); Configure(_user); root.Controls.Add(_user);
        root.Controls.Add(MakeLabel("كلمة المرور (10 أحرف على الأقل)")); Configure(_password); _password.UseSystemPasswordChar = true; root.Controls.Add(_password);
        if (_setupMode)
        {
            root.Controls.Add(MakeLabel("تأكيد كلمة المرور")); Configure(_confirm); _confirm.UseSystemPasswordChar = true; root.Controls.Add(_confirm);
        }
        var button = new Button { Text = _setupMode ? "إنشاء المدير والمتابعة" : "دخول", Height = 42, Dock = DockStyle.Fill, BackColor = Color.FromArgb(20,34,56), ForeColor = Color.White, FlatStyle = FlatStyle.Flat };
        button.Click += Submit;
        root.Controls.Add(button);
        Controls.Add(root);
        AcceptButton = button;
    }

    private static Label MakeLabel(string text) => new() { Text = text, AutoSize = false, Dock = DockStyle.Fill, TextAlign = ContentAlignment.BottomRight, Padding = new Padding(0,4,0,2) };
    private static void Configure(TextBox box) { box.Dock = DockStyle.Fill; box.BorderStyle = BorderStyle.FixedSingle; }
    private void Submit(object? sender, EventArgs e)
    {
        try
        {
            if (_setupMode)
            {
                if (_password.Text != _confirm.Text) throw new InvalidOperationException("كلمتا المرور غير متطابقتين.");
                _auth.CreateFirstAdmin(_user.Text, _password.Text);
                User = _auth.Authenticate(_user.Text, _password.Text);
            }
            else User = _auth.Authenticate(_user.Text, _password.Text);
            if (User is null) throw new InvalidOperationException("بيانات الدخول غير صحيحة أو أن الحساب موقوف.");
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "تسجيل الدخول", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
    }
}
