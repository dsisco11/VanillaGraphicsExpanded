using System;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.LumOn.Scene.Shaders;

/// <summary>Owns the complete std140 layout for lumon scene feedback mark pages parameters.</summary>
internal sealed class LumonSceneFeedbackMarkPagesParameters : CpuUniformBuffer
{
    #region Public API
    internal const int BlockSize = 16;
    internal const int FrameOffsetBytes = 0;

    /// <summary>Declares this layout's immutable byte extent.</summary>
    public override int SizeBytes => BlockSize;
    /// <summary>Stages the stamp while clearing unused components.</summary>
    internal void SetFrame(uint frame)
    {
        WriteUIntVector4(FrameOffsetBytes, frame, 0, 0, 0);
    }
    #endregion
}
