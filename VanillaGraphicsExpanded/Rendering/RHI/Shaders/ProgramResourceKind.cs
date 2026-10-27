namespace VanillaGraphicsExpanded.Rendering.RHI;

/// <summary>Identifies the semantic executable resource category.</summary>
public enum ProgramResourceKind
{
    /// <summary>Identifies ordinary or opaque uniforms.</summary>
    Uniform,
    /// <summary>Identifies a uniform buffer block.</summary>
    UniformBlock,
    /// <summary>Identifies a shader storage buffer block.</summary>
    ShaderStorageBlock,
    /// <summary>Identifies an atomic counter buffer.</summary>
    AtomicCounterBuffer,
    /// <summary>Identifies a vertex-stage input.</summary>
    ProgramInput,
    /// <summary>Identifies a final-stage output.</summary>
    ProgramOutput
}
