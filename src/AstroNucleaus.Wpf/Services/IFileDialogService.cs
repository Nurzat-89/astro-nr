namespace AstroNucleaus.Wpf.Services;

public interface IFileDialogService
{
    /// <summary>Asks the user for an ENDF file; returns null when cancelled.</summary>
    string? OpenEndfFile();
}
