using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace PulseMonitor.Collections;

/// <summary>
/// A high-performance ObservableCollection that supports batch updates
/// to minimize UI thread notifications and layout passes.
/// </summary>
public class BufferedObservableCollection<T> : ObservableCollection<T>
{
    private bool _suppressNotification = false;

    public BufferedObservableCollection() : base() { }
    public BufferedObservableCollection(IEnumerable<T> collection) : base(collection) { }

    /// <summary>
    /// Adds a range of items to the collection and fires only ONE notification.
    /// </summary>
    public void AddRange(IEnumerable<T> items)
    {
        if (items == null) return;

        _suppressNotification = true;
        foreach (var item in items)
        {
            Items.Add(item);
        }
        _suppressNotification = false;

        OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
        OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }

    /// <summary>
    /// Replaces current values with a snapshot and sends only ONE reset notification.
    /// </summary>
    public void ReplaceWithSnapshot(IReadOnlyList<T> snapshot, int count)
    {
        if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
        if (count < 0 || count > snapshot.Count) throw new ArgumentOutOfRangeException(nameof(count));

        _suppressNotification = true;
        try
        {
            Items.Clear();
            for (int i = 0; i < count; i++)
            {
                Items.Add(snapshot[i]);
            }
        }
        finally
        {
            _suppressNotification = false;
        }

        OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
        OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }

    /// <summary>
    /// Removes a range of items and fires only ONE notification.
    /// </summary>
    public void RemoveRange(int index, int count)
    {
        _suppressNotification = true;
        for (int i = 0; i < count; i++)
        {
            if (Items.Count > index) Items.RemoveAt(index);
        }
        _suppressNotification = false;

        OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
        OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }

    protected override void OnCollectionChanged(NotifyCollectionChangedEventArgs e)
    {
        if (!_suppressNotification)
        {
            base.OnCollectionChanged(e);
        }
    }

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        if (!_suppressNotification)
        {
            base.OnPropertyChanged(e);
        }
    }
}
