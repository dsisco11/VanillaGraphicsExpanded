using Vintagestory.API.MathTools;

using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.LumOn;

namespace VanillaGraphicsExpanded.Rendering.Shaders;

/// <summary>
/// CPU-side wrapper for VgeWorldProbeOrbsPointsParamsUBO (std140, 112 bytes).
/// </summary>
public sealed class VgeWorldProbeOrbsPointsParamsUbo : CpuUniformBuffer
{
    public const string BlockName = "VgeWorldProbeOrbsPointsParamsUBO";
    public const int UboSizeBytes = 112;

    public VgeWorldProbeOrbsPointsParamsUbo() : base(UboSizeBytes)
    {
    }

    public float[] ModelViewProjectionMatrix
    {
        set
        {
            WriteMatrix4(0, value);
        }
    }

    public Vec3f CameraPos
    {
        set
        {
            WriteVector4(64, new(value.X, value.Y, value.Z, 0f));
        }
    }

    public Vec3f WorldOffset
    {
        set
        {
            var (_, _, _, pointSize) = UboPacking.ReadVec4(DataReadOnly, 80);
            WriteVector4(80, new(value.X, value.Y, value.Z, pointSize));
        }
    }

    public float PointSize
    {
        set
        {
            var (x, y, z, _) = UboPacking.ReadVec4(DataReadOnly, 80);
            WriteVector4(80, new(x, y, z, value));
        }
    }

    public float FadeNear
    {
        set
        {
            WriteFloat(96, value);
        }
    }

    public float FadeFar
    {
        set
        {
            WriteFloat(100, value);
        }
    }
    /// <summary>Selects importance coloring in the retained draw parameters.</summary>
    public bool ImportanceColorMode { set => WriteFloat(104, value ? 1f : 0f); }
}
