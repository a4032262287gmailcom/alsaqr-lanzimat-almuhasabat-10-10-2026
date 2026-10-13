# أداة إصدار تراخيص الصقر

هذه أداة إصدار خاصة بمالك المنتج. لا تضف ملف المفتاح الخاص إلى مستودع Git أو حزمة العميل.

## الاستخدام

```powershell
dotnet build tools/AlSaqr.LicenseIssuer/AlSaqr.LicenseIssuer.csproj -c Release
dotnet run --project tools/AlSaqr.LicenseIssuer/AlSaqr.LicenseIssuer.csproj -- "C:\Secure\alsaqr-license-private.pem" "C:\Requests\AlSaqr-License-Request.alsaqr-request.json" "اسم العميل" "2027-12-31" "C:\Issued\license.json" "Accounting,POS,Inventory,Reports,Settings"
```

الترخيص الموقع مرتبط بمعرّف جهاز العميل. يضم التطبيق المفتاح العام فقط ويستخدم ECDSA P-256 مع SHA-256 للتحقق. احفظ المفتاح الخاص في تخزين آمن ومشفّر، واحتفظ بنسخة احتياطية آمنة منه؛ فقده يعني تعذر إصدار تراخيص جديدة بالمفتاح نفسه.
