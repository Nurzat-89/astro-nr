using System.Windows.Controls;
using System.Windows.Input;
using AstroNucleaus.Wpf.ViewModels;

namespace AstroNucleaus.Wpf.Views;

public partial class MaterialsView : UserControl
{
    public MaterialsView()
    {
        InitializeComponent();
    }

    private void OnRowDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (Grid.SelectedItem is not null && DataContext is ShellViewModel shell)
            shell.ShowBrowserCommand.Execute(null);
    }
}
