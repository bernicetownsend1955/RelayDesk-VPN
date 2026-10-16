using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace RelayDesk;

public sealed class ConnectionCollection : ObservableCollection<ConnectionRow>
{
    public void Replace(IEnumerable<ConnectionRow> rows)
    {
        var snapshot = rows.ToArray();
        Items.Clear(); foreach (var row in snapshot) Items.Add(row);
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
        OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }
}
