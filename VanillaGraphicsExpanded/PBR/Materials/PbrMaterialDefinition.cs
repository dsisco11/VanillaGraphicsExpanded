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
    PbrOverrideScale Scale,
    int Priority,
    string? Notes)
{
    /// <summary>Resolved root values, retaining the existing material constructor and atlas contract.</summary>
    public BRDFProperties Properties => new(Roughness, Metallic, Emissive, Noise, Scale);

}
