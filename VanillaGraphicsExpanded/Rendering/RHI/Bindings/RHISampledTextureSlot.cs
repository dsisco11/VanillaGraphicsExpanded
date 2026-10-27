namespace VanillaGraphicsExpanded.Rendering.RHI;

/// <summary>Supplies a sampled resource and sampler for an explicitly declared consumer slot.</summary>
/// <remarks>The dimension determines which target an empty input clears. Nonempty inputs retain their original resource.</remarks>
public readonly record struct RHISampledTextureSlot(int Slot, GpuTextureRef Texture,
    GpuSamplerRef Sampler = default, TextureDimension Dimension = TextureDimension.Two);
