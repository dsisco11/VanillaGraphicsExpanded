using System.Numerics;
using System.Runtime.InteropServices;

namespace VanillaGraphicsExpanded.Rendering.Shaders.Fixtures;

/// <summary>Packs the WaterPixelNormalRefraction numerical inputs using their std140 offsets.</summary>
internal sealed class WaterPixelNormalRefractionUniformBuffer : CpuUniformBuffer
{
    #region Public API
    /// <summary>Byte extent of this concrete std140 layout.</summary>
    internal const int BlockSize = 48;
    /// <summary>Declares the immutable byte extent owned by this layout.</summary>
    public override int SizeBytes => BlockSize;

    /// <summary>Creates zero-default inputs with immutable single-frame publication.</summary>
    internal WaterPixelNormalRefractionUniformBuffer() : base() { }

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
        get => MemoryMarshal.Read<int>(Bytes.Slice(44));
        set => WriteInt32(44, value);
    }

    /// <summary>Supplies the underlying geometric normal before wave detail.</summary>
    internal Vector3 BaseNormal
    {
        get => MemoryMarshal.Read<Vector3>(Bytes.Slice(32));
        set => WriteVector3(32, value);
    }

    /// <summary>Clears every packed field while preserving immutable snapshots captured for prior submissions.</summary>
    internal void Reset()
    {
        DataWritable.Clear();
        MarkDirty();
    }
    #endregion
}
