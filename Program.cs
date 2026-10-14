using AlSaqr.Accounting.Data;
using AlSaqr.Accounting.Forms;
using AlSaqr.Accounting.Services;

namespace AlSaqr.Accounting;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        try
        {
            var licensing = new LicenseService();
            var licenseStatus = licensing.GetStatus();
            if (!licenseStatus.Allowed)
            {
                using var activation = new LicenseActivationForm(licensing, licenseStatus);
                if (activation.ShowDialog() != DialogResult.OK) return;
            }

            Database.Initialize();
 feature/erp-pos-cashier-20261013
            var licensing = new LicenseService();
            var licenseStatus = licensing.CheckAccess();
            if (!licenseStatus.HasAccess)
            {
                using var licenseForm = new LicenseForm(licensing, startupGate: true);
                if (licenseForm.ShowDialog() != DialogResult.OK) return;
                licenseStatus = licensing.CheckAccess();
                if (!licenseStatus.HasAccess)
                {
                    MessageBox.Show(licenseStatus.Message, "خالد للأنظمة المحاسبية | الترخيص",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
            }

=======
 main
            var auth = new AuthService();
            using var login = new LoginForm(auth);
            if (login.ShowDialog() != DialogResult.OK || login.User is null) return;
            Application.Run(new MainForm(login.User));
        }
        catch (Exception ex)
        {
            MessageBox.Show("تعذر بدء نظام المحاسبة. لم يتم تجاوز الخطأ أو إنشاء معاملات تلقائيًا.\r\n\r\n" + ex.Message,
feature/erp-pos-cashier-20261013
                "خالد للأنظمة المحاسبية | خطأ بدء التشغيل", MessageBoxButtons.OK, MessageBoxIcon.Error);
=======
                "خطأ بدء التشغيل", MessageBoxButtons.OK, MessageBoxIcon.Error);
 main
        }
    }
}
