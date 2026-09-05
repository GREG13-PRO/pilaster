using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Pilaster.Setup.Constants;
using Pilaster.Setup.Services;

namespace Pilaster.Setup.Views.Pages;

public partial class FinishPage : UserControl
{
    private readonly SetupSession _session;

    public FinishPage(SetupSession session)
    {
        _session = session;
        InitializeComponent();
        DataContext = session;

        if (session.OperationSucceeded)
        {
            TitleText.Text = session.IsUninstall ? "A Pilaster el lett távolítva" : "A telepítés befejeződött";
            DetailText.Text = session.IsUninstall
                ? "Köszönjük, hogy kipróbáltad a Pilastert."
                : "A Pilaster készen áll a használatra.";
        }
        else
        {
            ResultIcon.Symbol = Wpf.Ui.Controls.SymbolRegular.ErrorCircle24;
            ResultIcon.SetResourceReference(ForegroundProperty, "SystemFillColorCriticalBrush");
            TitleText.Text = session.IsUninstall ? "Az eltávolítás nem fejeződött be" : "A telepítés nem fejeződött be";
            DetailText.Text = session.ErrorMessage ?? "Ismeretlen hiba történt.";
        }

        LaunchCheckBox.Visibility = !session.IsUninstall && session.OperationSucceeded ? Visibility.Visible : Visibility.Collapsed;
        LaunchCheckBox.SetBinding(CheckBox.IsCheckedProperty, new System.Windows.Data.Binding(nameof(SetupSession.LaunchAfterFinish)));
        ChangelogLink.Visibility = session.IsUninstall ? Visibility.Collapsed : Visibility.Visible;

        Loaded += (_, _) => PlayEntrance();
    }

    private void PlayEntrance()
    {
        var fade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(250));
        var scale = new DoubleAnimation(0.5, 1, TimeSpan.FromMilliseconds(350))
        {
            EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.4 },
        };

        ResultIcon.BeginAnimation(OpacityProperty, fade);
        ResultIconScale.BeginAnimation(ScaleTransform.ScaleXProperty, scale);
        ResultIconScale.BeginAnimation(ScaleTransform.ScaleYProperty, scale);
    }

    private void OnChangelogClick(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo(SetupInfo.ReleasesUrl) { UseShellExecute = true });
        }
        catch (System.ComponentModel.Win32Exception)
        {
        }
    }
}
