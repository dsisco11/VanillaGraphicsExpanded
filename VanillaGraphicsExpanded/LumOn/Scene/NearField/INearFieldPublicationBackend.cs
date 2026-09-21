using System;
using VanillaGraphicsExpanded.Voxels.ChunkProcessing;
using VanillaGraphicsExpanded.WorldPartition;

namespace VanillaGraphicsExpanded.LumOn.Scene.NearField;

/// <summary>Publication backend boundary permits lifecycle tests without a graphics context.</summary>
internal interface INearFieldPublicationBackend
{
    /// <summary>Tests whether a logical cell is addressable by the bounded backend.</summary>
    bool ContainsCell(in PartitionCoordinate coordinate);
    /// <summary>Claims storage only after coordinator validation.</summary>
    bool ClaimCell(PartitionRequest request);
    /// <summary>Acknowledges a coherent upload.</summary>
    bool PublishCell(PartitionRequest request, ReadOnlySpan<NearFieldSourceCell> cells, NearFieldMaterialRegistry materials);
    /// <summary>Hides stale data immediately.</summary>
    void InvalidateCell(in PartitionCellKey key);
    /// <summary>Retires the current logical owner.</summary>
    void RetireCell(in PartitionCellKey key);
}
