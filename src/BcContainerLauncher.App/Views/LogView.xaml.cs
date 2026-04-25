using System.Collections.Specialized;
using System.Windows.Controls;
using BcContainerLauncher.App.ViewModels;

namespace BcContainerLauncher.App.Views;

public partial class LogView : UserControl
{
    public LogView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, System.Windows.DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is LogViewModel oldVm)
        {
            ((INotifyCollectionChanged)oldVm.Entries).CollectionChanged -= OnEntriesChanged;
        }
        if (e.NewValue is LogViewModel newVm)
        {
            ((INotifyCollectionChanged)newVm.Entries).CollectionChanged += OnEntriesChanged;
        }
    }

    private void OnEntriesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (DataContext is not LogViewModel vm || !vm.AutoScroll)
        {
            return;
        }
        if (LogList.Items.Count > 0)
        {
            LogList.ScrollIntoView(LogList.Items[^1]);
        }
    }
}
