namespace VanillaGraphicsExpanded.PBR.Materials;

internal readonly record struct PbrMaterialNoise(
    float Roughness,
    float Metallic,
    float Emissive,
    float Reflectivity,
    float Normals);

/// <summary>Resolved material shading and independent physical displacement opt-in.</summary>
internal readonly record struct PbrMaterialDefinition(
    float Roughness,
    float Metallic,
    float Emissive,
    PbrMaterialNoise Noise,
    PbrOverrideScale Scale,
    int Priority,
    string? Notes,
    float DisplacementAmplitudeMetres = 0,
    float Transmission = 0)
{
    /// <summary>Resolved root values, retaining the existing material constructor and atlas contract.</summary>
    public BRDFProperties Properties => new(Roughness, Metallic, Emissive, Noise, Scale, Transmission);

}
