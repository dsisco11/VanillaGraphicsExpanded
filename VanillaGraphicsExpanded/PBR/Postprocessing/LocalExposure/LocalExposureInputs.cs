using VanillaGraphicsExpanded.Rendering;
namespace VanillaGraphicsExpanded.PBR.Postprocessing.LocalExposure;
/// <summary>Packs spatial-filter parameters into the existing shader-input slot.</summary>
internal sealed class LocalExposureInputs : CpuUniformBuffer
{
    #region Public API
    /// <summary>Byte extent of this concrete std140 layout.</summary>
    internal const int BlockSize = 16;
    /// <summary>Declares the immutable byte extent owned by this layout.</summary>
    public override int SizeBytes => BlockSize;

    /// <summary>Reserves one std140 vector for the current candidate.</summary>
    internal LocalExposureInputs() : base() { }
    /// <summary>Publishes the bilateral range width; geometry comes from actual texture dimensions.</summary>
    internal void Capture(float rangeWidth)=>WriteVector4(0,new(rangeWidth,0,0,0));
    #endregion
}
