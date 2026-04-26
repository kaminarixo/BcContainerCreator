using System.Windows;
using Microsoft.Win32;

namespace BcContainerCreator.App.Services;

public sealed class DialogService : IDialogService
{
    public string? PickFile(string filter, string? title = null)
    {
        var dlg = new OpenFileDialog
        {
            Filter = filter,
            Title = title ?? "Datei wählen",
            CheckFileExists = true
        };
        return dlg.ShowDialog() == true ? dlg.FileName : null;
    }

    public string? PickSaveFile(string filter, string defaultFileName, string? title = null)
    {
        var dlg = new SaveFileDialog
        {
            Filter = filter,
            FileName = defaultFileName,
            Title = title ?? "Speichern unter…"
        };
        return dlg.ShowDialog() == true ? dlg.FileName : null;
    }

    public void ShowMessage(string message, string title, bool isError = false)
    {
        MessageBox.Show(
            message,
            title,
            MessageBoxButton.OK,
            isError ? MessageBoxImage.Error : MessageBoxImage.Information);
    }
}
