using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Assertions;

namespace VanillaGraphicsExpanded.Tests.GPU.Fixtures;

/// <summary>Owns independent raw, filtered and reconstructed AO observations across subsequent draws.</summary>
internal sealed class AmbientOcclusionStages(IEnumerable<GpuTexture> sources):IDisposable
{
    private readonly GpuImageSet images=new(sources);
    #region Public API
    /// <summary>Retires every retained stage in its originating context.</summary>
    public void Dispose()=>images.Dispose();
    #endregion
    #region Internal API
    /// <summary>Gets the raw horizon visibility and receiver metadata.</summary>
    internal GpuTexture Raw=>images[0].Texture;
    /// <summary>Gets the spatially filtered reduced-domain visibility.</summary>
    internal GpuTexture Filtered=>images[1].Texture;
    /// <summary>Gets the reconstructed full-domain visibility.</summary>
    internal GpuTexture Reconstructed=>images[2].Texture;
    #endregion
}

