using System.Runtime.CompilerServices;
using System;
using System.Collections.Generic;

namespace VanillaGraphicsExpanded.Rendering.RHI;

/// <summary>Retains typed payloads or references in reusable CPU pages without boxing individual commands.</summary>
internal sealed class CommandPages<T>
{
    private const int PageSize = 128;
    private readonly List<T[]> pages = new();
    #region Internal API
    /// <summary>Gets the number of published entries.</summary>
    internal int Count { get; private set; }
    /// <summary>Appends a complete value, allocating only when a reusable page is exhausted.</summary>
    internal int Add(T value)
    {
        int index = Count;
        if (index / PageSize == pages.Count) pages.Add(new T[PageSize]);
        pages[index / PageSize][index % PageSize] = value;
        Count++;
        return index;
    }
    /// <summary>Reads a previously retained value without exposing mutable page storage.</summary>
    internal T Get(int index)
    {
        if ((uint)index >= (uint)Count) throw new ArgumentOutOfRangeException(nameof(index));
        return pages[index / PageSize][index % PageSize];
    }
    /// <summary>Releases retained values while keeping pages for a subsequent recording.</summary>
    internal void Clear()
    {
        if (RuntimeHelpers.IsReferenceOrContainsReferences<T>())
            for (int page = 0; page * PageSize < Count; page++)
                Array.Clear(pages[page], 0, Math.Min(PageSize, Count - page * PageSize));
        Count = 0;
    }
    #endregion
}
