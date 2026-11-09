using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.Support.GPU.Observations;

namespace VanillaGraphicsExpanded.Tests.Component.AmbientOcclusion.Fixtures;

/// <summary>Owns independently isolated direct, diffuse, specular and combined lighting observations.</summary>
internal sealed class AmbientOcclusionLightingObservation(IEnumerable<GpuTexture> sources):IDisposable
{
    private readonly GpuImageSet images=new(sources);
    #region Public API
    /// <summary>Retires every retained lighting product in its originating context.</summary>
    public void Dispose()=>images.Dispose();
    #endregion
    #region Internal API
    /// <summary>Gets the direct-only lighting control.</summary>
    internal GpuTexture Direct=>images[0].Texture;
    /// <summary>Gets the isolated ambient diffuse contribution.</summary>
    internal GpuTexture Diffuse=>images[1].Texture;
    /// <summary>Gets the isolated indirect specular contribution.</summary>
    internal GpuTexture Specular=>images[2].Texture;
    /// <summary>Gets the combined lighting result.</summary>
    internal GpuTexture Combined=>images[3].Texture;
    #endregion
}
