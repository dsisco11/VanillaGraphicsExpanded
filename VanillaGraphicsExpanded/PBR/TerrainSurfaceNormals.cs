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
    internal static void Bind(EnumChunkRenderPass pass)
    {
        // The engine binds the shared terrain shader before rendering each manager.
        if (ShaderProgramBase.CurrentShaderProgram is { } program && ShaderCapabilities.Has(program, ShaderCapability.TwoSidedSurfaceNormals))
            program.Uniform("vge_twoSidedTerrain", IsTwoSided(pass) ? 1 : 0);
    }
    /// <summary>Uses the engine category rather than reverse-classifying a manager.</summary>
    internal static bool IsTwoSided(EnumChunkRenderPass pass) => pass is EnumChunkRenderPass.OpaqueNoCull or EnumChunkRenderPass.BlendNoCull;
    #endregion
}
