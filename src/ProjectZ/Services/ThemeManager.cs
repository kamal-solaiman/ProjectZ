using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;
using ProjectZ.Helpers;
using ProjectZ.Models;

namespace ProjectZ.Services;

/// <summary>
/// Updates shared WPF resources in place. DynamicResource references throughout the application,
/// including already-open dialogs, pick up the change without recreating screens or losing form data.
/// Transparency is composited inside the application, not behind the desktop; printed paper is isolated.
/// </summary>
public sealed class ThemeManager(AppearanceStore store) : ObservableObject, IAppearanceRuntime
{
    /// <summary>Used by the XAML resource graph; Initialize loads the saved file before any window opens.</summary>
    public ThemeManager() : this(new AppearanceStore()) { }
    public static ThemeManager Instance { get; } = new();

    public static IReadOnlyList<string> ColorKeys { get; } =
    [
        "Background", "Surface", "SurfaceRaised", "Input", "Subtle", "Text", "Muted", "Heading",
        "Border", "BorderStrong", "Accent", "AccentInk", "AccentSoft", "AccentSoftInk", "OnAccent",
        "Highlight", "Navigation", "NavigationRaised", "NavigationInk", "NavigationMuted", "OnNavigation",
        "Hover", "Selection", "Success", "SuccessInk", "SuccessSoft", "SuccessBorder", "OnSuccess",
        "Danger", "DangerInk", "DangerSoft", "DangerBorder", "OnDanger",
        "Warning", "WarningInk", "WarningSoft", "WarningBorder", "OnWarning",
        "Info", "InfoInk", "InfoSoft", "InfoBorder", "OnInfo",
        "Violet", "VioletInk", "VioletSoft", "VioletBorder", "OnViolet", "Shadow"
    ];

    public AppearanceStore Store { get; } = store;
    public string StoragePath => Store.FilePath;
    public AppearanceSettings Saved { get; private set; } = new();
    public AppearanceSettings Active { get; private set; } = new();
    public string? LoadWarning { get; private set; }
    public string Treatment => Active.ThemeId;
    public bool AnimateBackdrop => !Active.ReduceMotion && SystemParameters.ClientAreaAnimation
        && Active.ThemeId is "aurora" or "liquid-glass";
    public bool IsFui => Active.ThemeId == "fui";
    public bool IsAurora => Active.ThemeId == "aurora";
    public bool IsHolographic => Active.ThemeId == "holographic";
    public bool IsFluent => Active.ThemeId == "fluent";
    public bool IsChrome => Active.ThemeId == "chrome";
    public bool IsLiquid => Active.ThemeId == "liquid-glass";
    public bool HasGlow => Active.ThemeId is "glassmorphism" or "liquid-glass" or "dark-glass" or "aurora" or "holographic";
    public CornerRadius CornerRadius => new(Active.CornerRadius);
    public CornerRadius SmallCornerRadius => new(Math.Max(0, Active.CornerRadius * .55));

    public void Initialize()
    {
        var result = Store.Load();
        Saved = result.Settings.Clone();
        LoadWarning = result.Warning;
        Preview(Saved);
    }

    public void Preview(AppearanceSettings settings)
    {
        ThemeCatalog.Validate(settings);
        Active = settings.Clone();
        ApplyResources(Active);
        Changed(nameof(Active));
        Changed(nameof(Saved));
        Changed(nameof(Treatment));
        Changed(nameof(AnimateBackdrop));
        Changed(nameof(IsFui));
        Changed(nameof(IsAurora));
        Changed(nameof(IsHolographic));
        Changed(nameof(IsFluent));
        Changed(nameof(IsChrome));
        Changed(nameof(IsLiquid));
        Changed(nameof(HasGlow));
        Changed(nameof(CornerRadius));
        Changed(nameof(SmallCornerRadius));
    }

    public void Save()
    {
        Store.Save(Active);
        Saved = Active.Clone();
        Changed(nameof(Saved));
    }

    public void RestoreSaved() => Preview(Saved);

