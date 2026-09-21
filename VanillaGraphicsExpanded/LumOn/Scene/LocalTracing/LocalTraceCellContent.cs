using System;
using VanillaGraphicsExpanded.Voxels.ChunkProcessing;
using VanillaGraphicsExpanded.WorldPartition;

namespace VanillaGraphicsExpanded.LumOn.Scene.LocalTracing;

/// <summary>Owned immutable cell payload, exposed only through a read-only span.</summary>
internal sealed class LocalTraceCellContent : IPartitionContent
{
    private readonly LocalTraceSourceCell[] cells;
    public ReadOnlySpan<LocalTraceSourceCell> Cells => cells;
    public bool Unsupported { get; }

    /// <summary>Takes the extraction buffer, which is not shared with mutable source state.</summary>
    internal LocalTraceCellContent(LocalTraceSourceCell[] cells, bool unsupported)
    {
        this.cells = cells;
        Unsupported = unsupported;
    }
}
