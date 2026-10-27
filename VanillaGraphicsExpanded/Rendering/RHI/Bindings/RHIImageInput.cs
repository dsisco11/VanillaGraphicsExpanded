namespace VanillaGraphicsExpanded.Rendering.RHI;

/// <summary>Pairs an authored image entry with an explicit image selection or its shader-defined default view.</summary>
public readonly struct RHIImageInput
{
    #region Public API
    /// <summary>Gets the authored contract entry identity.</summary>
    public ulong Identity { get; }
    /// <summary>Gets the supplied texture image selection.</summary>
    public RHIImageBinding View { get; }
    /// <summary>Reports a default whole texture view whose layering follows the immutable shader entry.</summary>
    public bool DefaultView { get; }
    /// <summary>Captures an explicitly selected view, preserving nonlayered intent independently of omission.</summary>
    public RHIImageInput(ulong identity, RHIImageBinding view) { Identity = identity; View = view; DefaultView = false; }
    /// <summary>Captures a texture whose shader contract determines the default layered selection.</summary>
    public RHIImageInput(ulong identity, GpuTextureRef texture) { Identity = identity; View = new(texture); DefaultView = true; }
    #endregion
}
