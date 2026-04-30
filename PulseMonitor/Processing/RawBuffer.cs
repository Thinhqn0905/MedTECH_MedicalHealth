using PulseMonitor.Hardware;

namespace PulseMonitor.Processing;

public sealed class RawBuffer
{
  private readonly IRSample[] _buffer;
  private readonly object _sync = new();
  private int _head;
  private int _count;

  public RawBuffer(int capacity = 1000)
  {
    if (capacity <= 0)
    {
      throw new ArgumentOutOfRangeException(nameof(capacity), "Capacity must be greater than 0.");
    }

    _buffer = new IRSample[capacity];
  }

  public int Capacity => _buffer.Length;

  public int Count
  {
    get
    {
      lock (_sync)
      {
        return _count;
      }
    }
  }

  public void Add(IRSample sample)
  {
    lock (_sync)
    {
      _buffer[_head] = sample;
      _head = (_head + 1) % Capacity;

      if (_count < Capacity)
      {
        _count++;
      }
    }
  }

  public IReadOnlyList<IRSample> Snapshot()
  {
    lock (_sync)
    {
      List<IRSample> snapshot = new(_count);
      int start = (_head - _count + Capacity) % Capacity;

      for (int i = 0; i < _count; i++)
      {
        int index = (start + i) % Capacity;
        snapshot.Add(_buffer[index]);
      }

      return snapshot;
    }
  }
}
