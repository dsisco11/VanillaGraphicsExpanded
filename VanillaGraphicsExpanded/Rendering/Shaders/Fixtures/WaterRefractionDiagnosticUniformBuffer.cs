using System.Numerics;
using System.Runtime.InteropServices;

namespace VanillaGraphicsExpanded.Rendering.Shaders.Fixtures;

/// <summary>Packs the WaterRefractionDiagnostic numerical inputs using their std140 offsets.</summary>
internal sealed class WaterRefractionDiagnosticUniformBuffer : CpuUniformBuffer
{
    #region Public API
    /// <summary>Creates zero-default inputs with immutable single-frame publication.</summary>
    internal WaterRefractionDiagnosticUniformBuffer() : base(192) { }

    /// <summary>Selects the independently authored optical geometry.</summary>
    internal int Scenario
    {
        get => MemoryMarshal.Read<int>(Bytes.Slice(0));
        set => WriteInt32(0, value);
    }

    /// <summary>Supplies full-frame projection dimensions.</summary>
    internal Vector2 FrameSize
    {
        get => MemoryMarshal.Read<Vector2>(Bytes.Slice(8));
        set => WriteVector2(8, value);
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

    /// <summary>Supplies the CPU-authored projection used to generate the receiver depths.</summary>
    internal Matrix4x4 Projection
    {
        get => MemoryMarshal.Read<Matrix4x4>(Bytes.Slice(64));
        set => WriteMatrix4(64, value);
    }

    /// <summary>Supplies its CPU inverse for production receiver reconstruction.</summary>
    internal Matrix4x4 InverseProjection
    {
        get => MemoryMarshal.Read<Matrix4x4>(Bytes.Slice(128));
        set => WriteMatrix4(128, value);
    }
    #endregion
}
