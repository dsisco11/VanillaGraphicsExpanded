using System.Numerics;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.PBR.Atmosphere.Celestial;

/// <summary>Packs the calibrated solar emitter and its CPU-certified visible-disk storage limit.</summary>
internal sealed class SunInputs : CpuUniformBuffer
{
    #region Public API
    /// <summary>Byte extent of this concrete std140 layout.</summary>
    internal const int BlockSize = 64;
    /// <summary>Declares the immutable byte extent owned by this layout.</summary>
    public override int SizeBytes => BlockSize;

    /// <summary>Allocates four std140 vectors for disk geometry, horizon and radiance.</summary>
    internal SunInputs() : base() { }

    /// <summary>Uses one completed atmosphere snapshot without engine meshes, textures or artistic sun uniforms.</summary>
    internal void Capture(AtmosphereLighting lighting, float maximumStorageScale)
    {
        Vector3 direction = Vector3.Normalize(lighting.Sun);
        Vector3 right = Vector3.Normalize(Vector3.Cross(System.MathF.Abs(direction.Y) < .99f ? Vector3.UnitY : Vector3.UnitZ, direction));
        Vector3 up = Vector3.Cross(direction, right);
        WriteVector4(0, new(direction, AtmosphereSolarDisk.AngularRadius));
        WriteVector4(16, new(right, lighting.HorizonElevation));
        WriteVector4(32, new(up, maximumStorageScale));
        WriteVector4(48, new(AtmosphereSolarDisk.Radiance(lighting), 0));
    }
    #endregion
}
