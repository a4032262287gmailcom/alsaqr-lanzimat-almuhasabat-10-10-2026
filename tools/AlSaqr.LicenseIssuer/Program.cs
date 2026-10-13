using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

internal sealed record LicenseRequest(string ProductId, string ProductName, string Licensee, string MachineId, DateTimeOffset RequestedAtUtc);
internal sealed record LicensePayload(string ProductId, string Licensee, string MachineId, DateTimeOffset IssuedAtUtc, DateTimeOffset ExpiresAtUtc, string[] Features, string KeyId);
internal sealed record SignedLicense(LicensePayload Payload, string Signature);

internal static class Program
{
    // Must be the same public key embedded in Services/LicenseService.cs.
    // The private counterpart is supplied externally and never stored in this repository.
    private const string ApplicationPublicKeyPem = """
-----BEGIN PUBLIC KEY-----
MFkwEwYHKoZIzj0DAQcDQgAE5Kfm6nn+eXKNq7wOydxul6BfMvvE
sCAuxnK1HxUyg9++ME0Sxjim4iv2nP3Crk+SwzuKVuP/UbC81dfaOR+gWg==
-----END PUBLIC KEY-----
""";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private static int Main(string[] args)
    {
        if (args.Length < 5)
        {
            Console.Error.WriteLine("الاستخدام:");
            Console.Error.WriteLine("AlSaqr.LicenseIssuer <private-key.pem> <activation-request.json> <licensee> <expiry-yyyy-MM-dd> <output-license.json> [features]");
            Console.Error.WriteLine("مثال features: Accounting,POS,Inventory,Reports");
            return 2;
        }

        try
        {
            var privateKeyPath = Path.GetFullPath(args[0]);
            var requestPath = Path.GetFullPath(args[1]);
            var licensee = args[2].Trim();
            var expiryDate = DateTime.ParseExact(args[3], "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal).Date.AddDays(1).AddTicks(-1);
            var outputPath = Path.GetFullPath(args[4]);
            var features = (args.Length >= 6 ? args[5] : "Accounting,POS,Inventory,Reports,Settings")
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToArray();

            if (licensee.Length == 0) throw new ArgumentException("اسم العميل/المنشأة مطلوب.");
            if (!File.Exists(privateKeyPath)) throw new FileNotFoundException("لم يتم العثور على المفتاح الخاص.", privateKeyPath);
            if (!File.Exists(requestPath)) throw new FileNotFoundException("لم يتم العثور على طلب التفعيل.", requestPath);
            if (expiryDate <= DateTime.UtcNow) throw new ArgumentException("تاريخ الانتهاء يجب أن يكون في المستقبل.");

            var request = JsonSerializer.Deserialize<LicenseRequest>(File.ReadAllText(requestPath), JsonOptions)
                ?? throw new InvalidOperationException("ملف طلب التفعيل غير صالح.");
            if (!string.Equals(request.ProductId, "ALSAQR-ACCOUNTING-DESKTOP", StringComparison.Ordinal))
                throw new InvalidOperationException("طلب التفعيل صادر لمنتج مختلف.");
            if (request.MachineId.Length != 32 || !request.MachineId.All(Uri.IsHexDigit))
                throw new InvalidOperationException("معرّف الجهاز في طلب التفعيل غير صالح؛ المتوقع 32 خانة سداسية عشرية.");

            var payload = new LicensePayload(
                "ALSAQR-ACCOUNTING-DESKTOP",
                licensee,
                request.MachineId,
                DateTimeOffset.UtcNow,
                new DateTimeOffset(DateTime.SpecifyKind(expiryDate, DateTimeKind.Utc)),
                features,
                "alsaqr-p256-v1");

            var bytes = JsonSerializer.SerializeToUtf8Bytes(payload, JsonOptions);
            using var ecdsa = ECDsa.Create();
            ecdsa.ImportFromPem(File.ReadAllText(privateKeyPath));
            EnsureSigningKeyMatchesApplication(ecdsa);
            var signature = ecdsa.SignData(bytes, HashAlgorithmName.SHA256,
                DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
            var signed = new SignedLicense(payload, Convert.ToBase64String(signature));

            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
            File.WriteAllText(outputPath, JsonSerializer.Serialize(signed, JsonOptions), new UTF8Encoding(false));
            Console.WriteLine("تم إنشاء ملف الترخيص الموقع رقميًا:");
            Console.WriteLine(outputPath);
            Console.WriteLine("الجهة: " + licensee);
            Console.WriteLine("الجهاز: " + request.MachineId);
            Console.WriteLine("ينتهي: " + payload.ExpiresAtUtc.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            Console.WriteLine("الميزات: " + string.Join(", ", features));
            Console.WriteLine("تنبيه أمني: لا تنسخ المفتاح الخاص إلى المستودع أو إلى جهاز العميل.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("تعذر إصدار الترخيص: " + ex.Message);
            return 1;
        }
    }

    private static void EnsureSigningKeyMatchesApplication(ECDsa signingKey)
    {
        using var expectedPublicKey = ECDsa.Create();
        expectedPublicKey.ImportFromPem(ApplicationPublicKeyPem);

        var actualPublicBytes = signingKey.ExportSubjectPublicKeyInfo();
        var expectedPublicBytes = expectedPublicKey.ExportSubjectPublicKeyInfo();
        if (!actualPublicBytes.AsSpan().SequenceEqual(expectedPublicBytes))
        {
            throw new CryptographicException(
                "المفتاح الخاص المحدد لا يطابق المفتاح العام المعتمد داخل تطبيق الصقر. لم يتم إصدار ملف الترخيص.");
        }
    }
}
