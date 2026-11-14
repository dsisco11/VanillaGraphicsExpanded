using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.LumOn;

namespace VanillaGraphicsExpanded.Rendering.Shaders;

/// <summary>
/// CPU-side wrapper for VgeDebugLinesParamsUBO (std140, 16 bytes).
/// </summary>
public sealed class VgeDebugLinesParamsUbo : CpuUniformBuffer
{
    #region Public API
    /// <summary>Declares the immutable byte extent owned by this layout.</summary>
    public override int SizeBytes => UboSizeBytes;

    public const string BlockName = "VgeDebugLinesParamsUBO";
    public const int UboSizeBytes = 16;

    /// <summary>Allocates only the line-specific origin offset.</summary>
    public VgeDebugLinesParamsUbo() : base()
    {
    }

    /// <summary>Offsets independently authored line vertices within the shared camera space.</summary>
    public Vintagestory.API.MathTools.Vec3f WorldOffset
    {
        set
        {
            WriteVector4(0, new(value.X, value.Y, value.Z, 0f));
        }
    }
    #endregion
}
