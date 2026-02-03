using System;
using System.Collections.Generic;

namespace VanillaGraphicsExpanded.Collections;

internal sealed class IndexedMaxHeap<TKey>
    where TKey : notnull
{
    private readonly List<TKey> keys;
    private readonly List<float> priorities;
    private readonly Dictionary<TKey, int> indexByKey;

    public IndexedMaxHeap(int capacity = 0, IEqualityComparer<TKey>? comparer = null)
    {
        if (capacity < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity));
        }

        keys = capacity > 0 ? new List<TKey>(capacity) : new List<TKey>();
        priorities = capacity > 0 ? new List<float>(capacity) : new List<float>();
        indexByKey = new Dictionary<TKey, int>(capacity: Math.Max(0, capacity), comparer ?? EqualityComparer<TKey>.Default);
    }

    public int Count => keys.Count;

    public void Clear()
    {
        keys.Clear();
        priorities.Clear();
        indexByKey.Clear();
    }

    public bool ContainsKey(TKey key) => indexByKey.ContainsKey(key);

    public bool TryGetPriority(TKey key, out float priority)
    {
        if (indexByKey.TryGetValue(key, out int index))
        {
            priority = priorities[index];
            return true;
        }

        priority = default;
        return false;
    }

    public bool TryPeekMax(out TKey key, out float priority)
    {
        if (Count <= 0)
        {
            key = default!;
            priority = default;
            return false;
        }

        key = keys[0];
        priority = priorities[0];
        return true;
    }

    public bool Upsert(TKey key, float priority)
    {
        if (indexByKey.TryGetValue(key, out int index))
        {
            float oldPriority = priorities[index];
            priorities[index] = priority;

            if (priority > oldPriority)
            {
                HeapifyUp(index);
            }
            else if (priority < oldPriority)
            {
                HeapifyDown(index);
            }

            return false;
        }

        int insertIndex = Count;
        keys.Add(key);
        priorities.Add(priority);
        indexByKey.Add(key, insertIndex);

        HeapifyUp(insertIndex);
        return true;
    }

    public bool UpdatePriority(TKey key, float priority)
    {
        if (!indexByKey.TryGetValue(key, out int index))
        {
            return false;
        }

        float oldPriority = priorities[index];
        priorities[index] = priority;

        if (priority > oldPriority)
        {
            HeapifyUp(index);
        }
        else if (priority < oldPriority)
        {
            HeapifyDown(index);
        }

        return true;
    }

    public bool Remove(TKey key)
    {
        if (!indexByKey.TryGetValue(key, out int index))
        {
            return false;
        }

        RemoveAt(index);
        return true;
    }

    public bool TryPopMax(out TKey key)
    {
        if (Count <= 0)
        {
            key = default!;
            return false;
        }

        key = keys[0];
        RemoveAt(0);
        return true;
    }

    /// <summary>
    /// Copies up to <paramref name="dst"/>.Length keys from the heap in descending priority order,
    /// without mutating the heap.
    /// Intended for debug overlays and telemetry; keep <paramref name="dst"/> small.
    /// </summary>
    public int CopyTopKeys(Span<TKey> dst)
    {
        if (dst.Length <= 0 || Count <= 0)
        {
            return 0;
        }

        int want = Math.Min(dst.Length, Count);

        // Candidate indices into the heap arrays. We do a small best-first search over indices.
        // For small K this avoids cloning or mutating the heap.
        int initialCap = Math.Min(Count, Math.Max(8, want * 4));
        int[] cand = System.Buffers.ArrayPool<int>.Shared.Rent(initialCap);
        int candCount = 0;

        try
        {
            cand[candCount++] = 0;

            int written = 0;
            while (written < want && candCount > 0)
            {
                int bestPos = 0;
                float bestPri = priorities[cand[0]];

                for (int i = 1; i < candCount; i++)
                {
                    float p = priorities[cand[i]];
                    if (p > bestPri)
                    {
                        bestPri = p;
                        bestPos = i;
                    }
                }

                int bestIndex = cand[bestPos];
                cand[bestPos] = cand[candCount - 1];
                candCount--;

                dst[written++] = keys[bestIndex];

                int left = (bestIndex << 1) + 1;
                int right = left + 1;

                if (left < Count)
                {
                    if (candCount >= cand.Length)
                    {
                        int[] grown = System.Buffers.ArrayPool<int>.Shared.Rent(Math.Min(Count, cand.Length * 2));
                        Array.Copy(cand, grown, cand.Length);
                        System.Buffers.ArrayPool<int>.Shared.Return(cand, clearArray: false);
                        cand = grown;
                    }

                    cand[candCount++] = left;
                }

                if (right < Count)
                {
                    if (candCount >= cand.Length)
                    {
                        int[] grown = System.Buffers.ArrayPool<int>.Shared.Rent(Math.Min(Count, cand.Length * 2));
                        Array.Copy(cand, grown, cand.Length);
                        System.Buffers.ArrayPool<int>.Shared.Return(cand, clearArray: false);
                        cand = grown;
                    }

                    cand[candCount++] = right;
                }
            }

            return written;
        }
        finally
        {
            System.Buffers.ArrayPool<int>.Shared.Return(cand, clearArray: false);
        }
    }

    private void RemoveAt(int index)
    {
        int lastIndex = Count - 1;
        TKey removedKey = keys[index];

        indexByKey.Remove(removedKey);

        if (index == lastIndex)
        {
            keys.RemoveAt(lastIndex);
            priorities.RemoveAt(lastIndex);
            return;
        }

        // Move last into removed slot.
        TKey movedKey = keys[lastIndex];
        float movedPriority = priorities[lastIndex];

        keys[index] = movedKey;
        priorities[index] = movedPriority;

        keys.RemoveAt(lastIndex);
        priorities.RemoveAt(lastIndex);

        indexByKey[movedKey] = index;

        // Fix heap.
        int parent = ParentIndex(index);
        if (index > 0 && priorities[index] > priorities[parent])
        {
            HeapifyUp(index);
        }
        else
        {
            HeapifyDown(index);
        }
    }

    private void HeapifyUp(int index)
    {
        while (index > 0)
        {
            int parent = ParentIndex(index);
            if (priorities[index] <= priorities[parent])
            {
                break;
            }

            Swap(index, parent);
            index = parent;
        }
    }

    private void HeapifyDown(int index)
    {
        while (true)
        {
            int left = LeftChildIndex(index);
            if (left >= Count)
            {
                return;
            }

            int right = left + 1;
            int largest = left;

            if (right < Count && priorities[right] > priorities[left])
            {
                largest = right;
            }

            if (priorities[largest] <= priorities[index])
            {
                return;
            }

            Swap(index, largest);
            index = largest;
        }
    }

    private void Swap(int a, int b)
    {
        if (a == b)
        {
            return;
        }

        (keys[b], keys[a]) = (keys[a], keys[b]);
        (priorities[b], priorities[a]) = (priorities[a], priorities[b]);

        indexByKey[keys[a]] = a;
        indexByKey[keys[b]] = b;
    }

    private static int ParentIndex(int index) => (index - 1) >> 1;

    private static int LeftChildIndex(int index) => (index << 1) + 1;
}
