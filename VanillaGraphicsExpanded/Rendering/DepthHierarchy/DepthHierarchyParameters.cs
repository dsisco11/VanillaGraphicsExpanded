using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Owns the complete std140 layout for depth hierarchy parameters.</summary>
internal sealed class DepthHierarchyParameters : CpuUniformBuffer
{
    #region Public API
    internal const int BlockSize = 16;
    internal const int ExtentOffset = 0;

    /// <summary>Declares this layout's immutable byte extent.</summary>
    public override int SizeBytes => BlockSize;

    /// <summary>Stages the complete input extent and generated mip count.</summary>
    internal void SetExtent(int width, int height, int levels)
    {
        WriteIntVector4(ExtentOffset, width, height, levels, 0);
    }
    #endregion
}
