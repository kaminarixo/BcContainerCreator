using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using System.Windows;
using BcContainerLauncher.App.Logging;
using BcContainerLauncher.App.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BcContainerLauncher.App.ViewModels;

/// <summary>
/// ViewModel für den Log-Tab. Bindet auf <see cref="InMemoryLogSink"/> und
/// projiziert Live-Events in eine Observable-Sammlung.
/// </summary>
public sealed partial class LogViewModel : ObservableObject
{
    private readonly InMemoryLogSink _sink;
    private readonly IDialogService _dialogService;

    public ObservableCollection<LogEntry> Entries { get; } = new();

    [ObservableProperty]
    private bool _autoScroll = true;

    public LogViewModel(InMemoryLogSink sink, IDialogService dialogService)
    {
        _sink = sink;
        _dialogService = dialogService;

        // Snapshot vor dem Live-Subscribe einlesen, damit nichts verloren geht.
        foreach (var e in _sink.Snapshot())
        {
            Entries.Add(e);
        }

        _sink.LogEmitted += OnLogEmitted;
    }

    private void OnLogEmitted(object? sender, LogEntry entry)
    {
        if (Application.Current?.Dispatcher is { } d && !d.CheckAccess())
        {
            d.BeginInvoke(new Action(() => AddEntry(entry)));
        }
        else
        {
            AddEntry(entry);
        }
    }

    private void AddEntry(LogEntry entry)
    {
        Entries.Add(entry);
        const int maxVisible = 2_000;
        while (Entries.Count > maxVisible)
        {
            Entries.RemoveAt(0);
        }
    }

    [RelayCommand]
    private void Clear() => Entries.Clear();

    [RelayCommand]
    private void Copy()
    {
        Clipboard.SetText(BuildText());
    }

    [RelayCommand]
    private void Save()
    {
        var path = _dialogService.PickSaveFile(
            "Log-Datei (*.log)|*.log|Text (*.txt)|*.txt",
            $"bccontainerlauncher-{DateTimeOffset.Now:yyyyMMdd-HHmmss}.log",
            "Log speichern");

        if (path is null)
        {
            return;
        }

        File.WriteAllText(path, BuildText(), Encoding.UTF8);
        _dialogService.ShowMessage($"Gespeichert: {path}", "Log gespeichert");
    }

    private string BuildText()
    {
        var sb = new StringBuilder(Entries.Count * 80);
        foreach (var e in Entries)
        {
            sb.Append(e.Timestamp.ToLocalTime().ToString("HH:mm:ss.fff"))
              .Append(" [").Append(e.Level.ToString()[..3].ToUpperInvariant()).Append("] ");
            if (!string.IsNullOrEmpty(e.SourceContext))
            {
                sb.Append('(').Append(e.SourceContext).Append(") ");
            }
            sb.AppendLine(e.Message);
        }
        return sb.ToString();
    }
}
