namespace VanillaGraphicsExpanded.LumOn;

/// <summary>
/// Debug visualization modes for the LumOn debug overlay.
/// 
/// Values map directly to the shader's <c>debugMode</c> switch in <c>lumon_debug.fsh</c>.
/// </summary>
public enum LumOnDebugMode
{
    Off = 0,

    ProbeGrid = 1,
    ProbeDepth = 2,
    ProbeNormal = 3,

    SceneDepth = 4,
    SceneNormal = 5,

    TemporalWeight = 6,
    TemporalRejection = 7,

    ShCoefficients = 8,
    InterpolationWeights = 9,

    RadianceOverlay = 10
}
