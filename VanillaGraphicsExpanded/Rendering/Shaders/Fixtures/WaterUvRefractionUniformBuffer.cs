using System.Numerics;
using System.Runtime.InteropServices;

namespace VanillaGraphicsExpanded.Rendering.Shaders.Fixtures;

/// <summary>Packs the WaterUvRefraction numerical inputs using their std140 offsets.</summary>
internal sealed class WaterUvRefractionUniformBuffer : CpuUniformBuffer
{
    #region Public API
    /// <summary>Creates zero-default inputs with immutable single-frame publication.</summary>
    internal WaterUvRefractionUniformBuffer() : base(32) { }

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

    /// <summary>Selects the submerged-camera exit interface.</summary>
    internal int Underwater
    {
        get => MemoryMarshal.Read<int>(Bytes.Slice(28));
        set => WriteInt32(28, value);
    }

    #endregion
}
