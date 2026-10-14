using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Win32;

namespace AlSaqr.Accounting.Services;

feature/erp-pos-cashier-20261013
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
    public const string ProductName = "خالد للأنظمة المحاسبية";
    public static readonly TimeSpan TrialDuration = TimeSpan.FromDays(30);
    private const string KeyId = "alsaqr-p256-v1";

    // Public key only. The matching private key is owned by the publisher and must never be committed.
    private const string PublicKeyPem = """
-----BEGIN PUBLIC KEY-----
MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEcsc4Xh4w9M62gHgEWznh9De8BtSt
JuXWafqQw3cox0/7vo1hAbePS1f35UghKYXtZzXpjhsho6XV5TQ9LcTZgw==
-----END PUBLIC KEY-----
""";

    private static readonly string Folder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AlSaqrAccounting");
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
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWAREMicrosoftCryptography", false);
            raw = Convert.ToString(key?.GetValue("MachineGuid"), System.Globalization.CultureInfo.InvariantCulture) ?? raw;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or PlatformNotSupportedException) { }
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes("AlSaqrAccounting|" + raw.Trim().ToUpperInvariant()));
        return Convert.ToHexString(bytes)[..32];
    }

    private sealed record TrialState(string MachineId, DateTimeOffset FirstRunUtc, DateTimeOffset LastRunUtc);
=======
internal sealed record LicenseStatus(bool Allowed, bool IsLicensed, int DaysRemaining, string Message, string MachineId);

internal sealed class LicenseService
{
    private const int TrialDays = 14;
    private const string ProductCode = "ALSAQR-ACCOUNTING-DESKTOP";
    private static readonly byte[] Entropy = SHA256.HashData(Encoding.UTF8.GetBytes("AlSaqr.Accounting.LicenseState.v1"));
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private static readonly string AppDirectory =
        Environment.GetEnvironmentVariable("ALSAQR_ACCOUNTING_DATA_DIR") ??
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AlSaqrAccounting");
    private static readonly string StatePath = Path.Combine(AppDirectory, "license-state.bin");

    private const string PublicKeyPem = """
-----BEGIN PUBLIC KEY-----
MIIBojANBgkqhkiG9w0BAQEFAAOCAY8AMIIBigKCAYEArqdXUo5ZS2zk2/WAX9pK
2XCQVMKK4djSS8u9tQxzoXY4jnMUpGW3ysf8S6NZyo9ZSbObAjfAmbbupwkUMG4d
zef4tI8vOZ5rUenZ/KstGkDxw23Ot2KzPL5sQuWCBIqWGYfgrZnhNpcaD9mAbEbI
yJi4Y3aJtgd3BF1NUNz0WVtjUyvvPaOfEfdjlmlKT7mdLSS4c3wNBCTIsfzvKALr
+3/RTegGgbSlUY++FlcAWb3Rq+JZeoHUWm69EtZAtc8xqJMhX5X+ezDnZSPQrxYp
LEpAXLkNcMKS2tPxqX6vNhvrqapzBN7xOSNg5OUg55k7dmQWFJ2aBxkDF7naW5NR
w0Em4riB+QA9qCkZT0lSQfhTijmQy/U2PySJeP6qsrZ0NGrx5cJ9a79yvZsz+/bN
BiS7R8eGdSImfTvU9H6KkASrWBWM9kPthU/8fvBTNupjqaiRn4F90XGNUI63pBEb
7cIwiW9bXL8yWhlo7ApBQDuIF1drN4URyLhPI3v1KjGvAgMBAAE=
-----END PUBLIC KEY-----
""";

    public string MachineId => GetMachineId();

