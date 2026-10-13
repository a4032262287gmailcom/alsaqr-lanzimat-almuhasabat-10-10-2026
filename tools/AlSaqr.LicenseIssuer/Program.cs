using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

internal sealed record LicenseRequest(string ProductId, string ProductName, string Licensee, string MachineId, DateTimeOffset RequestedAtUtc);
internal sealed record LicensePayload(string ProductId, string Licensee, string MachineId, DateTimeOffset IssuedAtUtc, DateTimeOffset ExpiresAtUtc, string[] Features, string KeyId);
internal sealed record SignedLicense(LicensePayload Payload, string Signature);

internal static class Program
{
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
            if (string.IsNullOrWhiteSpace(request.MachineId))
                throw new InvalidOperationException("طلب التفعيل لا يحتوي على معرّف الجهاز.");

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
}
