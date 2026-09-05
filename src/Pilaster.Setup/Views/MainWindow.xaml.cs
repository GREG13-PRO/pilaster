using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Animation;
using Pilaster.Setup.Constants;
using Pilaster.Setup.Services;
using Pilaster.Setup.Views.Pages;
using Wpf.Ui.Controls;

namespace Pilaster.Setup.Views;

/// <summary>
/// A varázsló váza: egyetlen ablak, ami a beállított sorrendben lévő
/// oldalakat (Views/Pages) cseréli a tartalmi területén, csúszó átmenettel
/// (ugyanaz a technika, mint Pilaster.App/Views/MainWindow.xaml:63-78-nál).
/// A lap-specifikus viselkedést (melyik gomb látszik, mit csinál a Tovább)
/// itt, konkrét típusellenőrzéssel kezeljük — ez egy rövid, lineáris
/// varázsló, egy általános oldal-interfész csak felesleges réteg lenne.
/// </summary>
public partial class MainWindow : FluentWindow
{
    private readonly SetupSession _session;

    /// <summary>
    /// Az utolsó (Finish) lap SZÁNDÉKOSAN NEM itt, felül épül fel — a
    /// konstruktor lefutásakor session.OperationSucceeded/ErrorMessage még a
    /// KEZDETI (hamis) állapotot mutatná, hiszen a művelet ekkor még el sem
    /// indult. A RunProgressStepAsync a művelet befejezése UTÁN, a valódi
    /// végeredménnyel cseréli ki ezt a tömbelemet — lásd ott.
    /// </summary>
    private readonly FrameworkElement[] _steps;
    private int _currentIndex;
    private CancellationTokenSource? _operationCts;

    public MainWindow(SetupSession session)
    {
        _session = session;
        InitializeComponent();

        _steps = session.IsUninstall
            ? new FrameworkElement[]
            {
                new UninstallConfirmPage(session),
                new ProgressPage(session),
                null!, // FinishPage — lásd a mezőhöz fűzött megjegyzést
            }
            : new FrameworkElement[]
            {
                new WelcomePage(),
                new LicensePage(),
                new OptionsPage(session),
                new ProgressPage(session),
                null!, // FinishPage — lásd a mezőhöz fűzött megjegyzést
            };

        foreach (var step in _steps)
        {
            if (step is LicensePage license)
            {
                license.CanAdvanceChanged += (_, _) => UpdateFooter();
            }
        }

        ShowStep(0, animateForward: true);
    }

    private void ShowStep(int index, bool animateForward)
    {
        _currentIndex = index;
        var page = _steps[index];

        PageHost.Content = page;
        PageTransform.X = animateForward ? 40 : -40;
        PageHost.Opacity = 0;

        var slide = new DoubleAnimation(PageTransform.X, 0, TimeSpan.FromMilliseconds(220))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        };
        var fade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220));

        PageTransform.BeginAnimation(System.Windows.Media.TranslateTransform.XProperty, slide);
        PageHost.BeginAnimation(OpacityProperty, fade);

        UpdateFooter();

        if (page is ProgressPage progressPage)
        {
            _ = RunProgressStepAsync(progressPage);
        }
    }

    private void UpdateFooter()
    {
        var page = _steps[_currentIndex];
        var isFirst = _currentIndex == 0;
        var isLast = _currentIndex == _steps.Length - 1;
        var isProgress = page is ProgressPage;

        BackButton.Visibility = isFirst || isLast || isProgress ? Visibility.Collapsed : Visibility.Visible;
        CancelButton.Visibility = isLast ? Visibility.Collapsed : Visibility.Visible;

        NextButton.IsEnabled = !isProgress && page is not LicensePage { IsAccepted: false };
        NextButton.Content = page switch
        {
            _ when isLast => "Bezárás",
            OptionsPage => "Telepítés",
            UninstallConfirmPage => "Eltávolítás",
            _ => "Tovább",
        };
    }

    private void OnNextClick(object sender, RoutedEventArgs e)
    {
        if (_currentIndex == _steps.Length - 1)
        {
            if (!_session.IsUninstall && _session.OperationSucceeded && _session.LaunchAfterFinish)
            {
                TryLaunchApp();
            }

            Close();
            return;
        }

        ShowStep(_currentIndex + 1, animateForward: true);
    }

    private void OnBackClick(object sender, RoutedEventArgs e)
    {
        if (_currentIndex > 0)
        {
            ShowStep(_currentIndex - 1, animateForward: false);
        }
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        _operationCts?.Cancel();
        Close();
    }

    private void TryLaunchApp()
    {
        try
        {
            Process.Start(new ProcessStartInfo(Path.Combine(_session.InstallDirectory, SetupInfo.AppExeName))
            {
                WorkingDirectory = _session.InstallDirectory,
                UseShellExecute = true,
            });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException)
        {
            // Az indítás sikertelensége nem akadályozhatja a telepítő bezárását.
        }
    }

    private async Task RunProgressStepAsync(ProgressPage progressPage)
    {
        _operationCts = new CancellationTokenSource();

        var progress = new Progress<CopyProgress>(p =>
        {
            _session.ProgressFraction = p.Fraction;
            _session.StatusText = p.BytesPerSecond is { } bps
                ? $"{p.CurrentFileName}  •  {bps / (1024 * 1024):0.0} MB/s"
                : p.CurrentFileName;
        });

        try
        {
            if (_session.IsUninstall)
            {
                await InstallOrchestrator.UninstallAsync(_session, progress, _operationCts.Token);
            }
            else
            {
                await InstallOrchestrator.InstallAsync(_session, progress, _operationCts.Token);
            }

            _session.OperationSucceeded = true;
        }
        catch (OperationCanceledException)
        {
            _session.OperationSucceeded = false;
            _session.ErrorMessage = "Megszakítva.";
        }
        catch (Exception ex)
        {
            _session.OperationSucceeded = false;
            _session.ErrorMessage = $"{ex.GetType().Name}: {ex.Message}";
        }

        // A FinishPage csak MOST épül fel, a végeredmény (OperationSucceeded/
        // ErrorMessage) ismeretében — lásd a _steps mezőhöz fűzött megjegyzést.
        _steps[_currentIndex + 1] = new FinishPage(_session);
        ShowStep(_currentIndex + 1, animateForward: true);
    }
}