    public LicenseStatus GetStatus()
    {
        var now = DateTimeOffset.UtcNow;
        PersistedState state;
        try { state = LoadOrCreateState(now); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException or JsonException or InvalidDataException)
        {
            return new(false, false, 0, "تعذر قراءة حالة الترخيص المحلية. أدخل ملف ترخيص صحيحًا لإصلاحها. " + ex.GetType().Name, MachineId);
        }
        if (now.AddMinutes(5) < state.LastSeenUtc)
            return new(false, false, 0, "تم اكتشاف رجوع ساعة النظام إلى وقت أقدم من آخر تشغيل. صحّح الساعة ثم أعد فتح البرنامج.", MachineId);

        if (!string.IsNullOrWhiteSpace(state.LicenseEnvelopeJson) &&
            TryReadLicense(state.LicenseEnvelopeJson, out var payload, out var error, verifyMachine: true))
        {
            var expiry = payload!.ExpiresUtc;
            if (now <= expiry)
            {
                SaveState(state with { LastSeenUtc = now });
                return new(true, true, DaysUntil(expiry, now),
                    $"الترخيص مفعل باسم {payload.Customer} حتى {expiry.ToLocalTime():yyyy-MM-dd}.", MachineId);
            }

            SaveState(state with { LastSeenUtc = now });
            return new(false, true, 0, "انتهت صلاحية الترخيص. استورد ملف ترخيص ساريًا للمتابعة.", MachineId);
        }
        else if (!string.IsNullOrWhiteSpace(state.LicenseEnvelopeJson))
        {
            // A malformed or mismatched installed license is not silently treated as an activation.
            var remainingAfterInvalid = RemainingTrialDays(state.FirstStartedUtc, now);
            SaveState(state with { LastSeenUtc = now });
            if (remainingAfterInvalid > 0)
                return new(true, false, remainingAfterInvalid, $"ملف الترخيص المحفوظ غير صالح ({error}). تستمر الفترة التجريبية مؤقتًا.", MachineId);
            return new(false, false, 0, $"ملف الترخيص المحفوظ غير صالح ({error}). يلزم تفعيل صحيح.", MachineId);
        }

        var remaining = RemainingTrialDays(state.FirstStartedUtc, now);
        SaveState(state with { LastSeenUtc = now });
        return remaining > 0
            ? new(true, false, remaining, $"نسخة تجريبية — متبقي {remaining} يومًا من أصل {TrialDays}.", MachineId)
            : new(false, false, 0, "انتهت الفترة التجريبية. أدخل ملف الترخيص الموقّع للمتابعة.", MachineId);
    }

    public void ActivateFromFile(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            throw new FileNotFoundException("حدد ملف ترخيص موجودًا.");

        var envelopeJson = File.ReadAllText(filePath, Encoding.UTF8);
        if (!TryReadLicense(envelopeJson, out var payload, out var error, verifyMachine: true))
            throw new InvalidDataException("تعذر قبول ملف الترخيص: " + error);

        var now = DateTimeOffset.UtcNow;
        if (payload!.IssuedUtc > now.AddMinutes(10))
            throw new InvalidDataException("تاريخ إصدار الترخيص في المستقبل.");
        if (payload.ExpiresUtc <= now)
            throw new InvalidDataException("انتهت صلاحية ملف الترخيص.");
        if (payload.IssuedUtc > payload.ExpiresUtc)
            throw new InvalidDataException("تواريخ الترخيص غير صحيحة.");

        var firstStarted = now;
        try
        {
            if (File.Exists(StatePath))
                firstStarted = LoadState()!.FirstStartedUtc;
        }
        catch
        {
            // A valid, signed, machine-matched license is allowed to repair a corrupt local state.
        }

        SaveState(new PersistedState(firstStarted, now, envelopeJson));
    }

    private static int RemainingTrialDays(DateTimeOffset start, DateTimeOffset now)
    {
        if (now < start) return 0;
        var elapsed = (int)Math.Floor((now - start).TotalDays);
        return Math.Max(0, TrialDays - elapsed);
    }

    private static int DaysUntil(DateTimeOffset expiry, DateTimeOffset now) =>
        Math.Max(0, (int)Math.Ceiling((expiry - now).TotalDays));

