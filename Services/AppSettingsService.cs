using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace AlSaqr.Accounting.Services;

internal sealed record AccountingAppSettings(
    string LegalEntityName,
    string VatRegistrationNumber,
    string Phone,
    string Address,
    string ReceiptFooter,
    string Theme,
    decimal DefaultVatRate,
    string BackupDirectory)
{
    public static AccountingAppSettings Defaults() => new(
        "", "", "", "", "شكرًا لتعاملكم مع الصقر لأنظمة المحاسبة",
        "Navy", 15m,
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "الصقر - النسخ الاحتياطي"));
}

internal sealed record UiThemePalette(
    string Name, string DisplayName,
    int PrimaryR, int PrimaryG, int PrimaryB,
    int SecondaryR, int SecondaryG, int SecondaryB,
    int AccentR, int AccentG, int AccentB,
    int BackgroundR, int BackgroundG, int BackgroundB,
    int SurfaceR, int SurfaceG, int SurfaceB,
    int ForegroundR, int ForegroundG, int ForegroundB,
    int MutedR, int MutedG, int MutedB)
{
    public static IReadOnlyList<UiThemePalette> All { get; } = new[]
    {
        new UiThemePalette("Navy", "كحلي وذهبي", 18,32,52, 37,66,101, 190,151,61, 244,246,249, 255,255,255, 29,41,57, 105,115,130),
        new UiThemePalette("Light", "فاتح كلاسيكي", 44,62,80, 82,101,120, 0,120,150, 248,250,252, 255,255,255, 31,41,55, 107,114,128),
        new UiThemePalette("Dark", "داكن", 23,28,38, 39,48,63, 92,157,225, 28,33,43, 39,45,57, 235,239,245, 164,174,190),
        new UiThemePalette("Forest", "أخضر تجاري", 26,65,56, 37,94,78, 191,151,74, 242,247,244, 255,255,255, 31,50,43, 103,120,111)
    };
}

internal static class AppSettingsService
{
    private static readonly byte[] Entropy = SHA256.HashData(Encoding.UTF8.GetBytes("AlSaqr.Accounting.AppSettings.v1"));
    private static string Folder =>
        Environment.GetEnvironmentVariable("ALSAQR_ACCOUNTING_DATA_DIR") ??
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AlSaqrAccounting");
    private static string PathName => Path.Combine(Folder, "app-settings.json");
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static AccountingAppSettings Load()
    {
        if (!File.Exists(PathName)) return AccountingAppSettings.Defaults();
        try
        {
            var stored = File.ReadAllBytes(PathName);
            var first = Array.FindIndex(stored, b => b is not (byte)' ' and not (byte)'\t' and not (byte)'\r' and not (byte)'\n');
            var isLegacyJson = first >= 0 && stored[first] == (byte)'{';
            var jsonBytes = isLegacyJson
                ? stored
                : ProtectedData.Unprotect(stored, Entropy, DataProtectionScope.CurrentUser);
            var saved = JsonSerializer.Deserialize<AccountingAppSettings>(jsonBytes, JsonOptions);
            if (saved is null) throw new InvalidDataException("ملف إعدادات الصقر فارغ أو غير صالح.");
            if (!UiThemePalette.All.Any(x => x.Name == saved.Theme))
                saved = saved with { Theme = "Navy" };
            if (saved.DefaultVatRate is < 0m or > 100m)
                saved = saved with { DefaultVatRate = 15m };
            if (isLegacyJson) Save(saved);
            return saved;
        }
        catch (Exception ex) when (ex is JsonException or CryptographicException or InvalidDataException or UnauthorizedAccessException or IOException)
        {
            throw new InvalidOperationException("تعذر قراءة إعدادات الصقر المحمية. تحقق من حساب Windows الحالي واحتفظ بنسخة من الملف قبل إعادة إنشائه.", ex);
        }
    }

    public static void Save(AccountingAppSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.Theme) || UiThemePalette.All.All(x => x.Name != settings.Theme))
            throw new ArgumentException("اختر ثيمًا معروفًا من قائمة الثيمات.");
        if (settings.DefaultVatRate is < 0m or > 100m)
            throw new ArgumentException("ضريبة القيمة المضافة الافتراضية يجب أن تكون بين صفر و100.");
        Directory.CreateDirectory(Folder);
        var json = JsonSerializer.SerializeToUtf8Bytes(settings, JsonOptions);
        var encrypted = ProtectedData.Protect(json, Entropy, DataProtectionScope.CurrentUser);
        var temp = PathName + ".tmp";
        File.WriteAllBytes(temp, encrypted);
        File.Move(temp, PathName, true);
    }

    public static UiThemePalette GetPalette(string? name) =>
        UiThemePalette.All.FirstOrDefault(x => x.Name == name) ?? UiThemePalette.All[0];
}
