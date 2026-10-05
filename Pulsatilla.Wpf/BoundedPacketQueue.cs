using System.Collections.Concurrent;

namespace Pulsatilla.Wpf;

/// <summary>Protect the UI and memory when capture is faster than analysis; expose lost samples explicitly.</summary>
public sealed class BoundedPacketQueue(int capacity = 4096)
{
    private readonly ConcurrentQueue<PacketObservation> _queue = new();
    private int _count;
    private long _dropped;
    public long DroppedCount => Interlocked.Read(ref _dropped);
    internal int Count => Volatile.Read(ref _count);
    public bool TryEnqueue(PacketObservation packet)
    {
        if (Interlocked.Increment(ref _count) > capacity)
        {
            Interlocked.Decrement(ref _count); Interlocked.Increment(ref _dropped); return false;
        }
        _queue.Enqueue(packet); return true;
    }
    public bool TryDequeue(out PacketObservation packet)
    {
        if (!_queue.TryDequeue(out packet!)) return false;
        Interlocked.Decrement(ref _count); return true;
    }
}
