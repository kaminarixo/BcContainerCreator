using System.Windows.Controls;

namespace BcContainerCreator.App.Views;

public partial class CreateContainerView : UserControl
{
    public CreateContainerView() => InitializeComponent();

    /// <summary>
    /// Auto-Scroll-to-End: bei jeder Änderung am Output-Text scrolle die
    /// Console an die letzte Zeile, damit der Live-Output mitläuft.
    /// </summary>
    private void OutputBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (sender is TextBox tb)
        {
            tb.ScrollToEnd();
        }
    }
}
