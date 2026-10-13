using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Win32;

namespace AlSaqr.Accounting.Services;

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
MIIBojANBgkqhkiG9w0BAQEFAAOCAYEArqdXUo5ZS2zk2/WAX9pK
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
        var state = LoadOrCreateState(now);
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
}
