using FitohPhone.Services;
void Check(bool condition, string name) { if (!condition) throw new Exception(name); Console.WriteLine("PASS: " + name); }
const string seeded = "PBKDF2-SHA256$600000$x5WAFq/7zUJDY2+DJ1UrNg==$7VWytRxWmltTzxexDmw3lc4XPLcXWoRgve3NMtqgtDE=";
Check(PasswordHasher.Verify("Admin@123", seeded), "SQL seed matches default password");
Check(!PasswordHasher.Verify("wrong", seeded), "Wrong password rejected");
var first = PasswordHasher.Hash("اختبار قوي");
Check(PasswordHasher.Verify("اختبار قوي", first), "Unicode round trip");
Check(first != PasswordHasher.Hash("اختبار قوي"), "Independent salts");
foreach (var invalid in new[] { "", "broken", "PBKDF2-SHA256$0$YQ==$YQ==", "PBKDF2-SHA256$999999999$YQ==$YQ==", "PBKDF2-SHA256$600000$!$!" })
    Check(!PasswordHasher.Verify("x", invalid), "Malformed hash rejected: " + invalid);
