using VanillaGraphicsExpanded.Rendering;
namespace VanillaGraphicsExpanded.PBR.Postprocessing.LocalExposure;
/// <summary>Packs spatial-filter parameters into the existing shader-input slot.</summary>
internal sealed class LocalExposureInputs : CpuUniformBuffer
{
    #region Public API
    /// <summary>Reserves one std140 vector for the current candidate.</summary>
    internal LocalExposureInputs() : base(16) { }
    /// <summary>Publishes the bilateral range width; geometry comes from actual texture dimensions.</summary>
    internal void Capture(float rangeWidth)=>WriteVector4(0,new(rangeWidth,0,0,0));
    #endregion
}
