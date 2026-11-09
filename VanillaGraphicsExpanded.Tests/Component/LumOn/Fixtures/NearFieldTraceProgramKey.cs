using VanillaGraphicsExpanded.LumOn;

namespace VanillaGraphicsExpanded.Tests.Component.LumOn.Fixtures;

/// <summary>Lists every variable shader option used to select the production trace executable.</summary>
internal readonly record struct NearFieldTraceProgramKey(bool NearField, float EmissiveBoost, int TexelsPerFrame,
    bool WorldProbes, int WorldResolution, float WorldSpacing, int WorldTileSize, float RayMaxDistance)
{
    #region Internal API
    /// <summary>Applies every variable and fixed shader option represented by this variant key.</summary>
    internal void Configure(LumOnScreenProbeAtlasTraceShaderProgram shader)
    {
        shader.NearField = NearField;
        shader.EmissiveBoost = EmissiveBoost;
        shader.TexelsPerFrame = TexelsPerFrame;
        shader.WorldProbes = WorldProbes;
        shader.WorldProbeLevels = 1;
        shader.WorldProbeResolution = WorldResolution;
        shader.WorldProbeBaseSpacing = WorldSpacing;
        shader.WorldProbeOctahedralSize = WorldTileSize;
        shader.HzbCoarseMip = 0;
        shader.RayMaxDistance = RayMaxDistance;
    }
    #endregion
}
