using System;
using System.Numerics;
using System.Runtime.InteropServices;

namespace VanillaGraphicsExpanded.Rendering.Shaders.Fixtures;

/// <summary>Packs the WaterReceiverFilter numerical inputs using their std140 offsets.</summary>
internal sealed class WaterReceiverFilterUniformBuffer : CpuUniformBuffer
{
    private const int SurfaceBatchOffset = 48;
    private const int SampleUvBatchOffset = SurfaceBatchOffset + 64 * 16;
    private const int BatchCapacity = 64;

    #region Public API
    /// <summary>Byte extent of this concrete std140 layout.</summary>
    internal const int BlockSize = SampleUvBatchOffset + BatchCapacity * 16;
    /// <summary>Declares the immutable byte extent owned by this layout.</summary>
    public override int SizeBytes => BlockSize;

    /// <summary>Creates zero-default scalar and bounded motion inputs with immutable single-frame publication.</summary>
    internal WaterReceiverFilterUniformBuffer() : base() { }

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

    /// <summary>Selects the scalar input path when zero, or the number of CPU-authored records to index.</summary>
    internal int BatchCount
    {
        get => MemoryMarshal.Read<int>(Bytes.Slice(44));
        set
        {
            if ((uint)value > BatchCapacity) throw new ArgumentOutOfRangeException(nameof(value));
            WriteInt32(44, value);
        }
    }

    /// <summary>Writes paired surface and sample coordinates, clearing every unpopulated slot.</summary>
    /// <param name="surfaces">CPU-authored surface records in original motion order.</param>
    /// <param name="sampleUvs">CPU-authored normalized receiver coordinates for the corresponding surfaces.</param>
    internal void SetBatch(ReadOnlySpan<Vector4> surfaces, ReadOnlySpan<Vector4> sampleUvs)
    {
        if (surfaces.Length > BatchCapacity) throw new ArgumentOutOfRangeException(nameof(surfaces));
        if (sampleUvs.Length != surfaces.Length) throw new ArgumentException("Surface and UV batches must have equal lengths.", nameof(sampleUvs));
        for (int index = 0; index < BatchCapacity; index++)
        {
            WriteVector4(SurfaceBatchOffset + index * 16, index < surfaces.Length ? surfaces[index] : Vector4.Zero);
            WriteVector4(SampleUvBatchOffset + index * 16, index < sampleUvs.Length ? sampleUvs[index] : Vector4.Zero);
        }
        BatchCount = surfaces.Length;
    }

    /// <summary>Resets scalar and paired motion data while preserving snapshots captured by earlier draws.</summary>
    internal void Reset()
    {
        DataWritable.Clear();
        MarkDirty();
    }

    #endregion
}