    private static bool TryReadLicense(string envelopeJson, out LicensePayload? payload, out string error, bool verifyMachine)
    {
        payload = null;
        error = "تنسيق الملف غير صحيح.";
        try
        {
            var envelope = JsonSerializer.Deserialize<LicenseEnvelope>(envelopeJson, JsonOptions);
            if (envelope is null || string.IsNullOrWhiteSpace(envelope.Payload) || string.IsNullOrWhiteSpace(envelope.Signature))
                return false;

            var payloadBytes = Convert.FromBase64String(envelope.Payload);
            var signature = Convert.FromBase64String(envelope.Signature);
            using var rsa = RSA.Create();
            rsa.ImportFromPem(PublicKeyPem);
            if (!rsa.VerifyData(payloadBytes, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1))
            {
                error = "فشل التحقق من التوقيع الرقمي.";
                return false;
            }

            payload = JsonSerializer.Deserialize<LicensePayload>(payloadBytes, JsonOptions);
            if (payload is null) { error = "محتوى الترخيص فارغ."; return false; }
            if (!string.Equals(payload.Product, ProductCode, StringComparison.Ordinal))
            { error = "الترخيص مخصص لمنتج آخر."; return false; }
            if (string.IsNullOrWhiteSpace(payload.LicenseId) || string.IsNullOrWhiteSpace(payload.Customer))
            { error = "بيانات العميل أو رقم الترخيص ناقصة."; return false; }
            if (verifyMachine && !string.Equals(payload.MachineId, GetMachineId(), StringComparison.OrdinalIgnoreCase))
            { error = "هذا الترخيص مرتبط بجهاز مختلف."; return false; }
            error = "";
            return true;
        }
        catch (Exception ex) when (ex is JsonException or FormatException or CryptographicException or ArgumentException)
        {
            error = "تعذر قراءة التوقيع أو محتوى الملف.";
            return false;
        }
    }

    private static string GetMachineId()
    {
        string? stableId = null;
        try { stableId = Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Cryptography", "MachineGuid", null) as string; }
        catch { /* Registry access may be restricted; a machine-name fallback is used. */ }

        stableId = string.IsNullOrWhiteSpace(stableId)
            ? $"{Environment.MachineName}|{Environment.OSVersion.Platform}|{Environment.ProcessorCount}"
            : stableId.Trim();
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes("ALSAQR|" + stableId));
        return Convert.ToHexString(hash)[..32];
    }

    private static PersistedState LoadOrCreateState(DateTimeOffset now)
    {
        if (File.Exists(StatePath)) return LoadState()!;
        var state = new PersistedState(now, now, null);
        SaveState(state);
        return state;
    }

    private static PersistedState? LoadState()
    {
        if (!File.Exists(StatePath)) return null;
        var protectedBytes = File.ReadAllBytes(StatePath);
        var bytes = ProtectedData.Unprotect(protectedBytes, Entropy, DataProtectionScope.CurrentUser);
        return JsonSerializer.Deserialize<PersistedState>(bytes, JsonOptions)
            ?? throw new InvalidDataException("ملف حالة الترخيص المحلي غير صالح.");
    }

    private static void SaveState(PersistedState state)
    {
        Directory.CreateDirectory(AppDirectory);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(state, JsonOptions);
        var protectedBytes = ProtectedData.Protect(bytes, Entropy, DataProtectionScope.CurrentUser);
        var temporaryPath = StatePath + ".tmp";
        File.WriteAllBytes(temporaryPath, protectedBytes);
        File.Move(temporaryPath, StatePath, true);
    }

    private sealed record LicenseEnvelope(string Payload, string Signature);
    private sealed record LicensePayload(string Product, string LicenseId, string Customer,
        string MachineId, DateTimeOffset IssuedUtc, DateTimeOffset ExpiresUtc, string[]? Features);
    private sealed record PersistedState(DateTimeOffset FirstStartedUtc, DateTimeOffset LastSeenUtc, string? LicenseEnvelopeJson);
 main
}
