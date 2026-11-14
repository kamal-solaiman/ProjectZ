using ProjectZ.Models;
using ProjectZ.Services;
using ProjectZ.ViewModels;

static void Check(bool condition, string name)
{
    if (!condition) throw new Exception("FAIL: " + name);
    Console.WriteLine("PASS: " + name);
}

// Catalog and color math ----------------------------------------------------------------------
Check(ThemeCatalog.All.Count == 9, "Eight requested presets plus a full custom studio");
Check(ThemeCatalog.All.Select(theme => theme.Id).Distinct().Count() == 9, "Theme ids are unique");
foreach (var id in new[] { "glassmorphism", "liquid-glass", "dark-glass", "aurora", "holographic", "fui", "chrome", "fluent", "custom" })
    Check(ThemeCatalog.Find(id).Id == id, "Catalog contains " + id);
Check(ThemeCatalog.All.All(theme => theme.Palette.IsValid()), "Every preset has eight valid HEX roles");
Check(Math.Abs(ThemeColorMath.Contrast("#000000", "#FFFFFF") - 21) < .001, "WCAG black/white contrast is 21:1");
Check(ThemeColorMath.Mix("#000000", "#FFFFFF", .5) == "#808080", "sRGB channel mix rounds deterministically");
foreach (var invalid in new[] { "", "#FFF", "#GGGGGG", "147D92", "#147D92FF", null })
    Check(!ThemeColorMath.IsHex(invalid), "Invalid HEX rejected: " + (invalid ?? "null"));
foreach (var theme in ThemeCatalog.All)
{
    var p = theme.Palette;
    Check(ThemeColorMath.Contrast(ThemeColorMath.EnsureTextContrast(p.Text, p.Surface), p.Surface) >= 4.5, theme.Id + " body text can reach 4.5:1");
    Check(ThemeColorMath.Contrast(ThemeColorMath.ReadableInk(p.Accent), p.Accent) >= 4.5, theme.Id + " action text is readable");
}
var repairedWhite = ThemeColorMath.EnsureTextContrast("#FFFFFF", "#FFFFFF");
Check(repairedWhite != "#FFFFFF" && ThemeColorMath.Contrast(repairedWhite, "#FFFFFF") >= 4.5, "White-on-white is repaired to the nearest readable shade");

