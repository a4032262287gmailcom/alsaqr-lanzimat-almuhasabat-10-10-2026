using AlSaqr.Accounting.Data;
using AlSaqr.Accounting.Forms;

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
            Application.Run(new MainForm());
        }
        catch (Exception ex)
        {
            MessageBox.Show("تعذر تشغيل نظام الصقر المحاسبي.\n\n" + ex.Message,
                "خطأ في بدء التشغيل", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
