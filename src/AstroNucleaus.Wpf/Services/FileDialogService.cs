using System.IO;
using Microsoft.Win32;

namespace AstroNucleaus.Wpf.Services;

public sealed class FileDialogService : IFileDialogService
{
    public string? OpenEndfFile()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Open ENDF file",
            Filter = "ENDF files (*.endf;*.endf6;*.dat;*.txt;*.asc)|*.endf;*.endf6;*.dat;*.txt;*.asc|All files (*.*)|*.*",
            CheckFileExists = true,
        };

        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public string? SaveFile(string title, string filter, string defaultFileName)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var dialog = new SaveFileDialog
        {
            Title = title,
            Filter = filter,
            FileName = new string(defaultFileName.Select(c => invalid.Contains(c) ? '_' : c).ToArray()),
            OverwritePrompt = true,
        };

        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }
}
