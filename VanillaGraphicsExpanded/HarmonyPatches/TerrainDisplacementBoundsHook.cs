using HarmonyLib;
using Vintagestory.API.Client;
using Vintagestory.API.MathTools;
using VanillaGraphicsExpanded.PBR.Materials;

namespace VanillaGraphicsExpanded.HarmonyPatches;

/// <summary>Reserves the maximum authored displacement in stored terrain-pool visibility bounds.</summary>
[HarmonyPatch(typeof(MeshDataPoolManager), nameof(MeshDataPoolManager.AddModel))]
internal static class TerrainDisplacementBoundsHook
{
    #region Engine callbacks
    /// <summary>Expands the value-copy once at admission, including models uploaded before detail is enabled.</summary>
    [HarmonyPrefix]
    internal static void Prefix(ref Sphere frustumCullSphere)
    {
        // Sphere is the engine boundary type (three AABB extents); do not modify the caller's instance.
        const float maximumOffset = MaterialDisplacement.MaximumAmplitudeMetres;
        frustumCullSphere.radius += maximumOffset;
        frustumCullSphere.radiusY += maximumOffset;
        frustumCullSphere.radiusZ += maximumOffset;
    }
    #endregion
}
