using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Pilaster.App.Localization;
using Pilaster.App.Services;
using Pilaster.Core.Settings;

// Lásd a ThemeService-ben: a .NET 10-es WPF saját ThemeMode típusa ütközne.
using ThemeMode = Pilaster.Core.Settings.ThemeMode;

namespace Pilaster.App.ViewModels;

/// <summary>Egy választható nyelv a legördülőben.</summary>
/// <param name="Code">Kultúrakód, vagy <c>null</c> a rendszernyelv követéséhez.</param>
/// <param name="DisplayName">A nyelv saját nevén — így az is megtalálja, aki épp nem érti a felület nyelvét.</param>
public sealed record LanguageOption(string? Code, string DisplayName);

/// <summary>A Beállítások ablak állapota.</summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly ISettingsService _settings;
    private readonly ThemeService _theme;

    /// <summary>
    /// Igaz, amíg a konstruktor tölti a mezőket. Enélkül a kezdeti értékek
    /// beállítása azonnal mentést és témaváltást váltana ki.
    /// </summary>
    private readonly bool _loaded;

    public SettingsViewModel(ISettingsService settings, ThemeService theme)
    {
        _settings = settings;
        _theme = theme;

        var current = settings.Current;

        _selectedTheme = current.Theme;
        _animationsEnabled = current.AnimationsEnabled;
        _selectedLanguage = Languages.FirstOrDefault(l => l.Code == current.Language) ?? Languages[0];

        QuickActions =
        [
            new QuickActionEditorViewModel("QuickAction_First", current.QuickAction1, OnQuickActionChanged),
            new QuickActionEditorViewModel("QuickAction_Second", current.QuickAction2, OnQuickActionChanged),
        ];

        _loaded = true;
    }

    /// <summary>
    /// A választható nyelvek. A rendszernyelv-követés külön elem, mert az nem
    /// ugyanaz, mint a jelenlegi rendszernyelv rögzítése: ha a felhasználó
    /// később átállítja a Windowst, ez követi, a rögzített kód nem.
    /// </summary>
    public static IReadOnlyList<LanguageOption> Languages { get; } =
    [
        new(null, "Rendszernyelv / System language"),
        new("hu", "Magyar"),
        new("en", "English"),
    ];

    public IReadOnlyList<ThemeMode> Themes { get; } =
        [ThemeMode.System, ThemeMode.Light, ThemeMode.Dark];

    public ObservableCollection<QuickActionEditorViewModel> QuickActions { get; }

    [ObservableProperty]
    private ThemeMode _selectedTheme;

    [ObservableProperty]
    private LanguageOption _selectedLanguage;

    [ObservableProperty]
    private bool _animationsEnabled;

    /// <summary>Az az ablak, amelyiken a témaváltás átúsztatása fusson.</summary>
    public System.Windows.Window? AnimationHost { get; set; }

    partial void OnSelectedThemeChanged(ThemeMode value)
    {
        if (_loaded)
        {
            _ = _theme.SetAsync(value, AnimationHost);
        }
    }

    partial void OnAnimationsEnabledChanged(bool value)
    {
        if (!_loaded)
        {
            return;
        }

        _settings.Current.AnimationsEnabled = value;
        _settings.NotifyChanged();
    }

    partial void OnSelectedLanguageChanged(LanguageOption value)
    {
        if (!_loaded)
        {
            return;
        }

        _settings.Current.Language = value.Code;
        _settings.NotifyChanged();

        // A null kódot a rendszernyelvre oldjuk fel — a mentésben viszont
        // null marad, hogy a követés megmaradjon.
        TranslationSource.Instance.SetLanguage(
            value.Code ?? TranslationSource.ResolveSystemLanguage());
    }

    private void OnQuickActionChanged() => _settings.NotifyChanged();
}
