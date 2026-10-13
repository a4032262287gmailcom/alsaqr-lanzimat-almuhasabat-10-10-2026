# أداة إصدار تراخيص الصقر

هذه أداة إصدار التراخيص الرسمية للصقر، وتستخدم ECDSA P-256 مع SHA-256. لا تضف ملف المفتاح الخاص إلى مستودع Git أو حزمة العميل. استخدم المفتاح الخاص المطابق للمفتاح العام المضمّن في `Services/LicenseService.cs`.

## الاستخدام

```powershell
dotnet build tools/AlSaqr.LicenseIssuer/AlSaqr.LicenseIssuer.csproj -c Release
dotnet run --project tools/AlSaqr.LicenseIssuer/AlSaqr.LicenseIssuer.csproj -- "C:\Secure\AlSaqr-License-PrivateKey-ECDSA.pem" "C:\Requests\AlSaqr-License-Request.alsaqr-request.json" "اسم العميل" "2027-12-31" "C:\Issued\license.json" "Accounting,POS,Inventory,Reports,Settings"
```

الترخيص الموقع مرتبط بمعرّف جهاز العميل. يضم التطبيق المفتاح العام فقط ويستخدم ECDSA P-256 مع SHA-256 للتحقق. احفظ المفتاح الخاص في تخزين آمن ومشفّر، واحتفظ بنسخة احتياطية آمنة منه؛ فقده يعني تعذر إصدار تراخيص جديدة بالمفتاح نفسه.


لا تستخدم سكربت إصدار RSA القديم؛ فهو يكتب بنية مختلفة ولا يتوافق مع مدقق الترخيص داخل التطبيق. احتفظ بمفتاح ECDSA الخاص خارج المستودع، مع نسخة احتياطية مشفّرة.


قبل الإصدار، تتحقق الأداة من أن المفتاح الخاص المحدد يطابق المفتاح العام المضمن في تطبيق الصقر. إذا كان المفتاح مختلفًا، تتوقف العملية ولا تنشئ ملف ترخيص غير صالح. كما تتحقق من أن معرّف الجهاز في طلب التفعيل مكوّن من 32 خانة سداسية عشرية.
