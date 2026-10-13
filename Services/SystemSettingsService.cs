using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace AlSaqr.Accounting.Services;

internal sealed class SystemSettings
{
    public string CompanyName { get; set; } = "الصقر لأنظمة المحاسبة";
    public string CommercialRegistration { get; set; } = "";
    public string VatNumber { get; set; } = "";
    public string Address { get; set; } = "";
    public string Phone { get; set; } = "";
    public string ReceiptFooter { get; set; } = "شكرًا لتعاملكم معنا";
    public string Theme { get; set; } = "Classic";
    public string PrinterName { get; set; } = "";
    public bool AutoPrintReceipt { get; set; } = false;
}

internal readonly record struct ThemePalette(Color Navy, Color Blue, Color Gold, Color Paper, Color Text, Color Muted);

internal sealed class SystemSettingsService
{
    private static readonly byte[] Entropy = SHA256.HashData(Encoding.UTF8.GetBytes("AlSaqr.Accounting.Settings.v1"));
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private static string AppDirectory =>
        Environment.GetEnvironmentVariable("ALSAQR_ACCOUNTING_DATA_DIR") ??
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AlSaqrAccounting");
    private static string SettingsPath => Path.Combine(AppDirectory, "system-settings.bin");

    public SystemSettings Load()
    {
        if (!File.Exists(SettingsPath)) return new SystemSettings();
        var protectedBytes = File.ReadAllBytes(SettingsPath);
        var jsonBytes = ProtectedData.Unprotect(protectedBytes, Entropy, DataProtectionScope.CurrentUser);
        return JsonSerializer.Deserialize<SystemSettings>(jsonBytes, JsonOptions)
            ?? throw new InvalidDataException("ملف إعدادات النظام فارغ أو غير صالح.");
    }

    public void Save(SystemSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.CompanyName = Required(settings.CompanyName, "اسم المنشأة", 160);
        settings.CommercialRegistration = Clean(settings.CommercialRegistration, 80);
        settings.VatNumber = Clean(settings.VatNumber, 80);
        settings.Address = Clean(settings.Address, 250);
        settings.Phone = Clean(settings.Phone, 60);
        settings.ReceiptFooter = Clean(settings.ReceiptFooter, 250);
        settings.Theme = settings.Theme is "Classic" or "Light" or "Dark" ? settings.Theme : "Classic";
        settings.PrinterName = Clean(settings.PrinterName, 250);

        Directory.CreateDirectory(AppDirectory);
        var json = JsonSerializer.SerializeToUtf8Bytes(settings, JsonOptions);
        var protectedBytes = ProtectedData.Protect(json, Entropy, DataProtectionScope.CurrentUser);
        var temporaryPath = SettingsPath + ".tmp";
        File.WriteAllBytes(temporaryPath, protectedBytes);
        File.Move(temporaryPath, SettingsPath, true);
    }

    public static ThemePalette PaletteFor(string? theme) => theme switch
    {
        "Dark" => new ThemePalette(
            Color.FromArgb(17, 24, 39), Color.FromArgb(31, 41, 55),
            Color.FromArgb(212, 167, 44), Color.FromArgb(31, 41, 55),
            Color.FromArgb(243, 244, 246), Color.FromArgb(203, 213, 225)),
        "Light" => new ThemePalette(
            Color.FromArgb(31, 41, 55), Color.FromArgb(55, 90, 127),
            Color.FromArgb(181, 138, 42), Color.FromArgb(250, 250, 252),
            Color.FromArgb(31, 41, 55), Color.FromArgb(90, 98, 110)),
        _ => new ThemePalette(
            Color.FromArgb(18, 32, 52), Color.FromArgb(37, 66, 101),
            Color.FromArgb(190, 151, 61), Color.FromArgb(244, 246, 249),
            Color.FromArgb(18, 32, 52), Color.FromArgb(90, 98, 110))
    };

    private static string Required(string? value, string field, int max)
    {
        var clean = Clean(value, max);
        if (clean.Length == 0) throw new ArgumentException(field + " مطلوب.");
        return clean;
    }

    private static string Clean(string? value, int max)
    {
        var s = (value ?? "").Trim();
        return s.Length <= max ? s : s[..max];
    }
}
