using ProjectZ.Models;
using ProjectZ.Services;
void Check(bool condition, string name) { if (!condition) throw new Exception(name); Console.WriteLine("PASS: " + name); }
const string seeded = "PBKDF2-SHA256$600000$x5WAFq/7zUJDY2+DJ1UrNg==$7VWytRxWmltTzxexDmw3lc4XPLcXWoRgve3NMtqgtDE=";
const string legacySeeded = "PBKDF2SHA256$100000$AAECAwQFBgcICQoLDA0ODw==$zSgEyjn+zUBGRlBk35ROLSGNjbk2Ys0/R6eaIsRtiKY=";
Check(PasswordHasher.Verify("Admin@123", seeded), "SQL seed matches default password");
Check(PasswordHasher.Verify("Admin@123", legacySeeded), "Legacy 100k SQL seed matches default password");
Check(!PasswordHasher.Verify("wrong", seeded), "Wrong password rejected");
Check(!PasswordHasher.Verify("wrong", legacySeeded), "Wrong password rejected (legacy seed)");
var first = PasswordHasher.Hash("اختبار قوي");
Check(PasswordHasher.Verify("اختبار قوي", first), "Unicode round trip");
Check(first != PasswordHasher.Hash("اختبار قوي"), "Independent salts");
foreach (var invalid in new[] { "", "broken", "PBKDF2-SHA256$0$YQ==$YQ==", "PBKDF2-SHA256$50000$YQ==$YQ==", "PBKDF2-SHA256$999999999$YQ==$YQ==", "PBKDF2-SHA256$600000$!$!" })
    Check(!PasswordHasher.Verify("x", invalid), "Malformed hash rejected: " + invalid);

// --- Store identity: the store name is the program name everywhere -------------------------
var identity = StoreIdentity.Current;
Check(identity.Name == "ProjectZ", "Default program name is the seeded store name");
Check(identity.WindowTitle == "ProjectZ", "Window title follows the store name");

void CheckRenamed(string name, string tagline, string address, string phone, string phoneAlt, string email)
{
    StoreIdentity.Apply(new StoreProfile
    {
        StoreName = name,
        Tagline = tagline,
        Address = address,
        Phone = phone,
        PhoneAlt = phoneAlt,
        Email = email,
        ServiceCenterName = "مركز الصيانة والخدمة",
        FooterNote = StoreProfile.DefaultFooterNote,
        CurrencyLabel = "ج.م",
        CurrencyCode = "EGP",
        DefaultLowStockAlert = 7
    });
}

CheckRenamed("NewPhones", "للموبايل والإكسسوارات", "شارع الجمهورية، كفر الشيخ", "01000000000", "01200000000", "info@newphones.test");
Check(identity.Name == "NewPhones", "Saving a new store name renames the program immediately");
Check(identity.WindowTitle == "NewPhones", "Window title follows the renamed program");
Check(identity.Headline == "NewPhones للموبايل والإكسسوارات", "Receipt headline uses the new store name");
Check(identity.ServiceCenterHeader.StartsWith("NewPhones —", StringComparison.Ordinal), "Maintenance receipt header is renamed");
Check(identity.ContactLine.Contains("كفر الشيخ", StringComparison.Ordinal)
      && identity.ContactLine.Contains("01000000000", StringComparison.Ordinal)
      && identity.ContactLine.Contains("01200000000", StringComparison.Ordinal), "Receipt contact line carries address and both phones");
Check(!identity.ContactLine.Contains("01000000000 / 01000000000", StringComparison.Ordinal), "Duplicate phone numbers are not repeated");
Check(identity.DefaultLowStockAlert == 7, "General settings feed the default low-stock alert");
Check(PosFormatting.FormatCurrency(1250m).EndsWith("ج.م", StringComparison.Ordinal), "Amount formatter carries the configured currency label");

var renamedRaised = 0;
void Count() => renamedRaised++;
StoreIdentity.Changed += Count;
CheckRenamed("آخر اسم", "", "", "", "", "");
Check(renamedRaised == 1, "Each saved identity notifies open screens once");
StoreIdentity.Disconnect(Count);
CheckRenamed("Third", "", "", "", "", "");
Check(renamedRaised == 1, "A detached screen stops receiving identity changes");

// Headline falls back to the plain name when no tagline is configured.
Check(StoreIdentity.Current.Headline == "Third", "Headline omits an empty tagline");

// Blank store names never produce a blank program name.
var defaults = StoreProfile.Defaults;
Check(defaults.StoreName == "ProjectZ" && defaults.CurrencyLabel == "ج.م" && defaults.DefaultLowStockAlert == StoreProfile.DefaultLowStockAlertValue,
    "Fallback profile keeps the ProjectZ defaults");

// Closing a screen detaches it from the static identity notifications, but must not re-raise the
// instance Disconnected event from inside its own handler (that recursed until a stack overflow).
var closeNotifications = 0;
void OnScreenClosing()
{
    closeNotifications++;
    StoreIdentity.Disconnect(OnScreenClosing);
}
StoreIdentity.Current.Disconnected += OnScreenClosing;
StoreIdentity.NotifyClosing();
StoreIdentity.Current.Disconnected -= OnScreenClosing;
Check(closeNotifications == 1, "Closing a screen does not recurse through StoreIdentity.NotifyClosing");
