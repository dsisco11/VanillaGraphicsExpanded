using System;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.LumOn.Scene.Shaders;

/// <summary>Owns the complete std140 layout for lumon scene capture voxel parameters.</summary>
internal sealed class LumonSceneCaptureVoxelParameters : CpuUniformBuffer
{
    #region Public API
    internal const int BlockSize = 64;
    internal const int AtlasLayoutOffsetBytes = 0;
    internal const int DiagnosticsOffsetBytes = 16;

    /// <summary>Declares this layout's immutable byte extent.</summary>
    public override int SizeBytes => BlockSize;
    /// <summary>Stages the complete atlas mapping lane without changing other dispatch fields.</summary>
    internal void SetAtlasLayout(uint tileSize, uint tilesPerAxis, uint tilesPerAtlas, uint border)
    {
        WriteUIntVector4(AtlasLayoutOffsetBytes, tileSize, tilesPerAxis, tilesPerAtlas, border);
    }

    /// <summary>Stages whether the current capture writes diagnostic counters.</summary>
    internal void SetDiagnostics(bool enabled)
    {
        WriteUIntVector4(DiagnosticsOffsetBytes, enabled ? 1u : 0u, 0, 0, 0);
    }
    #endregion
}
