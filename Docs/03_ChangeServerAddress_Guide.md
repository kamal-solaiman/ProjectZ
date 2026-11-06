# دليل تغيير عنوان السيرفر (Server Address)

عنوان السيرفر غير مثبت داخل الكود — يتم قراءته من ملف `appsettings.json` الموجود بجانب ملف `FitohPhone.exe`.

## 1. فتح ملف الإعدادات
افتح الملف:
```
FitohPhone.exe
appsettings.json   ←  في نفس المجلد
```

## 2. تعديل قيمة "Server"
| نوع العنوان | مثال |
|---|---|
| اسم الجهاز + Instance | `MAIN-PC\SQLEXPRESS` |
| اسم الجهاز (Instance افتراضي) | `MAIN-PC` |
| IP + Instance | `192.168.1.10\SQLEXPRESS` |
| IP فقط | `192.168.1.10` |

## 3. أمثلة لملف appsettings.json

### أ) Windows Authentication (الافتراضي):
```json
{
  "ConnectionSettings": {
    "Server": "192.168.1.10\\SQLEXPRESS",
    "Database": "FitohPhone",
    "IntegratedSecurity": true,
    "UserId": "",
    "Password": "",
    "TrustServerCertificate": true,
    "ConnectTimeout": 15
  }
}
```

### ب) SQL Server Authentication:
```json
{
  "ConnectionSettings": {
    "Server": "MAIN-PC\\SQLEXPRESS",
    "Database": "FitohPhone",
    "IntegratedSecurity": false,
    "UserId": "FitohPhoneUser",
    "Password": "StrongPassword123!",
    "TrustServerCertificate": true,
    "ConnectTimeout": 15
  }
}
```

## 4. ملاحظات
- في ملف JSON يجب كتابة الشرطة المائلة `\` بشكل مزدوج `\\` كما في الأمثلة أعلاه.
- بعد تعديل الملف احفظه وأعد تشغيل البرنامج — لا حاجة إلى إعادة بناء أي شيء.
- جهاز العميل لا يحتاج إلى أي تثبيت SQL Server.
