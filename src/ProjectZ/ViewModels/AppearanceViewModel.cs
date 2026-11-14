using System.Collections.ObjectModel;
using System.IO;
using ProjectZ.Helpers;
using ProjectZ.Models;
using ProjectZ.Services;

namespace ProjectZ.ViewModels;

public sealed class AppearanceViewModel : ObservableObject
{
    private readonly IAppearanceRuntime runtime;
    private AppearanceSettings draft;
    private ThemeOptionViewModel? selectedTheme;
    private bool refreshing;
    private string statusMessage = "";
    private bool isErrorStatus;

    public IReadOnlyList<ThemeOptionViewModel> Themes { get; }
    public ObservableCollection<ColorEditorViewModel> Colors { get; }
    public ObservableCollection<ContrastCheckViewModel> ContrastChecks { get; } = [];
    public RelayCommand SaveCommand { get; }
    public RelayCommand RestoreSavedCommand { get; }
    public RelayCommand ResetCustomCommand { get; }
    public RelayCommand ResetDefaultCommand { get; }

    public AppearanceViewModel(IAppearanceRuntime? runtime = null)
    {
        this.runtime = runtime ?? new DesignAppearanceRuntime();
        draft = this.runtime.Active.Clone();
        Themes = ThemeCatalog.All.Select(theme => new ThemeOptionViewModel(theme)).ToArray();
        Colors =
        [
            new("Background", "الخلفية", draft.CustomPalette.Background, ChangeColor),
            new("Surface", "البطاقات والأسطح", draft.CustomPalette.Surface, ChangeColor),
            new("Text", "النص الأساسي", draft.CustomPalette.Text, ChangeColor),
            new("MutedText", "النص الثانوي", draft.CustomPalette.MutedText, ChangeColor),
            new("Accent", "الأزرار والإجراءات", draft.CustomPalette.Accent, ChangeColor),
            new("Border", "الحدود والفواصل", draft.CustomPalette.Border, ChangeColor),
            new("Highlight", "التمييز والتحديد", draft.CustomPalette.Highlight, ChangeColor),
            new("Sidebar", "القائمة الجانبية", draft.CustomPalette.Sidebar, ChangeColor)
        ];
        SaveCommand = new RelayCommand(Save);
        RestoreSavedCommand = new RelayCommand(RestoreSaved);
        ResetCustomCommand = new RelayCommand(() =>
        {
            ApplyDraft(draft with { ThemeId = "custom", CustomPalette = ThemeCatalog.CustomPalette, SurfaceOpacity = 1, CornerRadius = 12 });
            SetStatus("تمت استعادة لوحة الألوان الافتراضية للمعاينة. اضغط «حفظ المظهر» لتثبيتها.", false);
        });
        ResetDefaultCommand = new RelayCommand(() =>
        {
            ApplyDraft(new AppearanceSettings { ReduceMotion = draft.ReduceMotion });
            SetStatus("الزجاج الشفاف جاهز للمعاينة. اضغط «حفظ المظهر» لاعتماده.", false);
        });
        RefreshAll();
        if (this.runtime.LoadWarning is { Length: > 0 } warning) SetStatus(warning, true);
        else SetStatus("اختر مظهرًا لمعاينته فورًا. لا يُستبدل الملف المحفوظ حتى تضغط «حفظ المظهر».", false);
    }

    public ThemeOptionViewModel? SelectedTheme
    {
        get => selectedTheme;
        set
        {
            if (refreshing || value is null || ReferenceEquals(value, selectedTheme)) return;
            var definition = value.Definition;
            ApplyDraft(draft with { ThemeId = definition.Id, SurfaceOpacity = definition.SurfaceOpacity, CornerRadius = definition.CornerRadius });
            SetStatus($"معاينة «{definition.Title}» على البرنامج كله. اضغط «حفظ المظهر» ليعود عند إعادة التشغيل.", false);
        }
    }

