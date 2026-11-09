using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.Support.GPU.Observations;

namespace VanillaGraphicsExpanded.Tests.Support.Clouds;

/// <summary>Owns completed physical cloud products and optional diagnostic receipts independently of temporary filter targets.</summary>
internal sealed class CloudTemporalObservation : IDisposable
{
    private readonly GpuImageSet physical;
    private readonly GpuImageSnapshot? diagnostics;

    #region Public API
    /// <summary>Retires every retained product even if one resource reports a cleanup failure.</summary>
    public void Dispose()
    {
        try { physical.Dispose(); }
        finally { diagnostics?.Dispose(); }
    }
    #endregion

    #region Internal API
    /// <summary>Gets physical transport without OIT encoding.</summary>
    internal GpuTexture Cloud=>physical[0].Texture;
    /// <summary>Gets the three independent bucket transmissions.</summary>
    internal GpuTexture Bins=>physical[1].Texture;
    /// <summary>Gets depth, classification, distance and observation confidence.</summary>
    internal GpuTexture Metadata=>physical[2].Texture;
    /// <summary>Gets the required age image independently of optional diagnostics.</summary>
    internal GpuTexture Age=>physical[3].Texture;
    /// <summary>Gets optional contribution, update-class, rejection and age receipts.</summary>
    internal GpuTexture? Diagnostics=>diagnostics?.Texture;

    /// <summary>Captures all completed physical images before the filter target is disposed.</summary>
    internal CloudTemporalObservation(IEnumerable<GpuTexture> images,GpuTexture? receipt)
    {
        physical=new(images);
        try { diagnostics=receipt is null?null:new(receipt); }
        catch { physical.Dispose();throw; }
    }
    #endregion
}
