using System.Windows.Controls;
using System.Windows.Data;
using Pilaster.Setup.Services;

namespace Pilaster.Setup.Views.Pages;

public partial class UninstallConfirmPage : UserControl
{
    public UninstallConfirmPage(SetupSession session)
    {
        InitializeComponent();
        DataContext = session;
        DeleteSettingsCheckBox.SetBinding(CheckBox.IsCheckedProperty, new Binding(nameof(SetupSession.DeleteSettingsOnUninstall)));
    }
}
