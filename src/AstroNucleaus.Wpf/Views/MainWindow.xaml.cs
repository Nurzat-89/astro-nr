using System.Windows;
using System.Windows.Input;
using AstroNucleaus.Wpf.ViewModels;

namespace AstroNucleaus.Wpf.Views;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
    }

    private void OnLoaded(object sender, RoutedEventArgs e) => _viewModel.Initialize();

    private async void OnFileDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (_viewModel.DownloadCommand.CanExecute(null))
            await _viewModel.DownloadCommand.ExecuteAsync(null);
    }
}
