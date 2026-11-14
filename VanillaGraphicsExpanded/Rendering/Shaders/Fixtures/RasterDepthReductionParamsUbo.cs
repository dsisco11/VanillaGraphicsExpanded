
namespace VanillaGraphicsExpanded.Rendering;

/// <summary>
/// CPU-side wrapper for VgeDepthHierarchyParamsUBO (std140, 16 bytes).
/// </summary>
public sealed class RasterDepthReductionParamsUbo : CpuUniformBuffer
{
    #region Public API
    /// <summary>Declares the immutable byte extent owned by this layout.</summary>
    public override int SizeBytes => UboSizeBytes;

    /// <summary>Identifies the shared shader parameter block.</summary>
    public const string BlockName = "VgeDepthHierarchyParamsUBO";
    /// <summary>Includes std140 block alignment padding.</summary>
    public const int UboSizeBytes = 16;

    /// <summary>Allocates the source-level parameter block.</summary>
    public RasterDepthReductionParamsUbo() : base()
    {
    }

    /// <summary>Selects a level relative to the texture base level.</summary>
    public int SrcMip
    {
        get => UboPacking.ReadInt32(DataReadOnly, 0);
        set
        {
            WriteIntVector4(0, value, 0, 0, 0);
        }
    }
    #endregion
}
