namespace BcContainerCreator.App.Services;

/// <summary>
/// Abstraktion über System-Dialoge, damit ViewModels testbar bleiben.
/// </summary>
public interface IDialogService
{
    /// <summary>Öffnet einen "Datei öffnen"-Dialog.</summary>
    string? PickFile(string filter, string? title = null);

    /// <summary>Öffnet einen "Datei speichern"-Dialog.</summary>
    string? PickSaveFile(string filter, string defaultFileName, string? title = null);

    /// <summary>Zeigt eine einfache Info-/Fehlermeldung.</summary>
    void ShowMessage(string message, string title, bool isError = false);
}
