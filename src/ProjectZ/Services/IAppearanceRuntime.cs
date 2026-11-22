using ProjectZ.Models;

namespace ProjectZ.Services;

/// <summary>Separates the editor from WPF so persistence and state transitions can be tested without a desktop.</summary>
public interface IAppearanceRuntime
{
    AppearanceSettings Active { get; }
    AppearanceSettings Saved { get; }
    string? LoadWarning { get; }
    string StoragePath { get; }
    void Preview(AppearanceSettings settings);
    void Save();
    void RestoreSaved();
}
