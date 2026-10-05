namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Defines cleanup order independently of registration order within an engine interruption.</summary>
internal enum EngineBoundaryCleanup
{
    /// <summary>Restore engine-aware shader ownership first.</summary>
    Shader,
    /// <summary>Restore borrowed resource and geometry bindings after shader reactivation.</summary>
    Bindings,
    /// <summary>Restore independent framebuffer bindings before final drawing-state restoration.</summary>
    Framebuffers
}
