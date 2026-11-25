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
}
