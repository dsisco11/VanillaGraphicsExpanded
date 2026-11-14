using System;
using System.Numerics;
using System.Runtime.InteropServices;

namespace VanillaGraphicsExpanded.Rendering.Shaders.Fixtures;

/// <summary>Packs the WaterRefractionDiagnostic numerical inputs using their std140 offsets.</summary>
internal sealed class WaterRefractionDiagnosticUniformBuffer : CpuUniformBuffer
{
    private const int BatchSurfaceOffset = 64;
    private const int BatchCapacity = 64;

    #region Public API
    /// <summary>Byte extent of this concrete std140 layout.</summary>
    internal const int BlockSize = BatchSurfaceOffset + BatchCapacity * 16;
    /// <summary>Declares the immutable byte extent owned by this layout.</summary>
    public override int SizeBytes => BlockSize;

    /// <summary>Creates zero-default scalar and bounded motion inputs with immutable single-frame publication.</summary>
    internal WaterRefractionDiagnosticUniformBuffer() : base() { }

    /// <summary>Selects the independently authored optical geometry.</summary>
    internal int Scenario
    {
        get => MemoryMarshal.Read<int>(Bytes.Slice(0));
        set => WriteInt32(0, value);
    }

    /// <summary>Supplies an optional independently reconstructed surface for diagnostic scenario twelve.</summary>
    internal Vector3 Surface
    {
        get => MemoryMarshal.Read<Vector3>(Bytes.Slice(16));
        set => WriteVector3(16, value);
    }

    /// <summary>Supplies the corresponding oriented view-space water normal.</summary>
    internal Vector3 Normal
    {
        get => MemoryMarshal.Read<Vector3>(Bytes.Slice(32));
        set => WriteVector3(32, value);
    }

    /// <summary>Caps all receiver evaluations performed by the ray-only entry point.</summary>
    internal int Budget
    {
        get => MemoryMarshal.Read<int>(Bytes.Slice(44));
        set => WriteInt32(44, value);
    }

    /// <summary>Selects raw ray traversal (zero), the tier dispatcher (one), or standalone UV (two).</summary>
    internal int SelectReceiver
    {
        get => MemoryMarshal.Read<int>(Bytes.Slice(48));
        set => WriteInt32(48, value);
    }

    /// <summary>Chooses the production quality identifier when dispatch is requested.</summary>
    internal int Quality
    {
        get => MemoryMarshal.Read<int>(Bytes.Slice(52));
        set => WriteInt32(52, value);
    }

    /// <summary>Selects an underwater exit for custom optical geometry.</summary>
    internal int Underwater
    {
        get => MemoryMarshal.Read<int>(Bytes.Slice(56));
        set => WriteInt32(56, value);
    }

    /// <summary>Selects the scalar input path when zero, or the number of CPU-authored surfaces to index.</summary>
    internal int BatchCount
    {
        get => MemoryMarshal.Read<int>(Bytes.Slice(60));
        set
        {
            if ((uint)value > BatchCapacity) throw new ArgumentOutOfRangeException(nameof(value));
            WriteInt32(60, value);
        }
    }

    /// <summary>Writes a complete bounded surface array and clears every unpopulated slot.</summary>
    /// <param name="surfaces">CPU-authored surface records in original motion order.</param>
    internal void SetBatch(ReadOnlySpan<Vector4> surfaces)
    {
        if (surfaces.Length > BatchCapacity) throw new ArgumentOutOfRangeException(nameof(surfaces));
        for (int index = 0; index < BatchCapacity; index++)
            WriteVector4(BatchSurfaceOffset + index * 16, index < surfaces.Length ? surfaces[index] : Vector4.Zero);
        BatchCount = surfaces.Length;
    }

    /// <summary>Resets scalar and motion data while preserving immutable copies already captured for earlier draws.</summary>
    internal void Reset()
    {
        DataWritable.Clear();
        MarkDirty();
    }

    #endregion
}
