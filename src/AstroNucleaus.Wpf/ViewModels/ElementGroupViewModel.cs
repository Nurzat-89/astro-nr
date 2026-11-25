using AstroNucleaus.Application.Endf;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AstroNucleaus.Wpf.ViewModels;

/// <summary>
/// Evaluations of one chemical element in the isotope tree, e.g. "Fe (4)".
/// </summary>
public partial class ElementGroupViewModel : ObservableObject
{
    public ElementGroupViewModel(string symbol, int? z, IReadOnlyList<EndfRemoteFile> files)
    {
        Symbol = symbol;
        Z = z;
        Files = files;
    }

    public string Symbol { get; }

    public int? Z { get; }

    public IReadOnlyList<EndfRemoteFile> Files { get; }

    public int Count => Files.Count;

    [ObservableProperty]
    private bool _isExpanded;
}
