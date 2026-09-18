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
            UboPacking.WriteMat4(DataWritable, 0, value);
            MarkDirty(0, 64);
        }
    }

    public Vec3f CameraPos
    {
        set
        {
            UboPacking.WriteVec4(DataWritable, 64, value.X, value.Y, value.Z, 0f);
            MarkDirty(64, 16);
        }
    }

    public Vec3f WorldOffset
    {
        set
        {
            var (_, _, _, pointSize) = UboPacking.ReadVec4(DataReadOnly, 80);
            UboPacking.WriteVec4(DataWritable, 80, value.X, value.Y, value.Z, pointSize);
            MarkDirty(80, 16);
        }
    }

    public float PointSize
    {
        set
        {
            var (x, y, z, _) = UboPacking.ReadVec4(DataReadOnly, 80);
            UboPacking.WriteVec4(DataWritable, 80, x, y, z, value);
            MarkDirty(80, 16);
        }
    }

    public float FadeNear
    {
        set
        {
            var (_, fadeFar, _, _) = UboPacking.ReadVec4(DataReadOnly, 96);
            UboPacking.WriteVec4(DataWritable, 96, value, fadeFar, 0f, 0f);
            MarkDirty(96, 16);
        }
    }

    public float FadeFar
    {
        set
        {
            var (fadeNear, _, _, _) = UboPacking.ReadVec4(DataReadOnly, 96);
            UboPacking.WriteVec4(DataWritable, 96, fadeNear, value, 0f, 0f);
            MarkDirty(96, 16);
        }
    }
}