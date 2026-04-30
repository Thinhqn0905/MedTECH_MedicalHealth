using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace PulseMonitor.Collections;

public class BatchObservableCollection<T> : ObservableCollection<T>
{
  private bool _isBatching;

  public BatchObservableCollection() : base() { }

  public BatchObservableCollection(IEnumerable<T> collection) : base(collection) { }

  public void AddRange(IEnumerable<T> range)
  {
    if (range == null) throw new ArgumentNullException(nameof(range));

    _isBatching = true;
    try
    {
      foreach (var item in range)
      {
        Items.Add(item);
      }
    }
    finally
    {
      _isBatching = false;
      OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
      OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
      OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
    }
  }

  /// <summary>
  /// A performance-optimized ClearAndBatch that keeps the collection at a fixed size 
  /// (circular-buffer like behaviour after reaching maxPoints).
  /// </summary>
  public void PushBatch(IEnumerable<T> items, int maxPoints)
  {
    _isBatching = true;
    try
    {
      foreach (var item in items)
      {
        Items.Add(item);
      }

      while (Items.Count > maxPoints)
      {
        Items.RemoveAt(0);
      }
    }
    finally
    {
      _isBatching = false;
      // Using Reset is the only way to notify after a large batch of changes to avoid N redraws
      OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
      OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
      OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
    }
  }

  protected override void OnCollectionChanged(NotifyCollectionChangedEventArgs e)
  {
    if (!_isBatching)
    {
      base.OnCollectionChanged(e);
    }
  }

  protected override void OnPropertyChanged(PropertyChangedEventArgs e)
  {
    if (!_isBatching)
    {
      base.OnPropertyChanged(e);
    }
  }
}