    private static void ApplyResources(AppearanceSettings settings)
    {
        var resources = Application.Current.Resources;
        var definition = ThemeCatalog.Find(settings.ThemeId);
        var p = ThemeCatalog.PaletteFor(settings);
        var dark = ThemeColorMath.Luminance(p.Surface) < .179;
        var polarity = dark ? "#FFFFFF" : "#000000";
        var opacity = settings.ThemeId == "custom" ? settings.SurfaceOpacity : definition.SurfaceOpacity;
        var text = ThemeColorMath.EnsureTextContrast(p.Text, p.Surface);
        var muted = ThemeColorMath.EnsureTextContrast(p.MutedText, p.Surface);
        var accentInk = ThemeColorMath.EnsureTextContrast(p.Accent, p.Surface);
        var navInk = ThemeColorMath.ReadableInk(p.Sidebar);
        var colors = new Dictionary<string, string>
        {
            ["Background"] = p.Background,
            ["Surface"] = p.Surface,
            ["SurfaceRaised"] = Mix(p.Surface, polarity, .045),
            ["Input"] = Mix(p.Surface, polarity, .025),
            ["Subtle"] = Mix(p.Surface, p.Background, .48),
            ["Text"] = text,
            ["Muted"] = muted,
            ["Heading"] = ThemeColorMath.EnsureTextContrast(Mix(text, p.Accent, .12), p.Surface),
            ["Border"] = p.Border,
            ["BorderStrong"] = Mix(p.Border, text, .22),
            ["Accent"] = p.Accent,
            ["AccentInk"] = accentInk,
            ["AccentSoft"] = Mix(p.Surface, p.Accent, .12),
            ["AccentSoftInk"] = ThemeColorMath.EnsureTextContrast(p.Accent, Mix(p.Surface, p.Accent, .12)),
            ["OnAccent"] = ThemeColorMath.ReadableInk(p.Accent),
            ["Highlight"] = ThemeColorMath.EnsureTextContrast(p.Highlight, p.Sidebar),
            ["Navigation"] = p.Sidebar,
            ["NavigationRaised"] = Mix(p.Sidebar, navInk, .10),
            ["NavigationInk"] = navInk,
            ["NavigationMuted"] = ThemeColorMath.EnsureTextContrast(Mix(navInk, p.Sidebar, .24), p.Sidebar),
            ["OnNavigation"] = navInk,
            ["Hover"] = Mix(p.Surface, p.Accent, .10),
            ["Selection"] = Mix(p.Surface, p.Accent, .20),
            ["Shadow"] = dark ? "#000000" : "#243B53"
        };

        void Semantic(string name, string light, string night)
        {
            var basis = dark ? night : light;
            var soft = Mix(p.Surface, basis, .13);
            colors[name] = basis;
            colors[name + "Ink"] = ThemeColorMath.EnsureTextContrast(basis, soft);
            colors[name + "Soft"] = soft;
            colors[name + "Border"] = Mix(p.Surface, basis, .42);
            colors["On" + name] = ThemeColorMath.ReadableInk(basis);
        }
        Semantic("Success", "#0F766E", "#5CDEB5");
        Semantic("Danger", "#B42318", "#FF9393");
        Semantic("Warning", "#B45309", "#F5CA72");
        Semantic("Info", "#1D4ED8", "#8CB9FF");
        Semantic("Violet", "#6D28D9", "#C3A7FF");

        foreach (var key in ColorKeys)
        {
            var color = ColorFrom(colors[key], key == "Shadow" ? (byte)(dark ? 100 : 35) : byte.MaxValue);
            resources["Theme.Color." + key] = color;
            resources["Theme.Brush." + key] = Frozen(new SolidColorBrush(color));
        }

        // The opaque surface color remains available for contrast calculations; only the paint is translucent.
        resources["Theme.Brush.Surface"] = SurfacePaint(p.Surface, opacity, settings.ThemeId, dark);
        resources["Theme.Brush.SurfaceRaised"] = SurfacePaint(colors["SurfaceRaised"], Math.Min(1, opacity + .08), settings.ThemeId, dark);
        resources["Theme.Brush.Input"] = Frozen(new SolidColorBrush(ColorFrom(colors["Input"], (byte)Math.Round(255 * Math.Min(1, opacity + .16)))));
        resources["Theme.Brush.Window"] = Backdrop(settings.ThemeId, p);
        resources["Theme.Brush.PanelBorder"] = PanelBorder(settings.ThemeId, p, dark);
        resources["Theme.Brush.Sheen"] = Frozen(new LinearGradientBrush(
            new GradientStopCollection
            {
                new(ColorFrom("#FFFFFF", (byte)(dark ? 24 : 90)), 0),
                new(ColorFrom("#FFFFFF", 0), .48),
                new(ColorFrom(p.Highlight, (byte)(dark ? 10 : 20)), 1)
            }, new Point(0, 0), new Point(.7, 1)));
        resources["Theme.Brush.Reveal"] = Frozen(new RadialGradientBrush(
            ColorFrom(p.Highlight, 55), ColorFrom(p.Accent, 0))
            { GradientOrigin = new Point(.8, .1), Center = new Point(.8, .1), RadiusX = .85, RadiusY = 1.2 });
        resources["Theme.Brush.BackdropA"] = Frozen(new SolidColorBrush(ColorFrom(GlowColor(settings.ThemeId, p, true), 100)));
        resources["Theme.Brush.BackdropB"] = Frozen(new SolidColorBrush(ColorFrom(GlowColor(settings.ThemeId, p, false), 90)));
        resources["Theme.Brush.Grid"] = GridPaint(settings.ThemeId, p.Highlight);
        resources["Theme.CornerRadius"] = new CornerRadius(settings.CornerRadius);
        resources["Theme.SmallCornerRadius"] = new CornerRadius(Math.Max(0, settings.CornerRadius * .55));
        resources["Theme.PanelCornerRadius"] = new CornerRadius(Math.Min(26, settings.CornerRadius + 4));
        resources["Theme.SurfaceOpacity"] = opacity;
        resources["Theme.MotionDuration"] = TimeSpan.FromMilliseconds(settings.ReduceMotion || !SystemParameters.ClientAreaAnimation ? 0 : 180);

        // Legacy surface aliases must share the actual translucent/gradient paint, not just its opaque color.
        // Overriding at application level keeps DynamicResource listeners in already-open windows alive.
        var aliasKeys = resources.MergedDictionaries
            .SelectMany(dictionary => dictionary.Keys.Cast<object>().OfType<string>())
            .Where(key => key.StartsWith("Theme.Legacy.", StringComparison.Ordinal))
            .Distinct().ToArray();
        foreach (var key in aliasKeys)
        {
            var role = key[(key.LastIndexOf('.') + 1)..];
            if (role is "Surface" or "SurfaceRaised" or "Input" or "Subtle")
                resources[key] = resources["Theme.Brush." + role];
        }
    }

