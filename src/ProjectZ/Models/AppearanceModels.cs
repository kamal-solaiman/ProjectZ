using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;

namespace ProjectZ.Models;

/// <summary>Device-local appearance. No credentials, store data or SQL settings belong in this file.</summary>
public sealed record AppearanceSettings
{
    public const int CurrentVersion = 1;
    public int Version { get; init; } = CurrentVersion;
    public string ThemeId { get; init; } = "glassmorphism";
    public ThemePalette CustomPalette { get; init; } = ThemeCatalog.CustomPalette;
    public double SurfaceOpacity { get; init; } = 1;
    public double CornerRadius { get; init; } = 12;
    public bool ReduceMotion { get; init; }
    public AppearanceSettings Copy() => this with { CustomPalette = CustomPalette with { } };
}

public sealed record ThemePalette(
    string Background, string Surface, string Text, string MutedText,
    string Accent, string Border, string Highlight, string Sidebar)
{
    public bool IsValid() => new[] { Background, Surface, Text, MutedText, Accent, Border, Highlight, Sidebar }
        .All(ThemeColorMath.IsHex);
}

public sealed record ThemeDefinition(
    string Id, string Title, string EnglishTitle, string Description, string Signature,
    string Icon, ThemePalette Palette, double SurfaceOpacity, double CornerRadius);

public static partial class ThemeCatalog
{
    public static ThemePalette CustomPalette { get; } = new(
        "#EAF0F6", "#FFFFFF", "#172B41", "#52647A", "#147D92", "#B8C9D8", "#087F8C", "#102E48");

    public static IReadOnlyList<ThemeDefinition> All { get; } =
    [
        new("glassmorphism", "الزجاج الشفاف", "Glassmorphism",
            "طبقات هادئة وشفافية ناعمة، مع حواف مضيئة وخلفية بلون السماء.",
            "شفافية · عمق · نعومة", "\uE737",
            new("#E8F1FA", "#FFFFFF", "#18334C", "#52677E", "#147D92", "#B6CCDF", "#087F8C", "#173C56"), .76, 16),
        new("liquid-glass", "الزجاج السائل", "Liquid Glass",
            "انعكاسات لؤلؤية وانحناءات مضيئة فوق طبقات زجاجية بلون الجليد.",
            "انسياب · انعكاس · لؤلؤ", "\uE793",
            new("#E2EDFA", "#F8FCFF", "#173451", "#506984", "#2879B9", "#A6C9E6", "#167BB5", "#1C4565"), .66, 22),
        new("dark-glass", "الزجاج الداكن", "Dark Glass",
            "زجاج ليلي عميق، نصوص فاتحة ولمسات فيروزية تمنح البيانات وضوحًا.",
            "ليلي · هادئ · مركز", "\uE708",
            new("#0B1220", "#1B293D", "#ECF3FC", "#AFBED2", "#36B8C4", "#40536D", "#79E2D5", "#101C2E"), .83, 16),
        new("aurora", "الشفق القطبي", "Aurora UI",
            "أشرطة ضوئية زمردية وبنفسجية تتحرك بهدوء فوق سماء ليلية.",
            "ضوء · حركة · خيال", "\uE706",
            new("#10182D", "#202B46", "#F0F5FF", "#B7C4DA", "#4BBDA8", "#465779", "#9DE7CE", "#17243C"), .82, 18),
        new("holographic", "التصميم الهولوجرافي", "Holographic UI",
            "ألوان طيفية وحدود قزحية ولمعة رقمية دقيقة على أسطح بلورية.",
            "طيف · بلور · مستقبل", "\uE7F4",
            new("#ECEAF8", "#FCFAFF", "#30294D", "#68617F", "#7862BA", "#C2B5DE", "#7554AD", "#3D355F"), .77, 14),
        new("fui", "واجهات الخيال العلمي", "FUI / HUD",
            "شبكة هندسية، زوايا حادة وبيانات بلون الرادار؛ طابع مركز قيادة.",
            "دقة · هندسة · رادار", "\uE950",
            new("#071722", "#102B38", "#E0F7FA", "#9BBBC7", "#29ABC3", "#326476", "#E9BB64", "#0B2230"), .91, 2),
        new("chrome", "الكروم والمعدن السائل", "Chrome / Liquid Metal",
            "تدرجات فضية مصقولة، ظلال معدنية وخطوط ضوء تعبر الأسطح.",
            "معدن · لمعان · نقاء", "\uE7F8",
            new("#DCE2E9", "#F5F7FA", "#253443", "#566576", "#4C6D88", "#A2AFBD", "#3E6685", "#344656"), .92, 10),
        new("fluent", "تصميم Microsoft", "Fluent Design",
            "أسطح محايدة، لمعة ضوئية عند التفاعل، ولمسة زرقاء بروح Windows.",
            "بساطة · وضوح · انسجام", "\uE782",
            new("#F3F3F3", "#FDFDFD", "#1B1B1B", "#606060", "#005FB8", "#D1D1D1", "#0067C0", "#202C3A"), .96, 8),
        new("custom", "تصميمك المخصص", "Custom Studio",
            "ثمانية أدوار لونية، شفافية وانحناءات قابلة للتعديل مع فحص مباشر للوضوح.",
            "ألوانك · اختيارك · هويتك", "\uE790", CustomPalette, 1, 12)
    ];