    public bool IsCustomSelected => draft.ThemeId == "custom";
    public bool HasInvalidColors => Colors.Any(color => !color.IsValid);
    public bool HasUnsavedChanges => draft != runtime.Saved || HasInvalidColors;
    public bool CanSave => HasUnsavedChanges && !HasInvalidColors;
    public bool CanRestore => HasUnsavedChanges;
    public string SavedThemeTitle => ThemeCatalog.Find(runtime.Saved.ThemeId).Title;
    public string ActiveThemeTitle => ThemeCatalog.Find(draft.ThemeId).Title;
    public string SaveStateLabel => HasInvalidColors ? "أكمل تصحيح الألوان" : HasUnsavedChanges ? "معاينة غير محفوظة" : "المظهر محفوظ";
    public string StoragePath => runtime.StoragePath;
    public string StatusMessage => statusMessage;
    public bool IsErrorStatus => isErrorStatus;
    public bool HasStatusMessage => !string.IsNullOrWhiteSpace(statusMessage);
    public string ContrastSummary => ContrastChecks.All(check => check.IsUnchanged)
        ? "الألوان المختارة تحقق نسبة وضوح 4.5:1 للنصوص الأساسية."
        : "تم تعديل درجة بعض النصوص تلقائيًا لتحقيق 4.5:1؛ قيم HEX التي اخترتها تبقى محفوظة.";

    public double OpacityPercent
    {
        get => Math.Round(draft.SurfaceOpacity * 100);
        set
        {
            if (refreshing) return;
            var normalized = Math.Clamp(Math.Round(value), 35, 100);
            ApplyDraft(draft with { ThemeId = "custom", SurfaceOpacity = normalized / 100 });
        }
    }

    public double CornerRadius
    {
        get => draft.CornerRadius;
        set
        {
            if (refreshing) return;
            ApplyDraft(draft with { ThemeId = "custom", CornerRadius = Math.Clamp(Math.Round(value), 0, 24) });
        }
    }

    public bool ReduceMotion
    {
        get => draft.ReduceMotion;
        set
        {
            if (refreshing || draft.ReduceMotion == value) return;
            ApplyDraft(draft with { ReduceMotion = value });
        }
    }

    private void ChangeColor(string role, string hex)
    {
        if (refreshing) return;
        var p = draft.CustomPalette;
        p = role switch
        {
            "Background" => p with { Background = hex },
            "Surface" => p with { Surface = hex },
            "Text" => p with { Text = hex },
            "MutedText" => p with { MutedText = hex },
            "Accent" => p with { Accent = hex },
            "Border" => p with { Border = hex },
            "Highlight" => p with { Highlight = hex },
            "Sidebar" => p with { Sidebar = hex },
            _ => p
        };
        ApplyDraft(draft with { ThemeId = "custom", CustomPalette = p });
    }

    private void ApplyDraft(AppearanceSettings settings)
    {
        try
        {
            runtime.Preview(settings);
            draft = settings.Clone();
            RefreshAll();
            SetStatus("المعاينة مباشرة على النوافذ المفتوحة. اضغط «حفظ المظهر» لتثبيت الاختيار على هذا الجهاز.", false);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidDataException)
        {
            SetStatus(ex.Message, true);
        }
    }

