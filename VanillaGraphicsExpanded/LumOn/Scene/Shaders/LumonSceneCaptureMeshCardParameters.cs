using System;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.LumOn.Scene.Shaders;

/// <summary>Owns the complete std140 layout for lumon scene capture mesh card parameters.</summary>
internal sealed class LumonSceneCaptureMeshCardParameters : CpuUniformBuffer
{
    #region Public API
    internal const int BlockSize = 32;
    internal const int AtlasLayoutOffsetBytes = 0;
    internal const int CaptureFloats0OffsetBytes = 16;

    /// <summary>Declares this layout's immutable byte extent.</summary>
    public override int SizeBytes => BlockSize;
    /// <summary>Stages the complete atlas mapping lane without changing other dispatch fields.</summary>
    internal void SetAtlasLayout(uint tileSize, uint tilesPerAxis, uint tilesPerAtlas, uint border)
    {
        WriteUIntVector4(AtlasLayoutOffsetBytes, tileSize, tilesPerAxis, tilesPerAtlas, border);
    }

    /// <summary>Stages the depth scale for card capture.</summary>
    internal void SetCaptureDepth(float depth)
    {
        WriteVector4(CaptureFloats0OffsetBytes, new(depth, 0, 0, 0));
    }
    #endregion
}
