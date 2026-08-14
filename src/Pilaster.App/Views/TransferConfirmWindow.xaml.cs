using System.Windows.Input;
using Pilaster.App.Localization;
using Wpf.Ui.Controls;

namespace Pilaster.App.Views;

/// <summary>
/// Total Commander F5 (Másolás)/F6 (Áthelyezés) megerősítő párbeszéde — a cél
/// mappa szerkeszthető szövegmezőben jelenik meg, ahogy a specifikáció kéri.
/// Ha a felhasználó a cél MAPPÁJÁT nem változtatja meg (csak a fájlnevet, egy
/// elem kijelölésekor), a végeredmény gyakorlatilag átnevezés — ezt nem itt,
/// hanem a <c>FileOperationEngine</c> már meglévő „azonos mappán belüli
/// gyors mozgatás" ága adja ingyen, külön eset nélkül.
/// </summary>
public partial class TransferConfirmWindow : FluentWindow
{
    public TransferConfirmWindow()
    {
        InitializeComponent();
    }

    /// <summary>A megerősített (esetleg szerkesztett) célútvonal, ha a felhasználó OK-t nyomott.</summary>
    public string? ConfirmedTarget { get; private set; }

    public void Initialize(bool isMove, int itemCount, string targetDirectory)
    {
        var strings = TranslationSource.Instance;
        var title = isMove ? strings["TC_MoveTitle"] : strings["TC_CopyTitle"];

        Title = title;
        TitleBarHost.Title = title;
        SummaryText.Text = string.Format(strings["TC_TransferSummary"], itemCount);
        HintText.Text = strings["TC_TransferHint"];
        TargetBox.Text = targetDirectory;
        Loaded += (_, _) =>
        {
            TargetBox.Focus();
            TargetBox.SelectAll();
        };
    }

    private void OnOkClick(object sender, System.Windows.RoutedEventArgs e) => Confirm();

    private void OnCancelClick(object sender, System.Windows.RoutedEventArgs e) => Close();

    private void OnTargetBoxKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter:
                e.Handled = true;
                Confirm();
                break;
            case Key.Escape:
                e.Handled = true;
                Close();
                break;
        }
    }

    private void Confirm()
    {
        var target = TargetBox.Text.Trim();

        if (target.Length == 0)
        {
            return;
        }

        ConfirmedTarget = target;
        DialogResult = true;
    }
}
