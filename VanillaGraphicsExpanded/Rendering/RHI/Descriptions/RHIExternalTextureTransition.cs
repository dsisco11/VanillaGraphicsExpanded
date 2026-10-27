namespace VanillaGraphicsExpanded.Rendering.RHI;
/// <summary>Identifies one image and its explicit layout at an external synchronization boundary.</summary>
public readonly record struct RHIExternalTextureTransition(GpuTextureRef Texture, ExternalTextureLayout Layout);
