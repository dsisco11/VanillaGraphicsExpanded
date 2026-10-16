using System.Numerics;

namespace VanillaGraphicsExpanded.PBR.Materials;

/// <summary>Supplies view-independent diffuse transport and interface reflectivity to world-probe tracing.</summary>
internal readonly record struct DerivedSurface(
    Vector3 DiffuseAlbedo,
    Vector3 SpecularF0)
{
    #region Public API
    /// <summary>Retains the missing-material magenta signal while reserving the default interface's reflection budget.</summary>
    public static readonly DerivedSurface Default = new(
        DiffuseAlbedo: Energy.MicrofacetEnergyTable.Shared.DiffuseAlbedo(new PbrMaterialSurface(
            PbrMaterialSurface.Default.Roughness, 0, 0, new Vector3(1f, 0f, 1f), new Vector3(0.04f))),
        SpecularF0: new Vector3(0.04f));
    #endregion
}
