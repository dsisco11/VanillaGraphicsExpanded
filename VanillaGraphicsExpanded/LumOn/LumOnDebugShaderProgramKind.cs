namespace VanillaGraphicsExpanded.LumOn;

/// <summary>
/// High-level shader program kinds for LumOn debug visualization.
/// Each kind corresponds to a distinct shader entrypoint (program name).
/// </summary>
internal enum LumOnDebugShaderProgramKind
{
    None = 0,

    ProbeAnchors,
    SceneGBuffer,
    Temporal,
    ShInterpolation,
    Indirect,
    ProbeAtlas,
    Composite,
    Direct,
    Velocity,
    WorldProbe,
}
