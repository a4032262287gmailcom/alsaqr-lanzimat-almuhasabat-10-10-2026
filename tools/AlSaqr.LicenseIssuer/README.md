# أداة إصدار تراخيص الصقر

هذه أداة إصدار التراخيص الرسمية للصقر، وتستخدم ECDSA P-256 مع SHA-256 للتوقيع. لا تضف ملف المفتاح الخاص إلى مستودع Git أو حزمة العميل. المفتاح الخاص المطابق للمفتاح العام المضمّن في التطبيق محفوظ خارج المستودع.

## الاستخدام

```powershell
dotnet build tools/AlSaqr.LicenseIssuer/AlSaqr.LicenseIssuer.csproj -c Release
dotnet run --project tools/AlSaqr.LicenseIssuer/AlSaqr.LicenseIssuer.csproj -- "C:\Secure\AlSaqr-License-PrivateKey-ECDSA.pem" "C:\Requests\AlSaqr-License-Request.alsaqr-request.json" "اسم العميل" "2027-12-31" "C:\Issued\license.json" "Accounting,POS,Inventory,Reports,Settings"
```

الترخيص الموقع مرتبط بمعرّف جهاز العميل. يضم التطبيق المفتاح العام فقط ويستخدم ECDSA P-256 مع SHA-256 للتحقق. احفظ المفتاح الخاص في تخزين آمن ومشفّر، واحتفظ بنسخة احتياطية آمنة منه؛ فقده يعني تعذر إصدار تراخيص جديدة بالمفتاح نفسه.


**تأكد من استخدام مفتاح ECDSA P-256 المطابق للمفتاح العام في `Services/LicenseService.cs`.** ملف `tools/New-AlSaqrLicense.ps1` القديم كان يصدر ترخيص RSA ببنية مختلفة، ولذلك أزيل لمنع إصدار ملفات لا يستطيع التطبيق التحقق منها.
