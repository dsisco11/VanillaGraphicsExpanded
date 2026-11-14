using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.PBR.Postprocessing;

/// <summary>Owns the complete std140 layout for minimum child mask inputs.</summary>
internal sealed class MinimumChildMaskInputs : CpuUniformBuffer
{
    #region Public API
    internal const int BlockSize = 16;
    internal const int LevelOffset = 0;

    /// <summary>Declares this layout's immutable byte extent.</summary>
    public override int SizeBytes => BlockSize;

    /// <summary>Stages the parent mip level with deterministic unused components.</summary>
    internal void SetLevel(int level)
    {
        WriteIntVector4(LevelOffset, level, 0, 0, 0);
    }
    #endregion
}
