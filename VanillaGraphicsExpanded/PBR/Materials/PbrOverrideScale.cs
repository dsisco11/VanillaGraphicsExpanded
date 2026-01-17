namespace VanillaGraphicsExpanded.PBR.Materials;

internal readonly record struct PbrOverrideScale(
    float Roughness,
    float Metallic,
    float Emissive,
    float Normal,
    float Depth)
{
    public static readonly PbrOverrideScale Identity = new(
        Roughness: 1f,
        Metallic: 1f,
        Emissive: 1f,
        Normal: 1f,
        Depth: 1f);
}
