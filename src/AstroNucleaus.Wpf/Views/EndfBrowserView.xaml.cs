using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using AstroNucleaus.Application.Endf;
using AstroNucleaus.Wpf.ViewModels;

namespace AstroNucleaus.Wpf.Views;

public partial class EndfBrowserView : UserControl
{
    public EndfBrowserView()
    {
        InitializeComponent();
    }

    private EndfBrowserViewModel? ViewModel => DataContext as EndfBrowserViewModel;

    private void OnTreeSelectionChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (ViewModel is { } vm)
            vm.SelectedFile = e.NewValue as EndfRemoteFile;
    }

    private async void OnTreeDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (FileTree.SelectedItem is EndfRemoteFile && ViewModel is { } vm && vm.DownloadCommand.CanExecute(null))
            await vm.DownloadCommand.ExecuteAsync(null);
    }

    private async void OnTreeKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && FileTree.SelectedItem is EndfRemoteFile && ViewModel is { } vm && vm.DownloadCommand.CanExecute(null))
        {
            e.Handled = true;
            await vm.DownloadCommand.ExecuteAsync(null);
        }
    }

    private void OnClearFilter(object sender, RoutedEventArgs e)
    {
        if (ViewModel is { } vm)
            vm.FilterText = string.Empty;
    }

    private void OnZoomIn(object sender, RoutedEventArgs e) => Plot.ZoomIn();

    private void OnZoomOut(object sender, RoutedEventArgs e) => Plot.ZoomOut();

    private void OnFit(object sender, RoutedEventArgs e) => Plot.ResetView();

    private void OnOpenMenu(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { ContextMenu: { } menu } button)
        {
            menu.PlacementTarget = button;
            menu.Placement = PlacementMode.Bottom;
            menu.IsOpen = true;
        }
    }

    private void OnSaveImage(object sender, RoutedEventArgs e)
    {
        if (ViewModel?.AskImagePath() is not { } path)
            return;

        try
        {
            Plot.SaveAsPng(path);
            ViewModel.Status = $"Saved plot to {Path.GetFileName(path)}";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ViewModel.Status = $"Error: {ex.Message}";
        }
    }
}
