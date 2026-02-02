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
