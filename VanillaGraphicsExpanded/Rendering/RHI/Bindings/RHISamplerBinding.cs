namespace VanillaGraphicsExpanded.Rendering.RHI;

/// <summary>Selects one authored sampler entry/array element and retains consumption-only resource references.</summary>
public readonly record struct RHISamplerBinding(ulong Identity, GpuTextureRef Texture, int Element = 0, GpuSamplerRef Sampler = default);