// Atomic local persistence and recovery ---------------------------------------------------------
var directory = Path.Combine(Path.GetTempPath(), "ProjectZ.Appearance.Tests", Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(directory);
try
{
    var path = Path.Combine(directory, "nested", "appearance.json");
    var store = new AppearanceStore(path);
    var first = store.Load();
    Check(first.Warning is null && first.Settings.ThemeId == "glassmorphism", "Missing file opens the default theme without blocking startup");
    var custom = new AppearanceSettings
    {
        ThemeId = "custom",
        CustomPalette = new ThemePalette("#101820", "#202C38", "#F5F7FA", "#B8C4D0", "#39B8A8", "#4A6074", "#A8E6CF", "#152230"),
        SurfaceOpacity = .82,
        CornerRadius = 19,
        ReduceMotion = true
    };
    store.Save(custom);
    Check(new AppearanceStore(path).Load().Settings == custom, "Custom palette, opacity, radius and motion survive a fresh store instance");
    store.Save(custom with { ThemeId = "dark-glass" });
    Check(File.Exists(path + ".bak"), "Second save retains the previous good file as backup");
    File.WriteAllText(path, "{ broken json");
    var recovered = store.Load();
    Check(recovered.Settings == custom && recovered.Warning is not null, "Corrupt primary recovers the backup with a visible warning");
    File.Delete(path);
    Check(store.Load().Settings == custom, "Missing primary can still recover the backup");
    File.WriteAllText(path, "{\"Version\":99,\"ThemeId\":\"dark-glass\"}");
    File.WriteAllText(path + ".bak", "{\"Version\":99,\"ThemeId\":\"dark-glass\"}");
    var fallback = store.Load();
    Check(fallback.Settings.ThemeId == "glassmorphism" && fallback.Warning is not null, "Unsupported schema falls back safely instead of crashing login");
    var before = File.ReadAllText(path);
    try { store.Save(custom with { SurfaceOpacity = 2 }); throw new Exception("Invalid opacity was accepted"); }
    catch (ArgumentOutOfRangeException) { Check(File.ReadAllText(path) == before, "Invalid save never replaces the last good file"); }
    Check(!Directory.GetFiles(Path.GetDirectoryName(path)!, "*.tmp").Any(), "No temporary file is left after save or rejection");

    // Editor state machine ----------------------------------------------------------------------
    var runtime = new TestRuntime(new AppearanceStore(Path.Combine(directory, "studio.json")));
    var studio = new AppearanceViewModel(runtime);
    studio.SelectedTheme = studio.Themes.First(theme => theme.Id == "dark-glass");
    Check(runtime.Active.ThemeId == "dark-glass" && runtime.Saved.ThemeId == "glassmorphism", "Selection previews globally without overwriting the saved theme");
    Check(studio.HasUnsavedChanges && studio.CanSave, "Unsaved preview enables Save");
    studio.SaveCommand.Execute(null);
    Check(!studio.HasUnsavedChanges && runtime.Saved.ThemeId == "dark-glass", "Save commits the active preview");
    Check(new AppearanceStore(runtime.StoragePath).Load().Settings.ThemeId == "dark-glass", "Saved selection is read back from disk");

    studio.SelectedTheme = studio.Themes.First(theme => theme.Id == "custom");
    Check(studio.IsCustomSelected && studio.Colors.Count == 8, "Custom studio exposes all eight color roles");
    studio.Colors[2].Hex = "#FFFFFF";
    Check(runtime.Active.CustomPalette.Text == "#FFFFFF", "Valid HEX updates the live custom palette");
    Check(studio.ContrastChecks.All(check => check.Ratio >= 4.5), "Contrast report uses effective readable text");
    studio.Colors[2].Hex = "#12";
    Check(studio.HasInvalidColors && !studio.CanSave, "Partial HEX blocks saving");
    var savedBeforeInvalid = runtime.Saved;
    studio.SaveCommand.Execute(null);
    Check(runtime.Saved == savedBeforeInvalid && studio.IsErrorStatus, "Invalid color cannot reach disk and reports an error");
    studio.RestoreSavedCommand.Execute(null);
    Check(!studio.HasInvalidColors && runtime.Active == runtime.Saved, "Restore saved cancels an invalid preview");

    studio.SelectedTheme = studio.Themes.First(theme => theme.Id == "custom");
    studio.OpacityPercent = 20;
    studio.CornerRadius = 99;
    Check(studio.OpacityPercent == 35 && studio.CornerRadius == 24, "Custom sliders clamp to safe ranges");
    studio.ResetCustomCommand.Execute(null);
    Check(runtime.Active.CustomPalette == ThemeCatalog.CustomPalette && studio.OpacityPercent == 100, "Reset custom restores the complete default palette");
    studio.ResetDefaultCommand.Execute(null);
    Check(runtime.Active.ThemeId == "glassmorphism" && studio.HasUnsavedChanges, "Default reset is a preview until saved");
    studio.SaveCommand.Execute(null);

    runtime.FailNextSave = true;
    studio.SelectedTheme = studio.Themes.First(theme => theme.Id == "aurora");
    studio.SaveCommand.Execute(null);
    Check(studio.IsErrorStatus && studio.HasUnsavedChanges && runtime.Active.ThemeId == "aurora", "Disk failure is honest and keeps the live preview available");
    runtime.FailNextSave = false;
    studio.SaveCommand.Execute(null);
    Check(!studio.IsErrorStatus && runtime.Saved.ThemeId == "aurora", "Retry after a disk failure can succeed");
}
finally
{
    Directory.Delete(directory, recursive: true);
}
Console.WriteLine("All appearance tests passed.");

sealed class TestRuntime(AppearanceStore store) : IAppearanceRuntime
{
    public AppearanceSettings Active { get; private set; } = new();
    public AppearanceSettings Saved { get; private set; } = new();
    public string? LoadWarning => null;
    public string StoragePath => store.FilePath;
    public bool FailNextSave { get; set; }
    public void Preview(AppearanceSettings settings) { ThemeCatalog.Validate(settings); Active = settings.Copy(); }
    public void Save()
    {
        if (FailNextSave) throw new IOException("Simulated disk failure");
        store.Save(Active);
        Saved = Active.Copy();
    }
    public void RestoreSaved() => Active = Saved.Copy();
}
