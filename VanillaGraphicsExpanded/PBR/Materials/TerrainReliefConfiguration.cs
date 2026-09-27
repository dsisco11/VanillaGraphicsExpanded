using VanillaGraphicsExpanded.LumOn;

namespace VanillaGraphicsExpanded.PBR.Materials;

/// <summary>Immutable compile-time relief selection used to detect all shader-affecting setting changes.</summary>
internal readonly record struct TerrainReliefConfiguration(TerrainSurfaceDetailMode Mode, int MinimumSteps,
    int MaximumSteps, int RefinementSteps, float FadeStart, float FadeEnd, float MaximumTexels, int DebugMode)
{
    /// <summary>Captures the sanitized settings without including the obsolete UV-space scale.</summary>
    internal static TerrainReliefConfiguration Capture(VgeConfig.MaterialAtlasConfig config) => new(
        config.TerrainSurfaceDetailMode, config.ParallaxMinSteps, config.ParallaxMaxSteps,
        config.ParallaxRefinementSteps, config.ParallaxFadeStart, config.ParallaxFadeEnd,
        config.ParallaxMaxTexels, config.ParallaxDebugMode);
}
