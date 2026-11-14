using System;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.LumOn.Scene.Shaders;

/// <summary>Owns the complete std140 layout for lumon scene feedback gather parameters.</summary>
internal sealed class LumonSceneFeedbackGatherParameters : CpuUniformBuffer
{
    #region Public API
    internal const int BlockSize = 32;
    internal const int RequestOffsetBytes = 0;
    internal const int ScreenOffsetBytes = 16;

    /// <summary>Declares this layout's immutable byte extent.</summary>
    public override int SizeBytes => BlockSize;
    /// <summary>Sets the request capacity while retaining the sampling configuration.</summary>
    internal void SetMaxRequests(uint value) => WriteUInt32(RequestOffsetBytes, value);

    /// <summary>Sets the sample budget while retaining the request capacity.</summary>
    internal void SetSampleCount(uint value) => WriteUInt32(RequestOffsetBytes + 2 * sizeof(uint), value);

    /// <summary>Sets the extent used to distribute feedback samples.</summary>
    internal void SetScreenSize(uint width, uint height) => WriteUIntVector4(ScreenOffsetBytes, width, height, 0, 0);
    #endregion
}
