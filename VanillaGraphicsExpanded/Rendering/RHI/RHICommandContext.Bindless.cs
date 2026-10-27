namespace VanillaGraphicsExpanded.Rendering.RHI;
/// <summary>Routes optional native handle-owner operations through the private execution contract.</summary>
public sealed partial class RHICommandContext
{
    #region Internal API
    /// <summary>Issues one validated native owner handle without exposing native identifiers publicly.</summary>
    internal ulong CreateTextureHandle(GpuTextureRef texture, GpuSamplerRef sampler) => backend.CreateTextureHandle(texture, sampler);
    /// <summary>Changes residency on behalf of an existing owner in its original context.</summary>
    internal void SetTextureHandleResidency(ulong handle, bool resident) => backend.SetTextureHandleResidency(handle, resident);
    #endregion
}
