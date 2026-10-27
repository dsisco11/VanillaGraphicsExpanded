namespace VanillaGraphicsExpanded.Rendering.RHI;

/// <summary>Identifies logical FramebufferSelection selections independently of native API constants.</summary>
public enum FramebufferSelection
{
    /// <summary>Selects Read.</summary>
    Read,
    /// <summary>Selects Draw.</summary>
    Draw,
    /// <summary>Selects ReadAndDraw.</summary>
    ReadAndDraw,
}