    private void Save()
    {
        if (HasInvalidColors)
        {
            SetStatus("يوجد لون غير مكتمل. استخدم صيغة #RRGGBB ثم احفظ.", true);
            return;
        }
        try
        {
            runtime.Preview(draft);
            runtime.Save();
            RefreshAll();
            SetStatus($"تم حفظ «{ActiveThemeTitle}» وألوانه على هذا الجهاز. سيُحمّل قبل شاشة الدخول عند إعادة التشغيل.", false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidDataException)
        {
            SetStatus("تعذر حفظ المظهر على هذا الجهاز. تحقق من مساحة القرص وصلاحية الكتابة ثم أعد المحاولة؛ المعاينة الحالية لم تُفقد.", true);
        }
    }

    private void RestoreSaved()
    {
        runtime.RestoreSaved();
        draft = runtime.Saved.Clone();
        RefreshAll();
        SetStatus($"تمت استعادة «{SavedThemeTitle}» المحفوظ وإلغاء المعاينة غير المحفوظة.", false);
    }

    private void RefreshAll()
    {
        refreshing = true;
        try
        {
            selectedTheme = Themes.First(theme => theme.Definition.Id == draft.ThemeId);
            foreach (var theme in Themes) theme.RefreshSelected(draft.ThemeId);
            var p = draft.CustomPalette;
            string[] values = [p.Background, p.Surface, p.Text, p.MutedText, p.Accent, p.Border, p.Highlight, p.Sidebar];
            for (var i = 0; i < Colors.Count; i++) Colors[i].SetHex(values[i]);
            RefreshContrast();
        }
        finally { refreshing = false; }
        Changed("");
    }

    private void RefreshContrast()
    {
        ContrastChecks.Clear();
        var p = ThemeCatalog.PaletteFor(draft);
        void Add(string label, string foreground, string background)
        {
            var effective = ThemeColorMath.EnsureTextContrast(foreground, background);
            ContrastChecks.Add(new(label, ThemeColorMath.Contrast(effective, background), effective, !string.Equals(effective, foreground, StringComparison.OrdinalIgnoreCase)));
        }
        Add("النص / البطاقات", p.Text, p.Surface);
        Add("النص الثانوي / البطاقات", p.MutedText, p.Surface);
        Add("النص / الخلفية", p.Text, p.Background);
        Add("الأزرار", ThemeColorMath.ReadableInk(p.Accent), p.Accent);
        Add("القائمة الجانبية", ThemeColorMath.ReadableInk(p.Sidebar), p.Sidebar);
    }

    private void SetStatus(string message, bool error)
    {
        statusMessage = message;
        isErrorStatus = error;
        Changed(nameof(StatusMessage));
        Changed(nameof(IsErrorStatus));
        Changed(nameof(HasStatusMessage));
    }

    private sealed class DesignAppearanceRuntime : IAppearanceRuntime
    {
        public AppearanceSettings Active { get; private set; } = new();
        public AppearanceSettings Saved { get; private set; } = new();
        public string? LoadWarning => null;
        public string StoragePath => AppearanceStore.DefaultFilePath;
        public void Preview(AppearanceSettings settings) { ThemeCatalog.Validate(settings); Active = settings.Clone(); }
        public void Save() { Saved = Active.Clone(); }
        public void RestoreSaved() => Active = Saved.Clone();
    }
}

public sealed class ThemeOptionViewModel(ThemeDefinition definition) : ObservableObject
{
    public ThemeDefinition Definition { get; } = definition;
    public string Id => Definition.Id;
    public string Title => Definition.Title;
    public string EnglishTitle => Definition.EnglishTitle;
    public string Description => Definition.Description;
    public string Signature => Definition.Signature;
    public string Icon => Definition.Icon;
    public bool IsSelected => isSelected;
    private bool isSelected;
    public IReadOnlyList<string> Swatches { get; } =
        [definition.Palette.Background, definition.Palette.Accent, definition.Palette.Highlight, definition.Palette.Sidebar];
    internal void RefreshSelected(string id)
    {
        var value = id == Id;
        if (value == isSelected) return;
        isSelected = value;
        Changed(nameof(IsSelected));
    }
}

public sealed class ColorEditorViewModel(string role, string label, string hex, Action<string, string> changed) : ObservableObject
{
    private string hex = hex;
    private bool refreshing;
    public string Role { get; } = role;
    public string Label { get; } = label;
    public string Hex
    {
        get => hex;
        set
        {
            if (refreshing || hex == value) return;
            hex = value;
            Changed(nameof(Hex));
            Changed(nameof(IsValid));
            Changed(nameof(Error));
            Changed(nameof(SwatchHex));
            if (IsValid) changed(Role, value.ToUpperInvariant());
        }
    }
    public bool IsValid => ThemeColorMath.IsHex(Hex);
    public string Error => IsValid ? "" : "أدخل # متبوعة بستة أرقام/حروف HEX.";
    public string SwatchHex => IsValid ? Hex : "#CBD5E1";
    internal void SetHex(string value)
    {
        if (string.Equals(hex, value, StringComparison.OrdinalIgnoreCase)) return;
        refreshing = true;
        hex = value;
        Changed(nameof(Hex));
        Changed(nameof(IsValid));
        Changed(nameof(Error));
        Changed(nameof(SwatchHex));
        refreshing = false;
    }
}

public sealed record ContrastCheckViewModel(string Label, double Ratio, string EffectiveHex, bool IsAdjusted)
{
    public string RatioDisplay => Ratio.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + ":1";
    public bool IsUnchanged => !IsAdjusted;
    public string StateLabel => IsAdjusted ? "درجة معدّلة للوضوح" : "وضوح جيد";
}
