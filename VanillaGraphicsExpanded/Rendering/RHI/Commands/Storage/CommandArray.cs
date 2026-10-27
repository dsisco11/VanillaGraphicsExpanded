using System.Runtime.CompilerServices;
using System;
namespace VanillaGraphicsExpanded.Rendering.RHI;
/// <summary>Copies variable-length typed arguments into reusable list-owned storage.</summary>
internal sealed class CommandArray<T>
{
    private T[] values = [];
    private int count;
    #region Internal API
    /// <summary>Freezes supplied elements without allocating a separate array per command.</summary>
    internal (int Offset, int Length) Add(ReadOnlySpan<T> source)
    {
        int start = count, end = checked(start + source.Length);
        if (end > values.Length) Array.Resize(ref values, Math.Max(end, Math.Max(64, checked(values.Length * 2))));
        source.CopyTo(values.AsSpan(start)); count = end; return (start, source.Length);
    }
    /// <summary>Reads the frozen interval during exclusive dispatch.</summary>
    internal ReadOnlySpan<T> Read((int Offset, int Length) range) => values.AsSpan(range.Offset, range.Length);
    /// <summary>Releases managed references without surrendering reusable capacity.</summary>
    internal void Clear()
    {
        if (RuntimeHelpers.IsReferenceOrContainsReferences<T>()) Array.Clear(values, 0, count);
        count = 0;
    }
    #endregion
}
