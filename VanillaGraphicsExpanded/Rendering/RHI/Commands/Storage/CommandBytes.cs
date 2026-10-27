using System;

namespace VanillaGraphicsExpanded.Rendering.RHI;

/// <summary>Owns copied client payloads independently of mutable caller arrays.</summary>
internal sealed class CommandBytes
{
    private byte[] storage = [];
    private int count;
    #region Internal API
    /// <summary>Copies a supplied payload into reusable contiguous CPU storage.</summary>
    internal (int Offset, int Length) Add(ReadOnlySpan<byte> bytes)
    {
        int start = count;
        int end = checked(start + bytes.Length);
        if (end > storage.Length) Array.Resize(ref storage, Math.Max(end, Math.Max(256, checked(storage.Length * 2))));
        bytes.CopyTo(storage.AsSpan(start)); count = end;
        return (start, bytes.Length);
    }
    /// <summary>Exposes a retained interval only during exclusive synchronous execution.</summary>
    internal ReadOnlySpan<byte> Read((int Offset, int Length) range) => storage.AsSpan(range.Offset, range.Length);
    /// <summary>Releases logical payload ownership while retaining capacity.</summary>
    internal void Clear() => count = 0;
    #endregion
}
