using System.Windows;
using AstroNucleaus.Wpf.ViewModels;

namespace AstroNucleaus.Wpf.Views;

public partial class MainWindow : Window
{
    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
