using System;
using VanillaGraphicsExpanded.Voxels.ChunkProcessing;
using VanillaGraphicsExpanded.WorldPartition;

namespace VanillaGraphicsExpanded.LumOn.Scene.NearField;

/// <summary>Owned immutable cell payload, exposed only through a read-only span.</summary>
internal sealed class NearFieldCellContent : IPartitionContent
{
    private readonly NearFieldSourceCell[] cells;
    public ReadOnlySpan<NearFieldSourceCell> Cells => cells;
    public bool Unsupported { get; }

    /// <summary>Takes the extraction buffer, which is not shared with mutable source state.</summary>
    internal NearFieldCellContent(NearFieldSourceCell[] cells, bool unsupported)
    {
        this.cells = cells;
        Unsupported = unsupported;
    }
}
