namespace AstroNucleaus.Wpf.Services;

public interface IFileDialogService
{
    /// <summary>Asks the user for an ENDF file; returns null when cancelled.</summary>
    string? OpenEndfFile();

    /// <summary>Asks the user where to save a file; returns null when cancelled.</summary>
    string? SaveFile(string title, string filter, string defaultFileName);
}
