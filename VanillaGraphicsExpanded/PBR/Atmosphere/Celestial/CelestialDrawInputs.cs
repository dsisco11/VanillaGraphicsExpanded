using System.Numerics;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.PBR.Atmosphere.Celestial;

/// <summary>Packs one camera-independent rotation and matching source/medium metadata.</summary>
internal sealed class CelestialDrawInputs : CpuUniformBuffer
{
    #region Public API
    /// <summary>Allocates a std140 matrix and source metadata vector.</summary>
    internal CelestialDrawInputs() : base(80) { }
    /// <summary>Uses column-major GPU packing for the row-vector CPU rotation.</summary>
    internal void Capture(Matrix4x4 rotation, float inverseEncoding, float horizon)
    {
        WriteVector4(0, new(rotation.M11, rotation.M12, rotation.M13, 0));
        WriteVector4(16, new(rotation.M21, rotation.M22, rotation.M23, 0));
        WriteVector4(32, new(rotation.M31, rotation.M32, rotation.M33, 0));
        WriteVector4(48, new(0,0,0,1));
        WriteVector4(64, new(inverseEncoding, horizon, 0, 0));
    }
    #endregion
}
