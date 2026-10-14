using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Win32;

namespace AlSaqr.Accounting.Services;

internal sealed record LicensePayload(
    string ProductId,
    string Licensee,
    string MachineId,
    DateTimeOffset IssuedAtUtc,
    DateTimeOffset ExpiresAtUtc,
    string[] Features,
    string KeyId);

internal sealed record SignedLicense(LicensePayload Payload, string Signature);
internal sealed record LicenseRequest(string ProductId, string ProductName, string Licensee, string MachineId, DateTimeOffset RequestedAtUtc);
internal sealed record LicenseCheck(bool HasAccess, bool IsLicensed, bool IsTrial, DateTimeOffset? ExpiresAtUtc, string Message, string MachineId);

internal sealed class LicenseService
{
    public const string ProductId = "ALSAQR-ACCOUNTING-DESKTOP";
    public const string ProductName = "الصقر لأنظمة المحاسبة";
    public static readonly TimeSpan TrialDuration = TimeSpan.FromDays(30);
    private const string KeyId = "alsaqr-p256-v1";

    // Public key only. The matching private key is owned by the publisher and must never be committed.
    private const string PublicKeyPem = """
-----BEGIN PUBLIC KEY-----
MFkwEwYHKoZIzj0DAQcDQgAE5Kfm6nn+eXKNq7wOydxul6BfMvvE
sCAuxnK1HxUyg9++ME0Sxjim4iv2nP3Crk+SwzuKVuP/UbC81dfaOR+gWg==
-----END PUBLIC KEY-----
""";

