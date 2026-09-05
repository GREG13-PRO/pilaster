using System;
using System.IO;
using System.Reflection;
using System.Windows.Controls;

namespace Pilaster.Setup.Views.Pages;

public partial class LicensePage : UserControl
{
    public event EventHandler? CanAdvanceChanged;

    public bool IsAccepted => AcceptCheckBox.IsChecked == true;

    public LicensePage()
    {
        InitializeComponent();
        LicenseText.Text = LoadEmbeddedLicense();
    }

    private static string LoadEmbeddedLicense()
    {
        var assembly = Assembly.GetExecutingAssembly();
        using var stream = assembly.GetManifestResourceStream("Pilaster.Setup.LICENSE.txt");

        if (stream is null)
        {
            return string.Empty;
        }

        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private void OnAcceptChanged(object sender, System.Windows.RoutedEventArgs e) =>
        CanAdvanceChanged?.Invoke(this, EventArgs.Empty);
}
