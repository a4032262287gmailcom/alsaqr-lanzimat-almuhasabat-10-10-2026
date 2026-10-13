# نظام تقارير ERP

هذا المشروع هو نسخة تجريبية حقيقية لعرض وتحليل ملفات التقارير التي تم توليدها عن المشروع ERP.

## ما الذي يعرضه النظام؟
- ملخص إحصائي للمشروع
- قائمة الدوال وملفاتها
- قائمة Event Handlers
- بحث سريع عبر أسماء الملفات، الدوال، Namespace، Behavior Guess
- صفحة تفاصيل للدالة المختارة
- تصدير نتائج البحث إلى CSV

## التشغيل محلياً

```bash
cd report-system
python -m venv .venv
source .venv/bin/activate   # Linux/Mac
# أو على ويندوز: .venv\Scripts\activate
pip install -r requirements.txt
python app.py
```

ثم افتح المتصفح على:

```text
http://localhost:5000
```

## التشغيل عبر Docker

```bash
docker-compose up --build
```

## ملاحظات
- يعتمد التطبيق على الملفات الموجودة في جذر الريبو مثل:
  - ERP_FunctionInventory_*.csv
  - ERP_EventHandlers_*.csv
  - ERP_FunctionDetails_*.txt
- هذا نظام تجريبي ومفيد لك كقاعدة لنظام أكبر لاحقاً.
