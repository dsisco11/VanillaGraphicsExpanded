using VanillaGraphicsExpanded.PBR.Tessellation;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace VanillaGraphicsExpanded.Tests.GPU.Fixtures;

/// <summary>Loads the production tessellation assets for focused source and raster tests.</summary>
internal static class TerrainTessellationTestAssets
{
    /// <summary>Supplies the required production terrain interface for source-only declaration tests.</summary>
    internal const string RequiredOutputs = """
        out vec4 worldPos;
        out vec3 normal;
        out vec2 uv;
        flat out vec2 vge_uvBase;
        flat out vec2 vge_uvExtent;
        flat out int renderFlags;
        """;

    #region Asset-backed preparation
    /// <summary>Generates stages using the same asset expansion as production.</summary>
    internal static TerrainTessellationStages.Sources Generate(string source, bool depthBias = false)
    {
        using var fixture = new BinaryShaderApiFixture();
        return TerrainTessellationStages.Generate(source, TerrainTessellationAssets.Load(fixture.Api.Assets), depthBias);
    }

    /// <summary>Prepares an engine program with the actual installed stage assets.</summary>
    internal static void Prepare(IShaderProgram program)
    {
        using var fixture = new BinaryShaderApiFixture();
        TerrainTessellationPatches.Prepare(program, fixture.Api.Assets);
    }

    /// <summary>Reads the shared numerical kernel directly from its production asset.</summary>
    internal static string Common()
    {
        using var fixture = new BinaryShaderApiFixture();
        return fixture.Api.Assets.TryGet(new AssetLocation("vanillagraphicsexpanded", "shaders/includes/tessellation/terrain_displacement.glsl"), true)!.ToText();
    }
    #endregion
}
