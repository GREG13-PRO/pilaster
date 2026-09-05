using System.Windows.Controls;
using Pilaster.Setup.Services;

namespace Pilaster.Setup.Views.Pages;

public partial class ProgressPage : UserControl
{
    public ProgressPage(SetupSession session)
    {
        InitializeComponent();
        DataContext = session;
        HeaderText.Text = session.IsUninstall ? "Eltávolítás folyamatban…" : "Telepítés folyamatban…";
    }
}
