namespace VanillaGraphicsExpanded.Rendering.RHI;
/// <summary>Provides private issuance and residency execution for existing optional bindless owners.</summary>
internal partial interface IRHIResourceExecution
{
    /// <summary>Issues a native handle for validated typed inputs, without changing ownership.</summary>
    ulong CreateTextureHandle(GpuTextureRef texture, GpuSamplerRef sampler);
    /// <summary>Changes residency for the caller's original-context native handle.</summary>
    void SetTextureHandleResidency(ulong handle, bool resident);
}
