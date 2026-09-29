using VanillaGraphicsExpanded.Rendering.Shaders;
using VanillaGraphicsExpanded.Rendering;
using Vintagestory.API.Client;
using Vintagestory.Client.NoObf;

namespace VanillaGraphicsExpanded.PBR;

/// <summary>Publishes two-sided terrain provenance without altering mesh data or unrelated surface normals.</summary>
internal static class TerrainSurfaceNormals
{
    #region Terrain draw binding
    /// <summary>Sets the normal policy through the existing terrain-manager draw boundary, independently of tessellation.</summary>
    internal static void Bind(MeshDataPoolManager manager, MeshPoolClassifier? pools)
    {
        // The engine binds the shared terrain shader before rendering each manager.
        if (ShaderProgramBase.CurrentShaderProgram is { } program && ShaderCapabilities.Has(program, ShaderCapability.TwoSidedSurfaceNormals))
            program.Uniform("vge_twoSidedTerrain", pools?.IsTwoSided(manager) == true ? 1 : 0);
    }
    #endregion
}
