using System.Windows.Controls;
using Microsoft.Win32;
using Pilaster.Setup.Services;

namespace Pilaster.Setup.Views.Pages;

public partial class OptionsPage : UserControl
{
    private readonly SetupSession _session;

    public OptionsPage(SetupSession session)
    {
        _session = session;
        InitializeComponent();
        DataContext = session;
    }

    private void OnBrowseClick(object sender, System.Windows.RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            InitialDirectory = _session.InstallDirectory,
        };

        if (dialog.ShowDialog() == true)
        {
            _session.InstallDirectory = dialog.FolderName;
        }
    }
}