    private static string Mix(string a, string b, double amount) => ThemeColorMath.Mix(a, b, amount);

    private static Brush SurfacePaint(string surface, double opacity, string theme, bool dark)
    {
        var alpha = (byte)Math.Round(255 * opacity);
        if (theme == "chrome")
            return Frozen(new LinearGradientBrush(
                new GradientStopCollection
                {
                    new(ColorFrom(Mix(surface, "#FFFFFF", .65), alpha), 0),
                    new(ColorFrom(surface, alpha), .24),
                    new(ColorFrom(Mix(surface, "#65788C", .16), alpha), .49),
                    new(ColorFrom(Mix(surface, "#FFFFFF", .48), alpha), .53),
                    new(ColorFrom(surface, alpha), .82),
                    new(ColorFrom(Mix(surface, "#FFFFFF", .3), alpha), 1)
                }, new Point(0, 0), new Point(.25, 1)));
        if (theme is "liquid-glass" or "holographic")
            return Frozen(new LinearGradientBrush(
                ColorFrom(Mix(surface, "#FFFFFF", dark ? .04 : .3), alpha),
                ColorFrom(surface, alpha), new Point(0, 0), new Point(1, 1)));
        return Frozen(new SolidColorBrush(ColorFrom(surface, alpha)));
    }

