using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using VanillaGraphicsExpanded.LumOn.Scene.Fallback;

namespace VanillaGraphicsExpanded.LumOn.Scene.HitLighting;

/// <summary>Encodes hit-capture admission and suppression metadata and decodes complete geometric samples.</summary>
internal static class SurfaceHitCaptureCodec
{
    public const int MaximumCaptures = 16, HeaderBytes = 544, RecordBytes = 4176;

    #region Header encoding and record decoding
    /// <summary>Packs a cleared header with bounded admission and retained texel suppression identities.</summary>
    public static void EncodeHeader(Span<uint> header, int pages, uint selection, IReadOnlyList<SurfaceHitRetryEntry> entries)
    {
        if (header.Length != (HeaderBytes >> 2) || pages <= 0 || entries.Count > SurfaceHitRetryCache.Capacity) throw new InvalidOperationException("Invalid hit capture admission.");
        // Reset the append count and unused suppression slots before encoding this dispatch's admission.
        header.Clear();
        header[1] = (uint)Math.Min(MaximumCaptures, SurfaceHitRetryCache.Capacity - entries.Count);
        header[2] = (uint)pages; header[3] = selection; header[4] = (uint)entries.Count;
        for (int i = 0; i < entries.Count; i++)
        {
            var request = entries[i].Request;
            int offset = 8 + (i << 2);
            header[offset] = request.Page; header[offset + 1] = request.Slot;
            header[offset + 2] = request.Patch; header[offset + 3] = request.Linear;
        }
    }

    /// <summary>Projects complete geometric records while the queue owns their mapping, without copying unused fixed-size slots.</summary>
    public static ImmutableArray<SurfaceFallbackResult> Decode(ReadOnlySpan<SurfaceHitCapture> records)
    {
        var captured = ImmutableArray.CreateBuilder<SurfaceFallbackResult>();
        foreach (ref readonly var record in records)
        {
            if (record.Complete != 1 || record.QueryCount == 0 || record.QueryCount > SurfaceFallbackWorker.MaximumRays) continue;
            var queries = ImmutableArray.CreateBuilder<SurfaceLightingQuery>((int)record.QueryCount);
            for (int i = 0; i < record.QueryCount; i++) queries.Add(record.Queries[i]);
            captured.Add(new([new(record.Request, 0, queries.Count, true)], queries.MoveToImmutable(), []));
        }
        return captured.ToImmutable();
    }

    #endregion
}
