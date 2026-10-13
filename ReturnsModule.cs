using System.Data;
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
            SetStatus("تم ترحيل المردود مع القيد الضريبي وتحديث المخزون داخل معاملة واحدة.");
        }
    }
}