    private static Brush Backdrop(string theme, ThemePalette p) => theme switch
    {
        "chrome" => Frozen(new LinearGradientBrush(new GradientStopCollection
        {
            new(ColorFrom(Mix(p.Background, "#FFFFFF", .7)), 0),
            new(ColorFrom(p.Background), .30),
            new(ColorFrom(Mix(p.Background, "#6B7C8F", .25)), .48),
            new(ColorFrom(Mix(p.Background, "#FFFFFF", .85)), .58),
            new(ColorFrom(p.Background), .82),
            new(ColorFrom(Mix(p.Background, "#FFFFFF", .45)), 1)
        }, new Point(0, 0), new Point(1, .65))),
        "fluent" => Frozen(new SolidColorBrush(ColorFrom(p.Background))),
        "fui" => Frozen(new SolidColorBrush(ColorFrom(p.Background))),
        "holographic" => Frozen(new LinearGradientBrush(new GradientStopCollection
        {
            new(ColorFrom(Mix(p.Background, "#BCEFF4", .48)), 0),
            new(ColorFrom(Mix(p.Background, "#E2C9F4", .42)), .45),
            new(ColorFrom(Mix(p.Background, "#F6D8E7", .45)), .75),
            new(ColorFrom(p.Background), 1)
        }, new Point(0, 0), new Point(1, 1))),
        "aurora" => Frozen(new LinearGradientBrush(ColorFrom(p.Background), ColorFrom(Mix(p.Background, "#29365C", .5)), new Point(0, 0), new Point(1, 1))),
        "dark-glass" => Frozen(new LinearGradientBrush(ColorFrom(p.Background), ColorFrom(Mix(p.Background, p.Sidebar, .65)), new Point(0, 0), new Point(1, 1))),
        "custom" => Frozen(new SolidColorBrush(ColorFrom(p.Background))),
        _ => Frozen(new LinearGradientBrush(new GradientStopCollection
        {
            new(ColorFrom(p.Background), 0),
            new(ColorFrom(Mix(p.Background, p.Accent, .13)), .55),
            new(ColorFrom(Mix(p.Background, "#D7CDEF", .38)), 1)
        }, new Point(0, 0), new Point(1, 1)))
    };

    private static Brush PanelBorder(string theme, ThemePalette p, bool dark)
    {
        if (theme is "fluent" or "custom" or "fui")
            return Frozen(new SolidColorBrush(ColorFrom(theme == "fui" ? p.Accent : p.Border, (byte)(theme == "fui" ? 160 : 255))));
        var stops = theme == "holographic"
            ? new[] { "#83DCE8", "#C0A0E8", "#EDB6D2", "#83DCE8" }
            : theme == "chrome"
                ? new[] { "#FFFFFF", "#8192A4", "#FFFFFF", "#A1AFBD" }
                : new[] { "#FFFFFF", p.Border, p.Highlight, "#FFFFFF" };
        var positions = new[] { 0d, .42, .72, 1d };
        var collection = new GradientStopCollection();
        for (var i = 0; i < stops.Length; i++)
            collection.Add(new GradientStop(ColorFrom(stops[i], (byte)(dark ? 115 : 190)), positions[i]));
        return Frozen(new LinearGradientBrush(collection, new Point(0, 0), new Point(1, 1)));
    }

    private static string GlowColor(string theme, ThemePalette p, bool first) => theme switch
    {
        "aurora" => first ? "#28D9AA" : "#9270EF",
        "dark-glass" => first ? "#218BA8" : "#6650AE",
        "holographic" => first ? "#7DE2EC" : "#D8A1E8",
        "liquid-glass" => first ? "#75D5ED" : "#B7A5ED",
        _ => first ? p.Accent : "#B6A1DD"
    };

    private static Brush GridPaint(string theme, string highlight)
    {
        if (theme is not ("fui" or "holographic")) return Brushes.Transparent;
        var geometry = new GeometryGroup();
        geometry.Children.Add(new LineGeometry(new Point(0, 0), new Point(32, 0)));
        if (theme == "fui") geometry.Children.Add(new LineGeometry(new Point(0, 0), new Point(0, 32)));
        var drawing = new GeometryDrawing(null, new Pen(new SolidColorBrush(ColorFrom(highlight, (byte)(theme == "fui" ? 30 : 15))), .6), geometry);
        var brush = new DrawingBrush(drawing)
        {
            TileMode = TileMode.Tile,
            Viewport = new Rect(0, 0, 32, theme == "fui" ? 32 : 6),
            ViewportUnits = BrushMappingMode.Absolute,
            Stretch = Stretch.None
        };
        return Frozen(brush);
    }

    private static Color ColorFrom(string hex, byte alpha = 255)
    {
        var (r, g, b) = ThemeColorMath.Parse(hex);
        return Color.FromArgb(alpha, r, g, b);
    }

    private static T Frozen<T>(T freezable) where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }
}
