using System.Numerics;

namespace VanillaGraphicsExpanded.PBR.Materials;

internal readonly record struct PbrMaterialSurface(
    float Roughness,
    float Metallic,
    float Emissive,
    Vector3 DiffuseAlbedo,
    Vector3 SpecularF0)
{
    public static readonly PbrMaterialSurface Default = new(
        Roughness: 0.85f,
        Metallic: 0f,
        Emissive: 0f,
        DiffuseAlbedo: new Vector3(0.55f),
        SpecularF0: new Vector3(0.04f));
}