    public static ThemeDefinition Find(string? id) => All.FirstOrDefault(theme => theme.Id == id)
        ?? throw new ArgumentException("المظهر المحدد غير موجود.", nameof(id));

    public static ThemePalette PaletteFor(AppearanceSettings settings) => settings.ThemeId == "custom"
        ? settings.CustomPalette : Find(settings.ThemeId).Palette;

    public static void Validate(AppearanceSettings settings)
    {
        if (settings.Version != AppearanceSettings.CurrentVersion)
            throw new InvalidDataException("إصدار ملف المظهر غير مدعوم.");
        _ = Find(settings.ThemeId);
        if (!settings.CustomPalette.IsValid())
            throw new FormatException("استخدم ألوانًا بصيغة #RRGGBB، مثل #147D92.");
        if (!double.IsFinite(settings.SurfaceOpacity) || settings.SurfaceOpacity is < .35 or > 1)
            throw new ArgumentOutOfRangeException(nameof(settings), "شفافية الأسطح يجب أن تكون بين 35% و100%.");
        if (!double.IsFinite(settings.CornerRadius) || settings.CornerRadius is < 0 or > 24)
            throw new ArgumentOutOfRangeException(nameof(settings), "انحناء الزوايا يجب أن يكون بين 0 و24.");
    }

    [GeneratedRegex("^#[0-9a-fA-F]{6}$")]
    private static partial Regex HexPattern();
    internal static bool IsHex(string? value) => value is not null && HexPattern().IsMatch(value);
}

/// <summary>Pure, sRGB-aware color operations shared by the theme engine, editor and tests.</summary>
public static class ThemeColorMath
{
    public static bool IsHex(string? value) => ThemeCatalog.IsHex(value);

    public static (byte R, byte G, byte B) Parse(string hex)
    {
        if (!IsHex(hex)) throw new FormatException("صيغة اللون يجب أن تكون #RRGGBB.");
        return (byte.Parse(hex.AsSpan(1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture),
            byte.Parse(hex.AsSpan(3, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture),
            byte.Parse(hex.AsSpan(5, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
    }

    public static string Hex(byte r, byte g, byte b) => $"#{r:X2}{g:X2}{b:X2}";

    public static string Mix(string first, string second, double amount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(amount);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(amount, 1);
        var a = Parse(first); var b = Parse(second);
        return Hex((byte)Math.Round(a.R + (b.R - a.R) * amount),
            (byte)Math.Round(a.G + (b.G - a.G) * amount),
            (byte)Math.Round(a.B + (b.B - a.B) * amount));
    }

    public static double Luminance(string hex)
    {
        var (r, g, b) = Parse(hex);
        static double Channel(byte value)
        {
            var c = value / 255d;
            return c <= .04045 ? c / 12.92 : Math.Pow((c + .055) / 1.055, 2.4);
        }
        return .2126 * Channel(r) + .7152 * Channel(g) + .0722 * Channel(b);
    }

    public static double Contrast(string first, string second)
    {
        var a = Luminance(first); var b = Luminance(second);
        return (Math.Max(a, b) + .05) / (Math.Min(a, b) + .05);
    }

    public static string ReadableInk(string background) => Contrast(background, "#FFFFFF") >= 4.5
        ? "#FFFFFF" : "#111827";

    /// <summary>Keep the chosen hue where possible, moving lightness only as far as needed for 4.5:1.</summary>
    public static string EnsureTextContrast(string foreground, string background)
    {
        if (Contrast(foreground, background) >= 4.5) return foreground;
        var target = Luminance(background) > .179 ? "#000000" : "#FFFFFF";
        for (var step = 1; step <= 100; step++)
        {
            var candidate = Mix(foreground, target, step / 100d);
            if (Contrast(candidate, background) >= 4.5) return candidate;
        }
        return target;
    }
}
