using System.Numerics;
using System.Runtime.InteropServices;

namespace VanillaGraphicsExpanded.Rendering.Shaders.Fixtures;

/// <summary>Packs the WaterReceiverFilter numerical inputs using their std140 offsets.</summary>
internal sealed class WaterReceiverFilterUniformBuffer : CpuUniformBuffer
{
    #region Public API
    /// <summary>Creates zero-default inputs with immutable single-frame publication.</summary>
    internal WaterReceiverFilterUniformBuffer() : base(48) { }

    /// <summary>Locates the requested normalized background sample.</summary>
    internal Vector2 SampleUv
    {
        get => MemoryMarshal.Read<Vector2>(Bytes.Slice(0));
        set => WriteVector2(0, value);
    }

    /// <summary>Supplies the local interface point in view coordinates.</summary>
    internal Vector3 Surface
    {
        get => MemoryMarshal.Read<Vector3>(Bytes.Slice(16));
        set => WriteVector3(16, value);
    }

    /// <summary>Supplies the oriented local interface normal.</summary>
    internal Vector3 Normal
    {
        get => MemoryMarshal.Read<Vector3>(Bytes.Slice(32));
        set => WriteVector3(32, value);
    }

    #endregion
}
