using System;
using System.Collections.Generic;

namespace VanillaGraphicsExpanded.PBR.Materials.Async;

internal sealed class PriorityFifoQueue<T>
{
    private readonly object gate = new();
    private readonly SortedDictionary<int, Queue<T>> queuesByPriority = new();

    public void Clear()
    {
        lock (gate)
        {
            queuesByPriority.Clear();
        }
    }

    public void Enqueue(int priority, T item)
    {
        lock (gate)
        {
            if (!queuesByPriority.TryGetValue(priority, out Queue<T>? q))
            {
                q = new Queue<T>();
                queuesByPriority[priority] = q;
            }

            q.Enqueue(item);
        }
    }

    public bool TryDequeue(out T item)
    {
        lock (gate)
        {
            if (queuesByPriority.Count == 0)
            {
                item = default!;
                return false;
            }

            int highestPriority = int.MinValue;
            Queue<T>? highestQueue = null;

            foreach ((int priority, Queue<T> q) in queuesByPriority)
            {
                highestPriority = priority;
                highestQueue = q;
            }

            if (highestQueue is null || highestQueue.Count == 0)
            {
                item = default!;
                return false;
            }

            item = highestQueue.Dequeue();
            if (highestQueue.Count == 0)
            {
                queuesByPriority.Remove(highestPriority);
            }

            return true;
        }
    }
}
