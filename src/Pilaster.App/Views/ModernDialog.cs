using System.Windows;
using Pilaster.App.Localization;
using Wpf.Ui.Controls;

namespace Pilaster.App.Views;

/// <summary>
/// Fluent stílusú párbeszédablak (WPF-UI <see cref="Wpf.Ui.Controls.MessageBox"/>)
/// a régi, natív <see cref="System.Windows.MessageBox"/> helyett — az a
/// program többi része mellett idegen, régimódi Windows-ablaknak hatott
/// (felhasználói visszajelzés).
/// </summary>
public static class ModernDialog
{
    /// <summary>A gomb, amivel a felhasználó bezárta.</summary>
    public enum Choice
    {
        Primary,
        Secondary,
        Cancel,
    }

    /// <param name="owner">A szülőablak — fölötte, középen nyílik.</param>
    /// <param name="title">Cím.</param>
    /// <param name="message">Üzenet.</param>
    /// <param name="primary">A fő (kiemelt) gomb felirata.</param>
    /// <param name="secondary">Opcionális második gomb (pl. „Elvetés").</param>
    /// <param name="cancel">A bezáró gomb felirata; <c>null</c> esetén nincs.</param>
    public static async Task<Choice> ShowAsync(
        Window? owner,
        string title,
        string message,
        string primary,
        string? secondary = null,
        string? cancel = null)
    {
        var dialog = new Wpf.Ui.Controls.MessageBox
        {
            Title = title,
            Content = new System.Windows.Controls.TextBlock
            {
                Text = message,
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 420,
            },
            PrimaryButtonText = primary,
            PrimaryButtonAppearance = ControlAppearance.Primary,
            IsSecondaryButtonEnabled = secondary is not null,
            SecondaryButtonText = secondary ?? string.Empty,
            IsCloseButtonEnabled = cancel is not null,
            CloseButtonText = cancel ?? string.Empty,
            WindowStartupLocation = owner is null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner,
        };

        if (owner is { IsLoaded: true })
        {
            dialog.Owner = owner;
        }

        return await dialog.ShowDialogAsync() switch
        {
            Wpf.Ui.Controls.MessageBoxResult.Primary => Choice.Primary,
            Wpf.Ui.Controls.MessageBoxResult.Secondary => Choice.Secondary,
            _ => Choice.Cancel,
        };
    }

    /// <summary>Csak tájékoztatás, egyetlen „OK" gombbal.</summary>
    public static Task InformAsync(Window? owner, string title, string message) =>
        ShowAsync(owner, title, message, TranslationSource.Instance["Cmd_Ok"]);

    /// <summary>Igen/Mégse jellegű megerősítés — igaz, ha a fő gombot választották.</summary>
    public static async Task<bool> ConfirmAsync(Window? owner, string title, string message, string primary) =>
        await ShowAsync(owner, title, message, primary, cancel: TranslationSource.Instance["Cmd_Cancel"]) == Choice.Primary;
}
