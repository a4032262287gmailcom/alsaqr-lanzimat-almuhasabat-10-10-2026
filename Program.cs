using AlSaqr.Accounting.Data;
using AlSaqr.Accounting.Forms;
 desktop-accounting-core-20261013
using AlSaqr.Accounting.Services;
=======
 main

namespace AlSaqr.Accounting;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        try
        {
            Database.Initialize();
 desktop-accounting-core-20261013
            var auth = new AuthService();
            using var login = new LoginForm(auth);
            if (login.ShowDialog() != DialogResult.OK || login.User is null) return;
            Application.Run(new MainForm(login.User));
        }
        catch (Exception ex)
        {
            MessageBox.Show("تعذر بدء نظام المحاسبة. لم يتم تجاوز الخطأ أو إنشاء معاملات تلقائيًا.\r\n\r\n" + ex.Message,
                "خطأ بدء التشغيل", MessageBoxButtons.OK, MessageBoxIcon.Error);
=======
            Application.Run(new MainForm());
        }
        catch (Exception ex)
        {
            MessageBox.Show("تعذر تشغيل نظام الصقر المحاسبي.\n\n" + ex.Message,
                "خطأ في بدء التشغيل", MessageBoxButtons.OK, MessageBoxIcon.Error);
 main
        }
    }
}
