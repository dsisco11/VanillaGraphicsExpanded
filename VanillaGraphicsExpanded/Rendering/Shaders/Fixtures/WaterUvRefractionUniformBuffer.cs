using System.Numerics;
using System.Runtime.InteropServices;

namespace VanillaGraphicsExpanded.Rendering.Shaders.Fixtures;

/// <summary>Packs the WaterUvRefraction numerical inputs using their std140 offsets.</summary>
internal sealed class WaterUvRefractionUniformBuffer : CpuUniformBuffer
{
    #region Public API
    /// <summary>Creates zero-default inputs with immutable single-frame publication.</summary>
    internal WaterUvRefractionUniformBuffer() : base(176) { }

    /// <summary>Supplies the view-space interface point.</summary>
    internal Vector3 Surface
    {
        get => MemoryMarshal.Read<Vector3>(Bytes.Slice(0));
        set => WriteVector3(0, value);
    }

    /// <summary>Supplies the oriented view-space interface normal.</summary>
    internal Vector3 Normal
    {
        get => MemoryMarshal.Read<Vector3>(Bytes.Slice(16));
        set => WriteVector3(16, value);
    }

    /// <summary>Projects optical geometry using the complete camera projection.</summary>
    internal Matrix4x4 Projection
    {
        get => MemoryMarshal.Read<Matrix4x4>(Bytes.Slice(32));
        set => WriteMatrix4(32, value);
    }

    /// <summary>Supplies original view dimensions independently of background resolution.</summary>
    internal Vector2 FrameSize
    {
        get => MemoryMarshal.Read<Vector2>(Bytes.Slice(160));
        set => WriteVector2(160, value);
    }

    /// <summary>Selects the submerged-camera exit interface.</summary>
    internal int Underwater
    {
        get => MemoryMarshal.Read<int>(Bytes.Slice(168));
        set => WriteInt32(168, value);
    }

    /// <summary>Reconstructs receivers with the CPU inverse of the supplied camera projection.</summary>
    internal Matrix4x4 InverseProjection
    {
        get => MemoryMarshal.Read<Matrix4x4>(Bytes.Slice(96));
        set => WriteMatrix4(96, value);
    }
    #endregion
}
