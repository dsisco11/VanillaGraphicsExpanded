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

    public static PbrOverrideScale Multiply(PbrOverrideScale a, PbrOverrideScale b)
        => new(
            Roughness: a.Roughness * b.Roughness,
            Metallic: a.Metallic * b.Metallic,
            Emissive: a.Emissive * b.Emissive,
            Normal: a.Normal * b.Normal,
            Depth: a.Depth * b.Depth);
}
