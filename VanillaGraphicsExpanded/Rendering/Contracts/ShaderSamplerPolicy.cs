namespace VanillaGraphicsExpanded.Rendering.Contracts;

/// <summary>Selects the shared sampler object used when publishing a texture binding.</summary>
internal enum ShaderSamplerPolicy { Default, NearestClamp, LinearClamp, ShadowCompareLinearClamp }
