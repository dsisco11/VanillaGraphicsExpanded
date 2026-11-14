using System.Numerics;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.PBR.Atmosphere.Celestial;

/// <summary>Packs the owned lunar disk's admitted geometry and physical surface radiance.</summary>
internal sealed class MoonInputs : CpuUniformBuffer
{
    #region Public API
    /// <summary>Byte extent of this concrete std140 layout.</summary>
    internal const int BlockSize = 80;
    /// <summary>Declares the immutable byte extent owned by this layout.</summary>
    public override int SizeBytes => BlockSize;

    /// <summary>Allocates five std140 vectors.</summary>
    internal MoonInputs() : base() { }
    /// <summary>Publishes one immutable disk without borrowing calendar or legacy shader values.</summary>
    internal void Capture(MoonDisk disk)
    {
        WriteVector4(0,new(disk.Direction,disk.AngularRadius));
        WriteVector4(16,new(disk.Right,disk.Horizon));
        WriteVector4(32,new(disk.Up,0));
        WriteVector4(48,new(disk.SurfaceLight,0));
        WriteVector4(64,new(disk.Radiance,0));
    }
    #endregion
}
