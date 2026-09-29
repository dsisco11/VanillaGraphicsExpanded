namespace VanillaGraphicsExpanded.PBR.Materials;

/// <summary>Immutable resolved properties for a material.</summary>
internal readonly record struct BRDFProperties(float Roughness, float Metallic, float Emissive,
    PbrMaterialNoise Noise, PbrOverrideScale Scale, float Transmission = 0);
