using System.Windows;
using AstroNucleaus.Wpf.ViewModels;

namespace AstroNucleaus.Wpf.Views;

public partial class MainWindow : Window
{
    private readonly ShellViewModel _viewModel;

    public MainWindow(ShellViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
        FitToWorkArea();
    }

    /// <summary>Keeps the default size inside the screen; small screens start maximized.</summary>
    private void FitToWorkArea()
    {
        var workArea = SystemParameters.WorkArea;
        if (workArea.Width < MinWidth + 40 || workArea.Height < MinHeight + 40)
        {
            WindowState = WindowState.Maximized;
            return;
        }

        Width = Math.Min(Width, workArea.Width * 0.94);
        Height = Math.Min(Height, workArea.Height * 0.94);
    }

    private void OnLoaded(object sender, RoutedEventArgs e) => _viewModel.Initialize();
}
