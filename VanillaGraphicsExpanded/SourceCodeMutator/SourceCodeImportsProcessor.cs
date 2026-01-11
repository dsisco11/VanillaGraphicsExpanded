using System;
using System.Buffers;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
namespace VanillaGraphicsExpanded;

/// <summary>
/// Processes <c>@import</c> directives in shader source code using TinyAst.
/// </summary>
/// <remarks>
/// This processor finds all <c>@import</c> directives (matched as <see cref="GlDirectiveNode"/>)
/// and replaces them with the referenced file contents, commenting out the original directive for debugging.
/// </remarks>
public static class SourceCodeImportsProcessor
{
    // Mask to check for non-ASCII: if any char has bit 7+ set, it's non-ASCII
    private static readonly Vector<ushort> AsciiMask = new(0xFF80);

    /// <summary>
    /// Strips non-ASCII characters from shader source code.
    /// GLSL specification only allows ASCII characters (0x00-0x7F).
    /// Uses .NET 8's built-in SIMD-optimized Ascii.IsValid for the fast path,
    /// and custom SIMD for stripping when non-ASCII is found.
    /// </summary>
    /// <param name="source">The shader source code.</param>
    /// <returns>Source code with non-ASCII characters removed.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static string StripNonAscii(string source)
    {
        if (string.IsNullOrEmpty(source))
            return source;

        // Fast path: use .NET 8's built-in SIMD-optimized ASCII check
        if (System.Text.Ascii.IsValid(source))
            return source;

        // Need to strip non-ASCII characters
        return StripNonAsciiCore(source.AsSpan());
    }

    /// <summary>
    /// Strips non-ASCII characters when we know there are some present.
    /// Uses SIMD to find ASCII runs and bulk copy them.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static string StripNonAsciiCore(ReadOnlySpan<char> source)
    {
        // Rent a buffer from the pool - worst case same size as input
        char[] buffer = ArrayPool<char>.Shared.Rent(source.Length);
        try
        {
            Span<char> dest = buffer.AsSpan();
            int writeIndex = 0;
            int i = 0;

            // Process in chunks: find ASCII runs and copy them in bulk
            while (i < source.Length)
            {
                // Skip any non-ASCII characters
                while (i < source.Length && source[i] > 127)
                    i++;

                if (i >= source.Length)
                    break;

                // Find the end of this ASCII run using SIMD
                int asciiRunStart = i;
                int asciiRunEnd = FindNextNonAsciiSimd(source, i);

                // Copy the ASCII run in bulk
                int runLength = asciiRunEnd - asciiRunStart;
                if (runLength > 0)
                {
                    source.Slice(asciiRunStart, runLength).CopyTo(dest.Slice(writeIndex));
                    writeIndex += runLength;
                }

                i = asciiRunEnd;
            }

            return new string(buffer, 0, writeIndex);
        }
        finally
        {
            ArrayPool<char>.Shared.Return(buffer);
        }
    }

    /// <summary>
    /// Finds the index of the next non-ASCII character starting from the given position.
    /// Uses SIMD for bulk scanning. Returns source.Length if no non-ASCII found.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static int FindNextNonAsciiSimd(ReadOnlySpan<char> span, int start)
    {
        if (start >= span.Length)
            return span.Length;

        int i = start;

        // SIMD path: process Vector<ushort>.Count chars at a time
        if (Vector.IsHardwareAccelerated)
        {
            int vectorizableEnd = span.Length - Vector<ushort>.Count;

            if (i <= vectorizableEnd)
            {
                ref char searchStart = ref MemoryMarshal.GetReference(span);

                while (i <= vectorizableEnd)
                {
                    Vector<ushort> chars = Unsafe.ReadUnaligned<Vector<ushort>>(
                        ref Unsafe.As<char, byte>(ref Unsafe.Add(ref searchStart, i)));

                    Vector<ushort> masked = Vector.BitwiseAnd(chars, AsciiMask);
                    if (!Vector.EqualsAll(masked, Vector<ushort>.Zero))
                    {
                        // Found non-ASCII, find exact position
                        for (int j = 0; j < Vector<ushort>.Count; j++)
                        {
                            if (span[i + j] > 127)
                                return i + j;
                        }
                    }
                    i += Vector<ushort>.Count;
                }
            }
        }

        // Scalar tail
        for (; i < span.Length; i++)
        {
            if (span[i] > 127)
                return i;
        }

        return span.Length;
    }
}
