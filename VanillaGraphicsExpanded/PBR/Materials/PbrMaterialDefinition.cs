namespace VanillaGraphicsExpanded.PBR.Materials;

internal readonly record struct PbrMaterialNoise(
    float Roughness,
    float Metallic,
    float Emissive,
    float Reflectivity,
    float Normals);

internal readonly record struct PbrMaterialDefinition(
    float Roughness,
    float Metallic,
    float Emissive,
    PbrMaterialNoise Noise,
    int Priority,
    string? Notes);
