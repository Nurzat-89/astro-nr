using AstroNucleaus.Application.Abstractions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AstroNucleaus.Wpf.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly IDateTimeProvider _dateTimeProvider;

    [ObservableProperty]
    private string _title = "AstroNucleaus";

    [ObservableProperty]
    private string _status = "Ready";

    public MainViewModel(IDateTimeProvider dateTimeProvider)
    {
        _dateTimeProvider = dateTimeProvider;
    }

    [RelayCommand]
    private void Refresh() => Status = $"Updated at {_dateTimeProvider.UtcNow:O}";
}
