using System;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.LumOn.Scene.Shaders;

/// <summary>Owns the complete std140 layout for lumon scene feedback compact pages parameters.</summary>
internal sealed class LumonSceneFeedbackCompactPagesParameters : CpuUniformBuffer
{
    #region Public API
    internal const int BlockSize = 16;
    internal const int RequestOffsetBytes = 0;

    /// <summary>Declares this layout's immutable byte extent.</summary>
    public override int SizeBytes => BlockSize;
    /// <summary>Sets the bounded output capacity.</summary>
    internal void SetMaxRequests(uint value) => WriteUInt32(RequestOffsetBytes, value);

    /// <summary>Sets the request publication stamp.</summary>
    internal void SetFrameStamp(uint value) => WriteUInt32(RequestOffsetBytes + sizeof(uint), value);

    /// <summary>Sets the first source page in the bounded scan.</summary>
    internal void SetScanOffset(uint value) => WriteUInt32(RequestOffsetBytes + 2 * sizeof(uint), value);

    /// <summary>Sets the scan operation.</summary>
    internal void SetCompactMode(uint value) => WriteUInt32(RequestOffsetBytes + 3 * sizeof(uint), value);
    #endregion
}
