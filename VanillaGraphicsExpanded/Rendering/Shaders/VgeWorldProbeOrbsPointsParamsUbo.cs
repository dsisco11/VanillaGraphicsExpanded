using Vintagestory.API.MathTools;

using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.LumOn;

namespace VanillaGraphicsExpanded.Rendering.Shaders;

/// <summary>
/// CPU-side wrapper for VgeWorldProbeOrbsPointsParamsUBO (std140, 32 bytes).
/// </summary>
public sealed class VgeWorldProbeOrbsPointsParamsUbo : CpuUniformBuffer
{
    public const string BlockName = "VgeWorldProbeOrbsPointsParamsUBO";
    public const int UboSizeBytes = 32;

    /// <summary>Allocates point geometry and fading controls independently of the shared camera.</summary>
    public VgeWorldProbeOrbsPointsParamsUbo() : base(UboSizeBytes)
    {
    }

    /// <summary>Offsets point positions and preserves their requested screen size.</summary>
    public Vec3f WorldOffset
    {
        set
        {
            var (_, _, _, pointSize) = UboPacking.ReadVec4(DataReadOnly, 0);
            WriteVector4(0, new(value.X, value.Y, value.Z, pointSize));
        }
    }

    public float PointSize
    {
        set
        {
            var (x, y, z, _) = UboPacking.ReadVec4(DataReadOnly, 0);
            WriteVector4(0, new(x, y, z, value));
        }
    }

    public float FadeNear
    {
        set
        {
            WriteFloat(16, value);
        }
    }

    public float FadeFar
    {
        set
        {
            WriteFloat(20, value);
        }
    }
    /// <summary>Selects importance coloring in the retained draw parameters.</summary>
    public bool ImportanceColorMode { set => WriteFloat(24, value ? 1f : 0f); }
}