    private static string Folder =>
        Environment.GetEnvironmentVariable("ALSAQR_ACCOUNTING_DATA_DIR") ??
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AlSaqrAccounting");
    private static readonly string LicensePath = Path.Combine(Folder, "license.json");
    private static readonly string TrialPath = Path.Combine(Folder, "trial.bin");
    private static readonly byte[] TrialEntropy = Encoding.UTF8.GetBytes("AlSaqrAccountingTrial-2026-v1");
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };

    public string DeviceId => GetDeviceId();

    public LicenseCheck CheckAccess()
    {
        Directory.CreateDirectory(Folder);
        if (File.Exists(LicensePath))
        {
            try
            {
                var signed = JsonSerializer.Deserialize<SignedLicense>(File.ReadAllText(LicensePath), JsonOptions);
                if (signed is null) return Denied("ملف الترخيص فارغ أو غير صالح.");
                var validation = Validate(signed);
                if (validation is not null) return Denied(validation);
                return new LicenseCheck(true, true, false, signed.Payload.ExpiresAtUtc,
                    $"الترخيص صالح باسم {signed.Payload.Licensee} حتى {signed.Payload.ExpiresAtUtc.ToLocalTime():yyyy-MM-dd}.", DeviceId);
            }
            catch (JsonException) { return Denied("تعذر قراءة ملف الترخيص؛ اختر ملف تفعيل صحيحًا."); }
            catch (FormatException) { return Denied("صيغة توقيع الترخيص غير صالحة."); }
            catch (CryptographicException) { return Denied("تعذر التحقق من التوقيع الرقمي للترخيص."); }
        }

        return CheckTrial(createIfMissing: true);
    }

    public LicenseCheck InspectStatus() => CheckAccess();

    public void ExportActivationRequest(string destinationPath, string licensee)
    {
        if (string.IsNullOrWhiteSpace(destinationPath)) throw new ArgumentException("حدد مسار حفظ طلب الترخيص.");
        var request = new LicenseRequest(ProductId, ProductName, (licensee ?? string.Empty).Trim(),
            DeviceId, DateTimeOffset.UtcNow);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(destinationPath))!);
        File.WriteAllText(destinationPath, JsonSerializer.Serialize(request, JsonOptions), new UTF8Encoding(false));
    }

    public LicenseCheck ImportAndActivate(string licenseFilePath)
    {
        if (string.IsNullOrWhiteSpace(licenseFilePath) || !File.Exists(licenseFilePath))
            throw new FileNotFoundException("حدد ملف ترخيص موجودًا.");
        var signed = JsonSerializer.Deserialize<SignedLicense>(File.ReadAllText(licenseFilePath), JsonOptions)
            ?? throw new InvalidOperationException("ملف الترخيص لا يحتوي على بيانات.");
        var error = Validate(signed);
        if (error is not null) throw new InvalidOperationException(error);
        Directory.CreateDirectory(Folder);
        var temp = LicensePath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(signed, JsonOptions), new UTF8Encoding(false));
        File.Move(temp, LicensePath, true);
        return CheckAccess();
    }

    public LicensePayload CreatePayloadForPublisher(string licensee, DateTimeOffset expiresAtUtc, IEnumerable<string> features)
    {
        if (string.IsNullOrWhiteSpace(licensee)) throw new ArgumentException("اسم الجهة المرخص لها مطلوب.");
        if (expiresAtUtc <= DateTimeOffset.UtcNow) throw new ArgumentException("تاريخ انتهاء الترخيص يجب أن يكون في المستقبل.");
        var normalizedFeatures = (features ?? Array.Empty<string>())
            .Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim())
            .Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToArray();
        return new LicensePayload(ProductId, licensee.Trim(), DeviceId, DateTimeOffset.UtcNow,
            expiresAtUtc.ToUniversalTime(), normalizedFeatures, KeyId);
    }

    public string ExportRequestText(string licensee = "") =>
        JsonSerializer.Serialize(new LicenseRequest(ProductId, ProductName, licensee.Trim(), DeviceId, DateTimeOffset.UtcNow), JsonOptions);

    private string? Validate(SignedLicense signed)
    {
        var p = signed.Payload;
        if (p is null) return "بيانات الترخيص ناقصة.";
        if (!string.Equals(p.ProductId, ProductId, StringComparison.Ordinal))
            return "ملف الترخيص صادر لمنتج مختلف.";
        if (!string.Equals(p.KeyId, KeyId, StringComparison.Ordinal))
            return "معرّف مفتاح الترخيص غير معروف.";
        if (!string.Equals(p.MachineId, DeviceId, StringComparison.OrdinalIgnoreCase))
            return "هذا الترخيص مرتبط بجهاز آخر. صدّر طلب تفعيل جديدًا لهذا الجهاز.";
        if (string.IsNullOrWhiteSpace(p.Licensee)) return "اسم الجهة المرخص لها مفقود.";
        if (p.ExpiresAtUtc <= p.IssuedAtUtc) return "تواريخ صلاحية الترخيص غير صحيحة.";
        var now = DateTimeOffset.UtcNow;
        if (p.IssuedAtUtc > now.AddMinutes(10)) return "تاريخ إصدار الترخيص يقع في المستقبل.";
        if (p.ExpiresAtUtc <= now) return "انتهت صلاحية الترخيص في " + p.ExpiresAtUtc.ToLocalTime().ToString("yyyy-MM-dd") + ".";
        byte[] signature;
        try { signature = Convert.FromBase64String(signed.Signature ?? string.Empty); }
        catch (FormatException) { return "توقيع الترخيص ليس Base64 صالحًا."; }
        using var ecdsa = ECDsa.Create();
        ecdsa.ImportFromPem(PublicKeyPem);
        var canonicalPayload = JsonSerializer.SerializeToUtf8Bytes(p, JsonOptions);
        if (!ecdsa.VerifyData(canonicalPayload, signature, HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation))
            return "التوقيع الرقمي غير صحيح أو تم تعديل بيانات الترخيص.";
        return null;
    }

    private LicenseCheck CheckTrial(bool createIfMissing)
    {
        TrialState state;
        if (!File.Exists(TrialPath))
        {
            if (!createIfMissing) return Denied("لا يوجد ترخيص مفعل.");
            state = new TrialState(DeviceId, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
            SaveTrial(state);
        }
        else
        {
            try
            {
                var clear = ProtectedData.Unprotect(File.ReadAllBytes(TrialPath), TrialEntropy, DataProtectionScope.CurrentUser);
                state = JsonSerializer.Deserialize<TrialState>(clear)
                    ?? throw new InvalidOperationException("بيانات الفترة التجريبية غير صالحة.");
            }
            catch (Exception ex) when (ex is CryptographicException or JsonException or IOException)
            {
                return Denied("تعذر التحقق من سجل الفترة التجريبية. يلزم تفعيل الترخيص.");
            }
        }

        if (!string.Equals(state.MachineId, DeviceId, StringComparison.OrdinalIgnoreCase))
            return Denied("الفترة التجريبية مرتبطة بجهاز آخر.");
        var now = DateTimeOffset.UtcNow;
        if (now < state.LastRunUtc.AddMinutes(-5))
            return Denied("تم رصد تغيير في ساعة الجهاز إلى وقت سابق. صحح ساعة Windows أو فعّل الترخيص.");
        var expires = state.FirstRunUtc.Add(TrialDuration);
        if (now >= expires)
            return Denied("انتهت الفترة التجريبية لمدة 30 يومًا. استورد ملف الترخيص أو أنشئ طلب تفعيل.");
        SaveTrial(state with { LastRunUtc = now });
        return new LicenseCheck(true, false, true, expires,
            $"فترة تجريبية — متبقي {(int)Math.Ceiling((expires - now).TotalDays)} يومًا. الجهاز: {DeviceId}", DeviceId);
    }

    private void SaveTrial(TrialState state)
    {
        Directory.CreateDirectory(Folder);
        var encrypted = ProtectedData.Protect(JsonSerializer.SerializeToUtf8Bytes(state), TrialEntropy, DataProtectionScope.CurrentUser);
        var temp = TrialPath + ".tmp";
        File.WriteAllBytes(temp, encrypted);
        File.Move(temp, TrialPath, true);
    }

    private static LicenseCheck Denied(string message) => new(false, false, false, null, message, GetDeviceId());

    private static string GetDeviceId()
    {
        var raw = Environment.MachineName;
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Cryptography", false);
            raw = Convert.ToString(key?.GetValue("MachineGuid"), System.Globalization.CultureInfo.InvariantCulture) ?? raw;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or PlatformNotSupportedException) { }
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes("AlSaqrAccounting|" + raw.Trim().ToUpperInvariant()));
        return Convert.ToHexString(bytes)[..32];
    }

    private sealed record TrialState(string MachineId, DateTimeOffset FirstRunUtc, DateTimeOffset LastRunUtc);
}
