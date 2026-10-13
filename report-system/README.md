# تقرير ERP Dashboard

هذا النظام مصمم لتحليل ملفات التقارير المولّدة للمشروع ERP:

- ERP_FunctionInventory_*.csv
- ERP_EventHandlers_*.csv
- ERP_FunctionDetails_*.txt

## المميزات
- عرض ملخص عام للمشروع
- عدد الدوال، الأنواع، وEvent Hookups
- بحث سريع عبر اسم الملف أو الدالة أو namespace
- عرض أكثر الـNamespaces وBehavior Guess شيوعًا
- جدول للدوال وEvent Handlers

## التشغيل

```bash
cd report-system
pip install -r requirements.txt
python app.py
```

ثم افتح المتصفح على:

```text
http://localhost:5000
```

## ملاحظة
هذا النظام يعتمد على التقارير الموجودة في جذر الريبو، ولا يضيف كود التطبيق نفسه.
