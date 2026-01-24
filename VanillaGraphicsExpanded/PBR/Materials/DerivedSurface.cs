using System.Numerics;

namespace VanillaGraphicsExpanded.PBR.Materials;

internal readonly record struct DerivedSurface(
    Vector3 DiffuseAlbedo,
    Vector3 SpecularF0)
{
    public static readonly DerivedSurface Default = new(
        DiffuseAlbedo: new Vector3(0.55f),
        SpecularF0: new Vector3(0.04f));
}
