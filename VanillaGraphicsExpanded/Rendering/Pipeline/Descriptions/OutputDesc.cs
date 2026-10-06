namespace VanillaGraphicsExpanded.Rendering.Pipeline.Descriptions;

/// <summary>Framebuffer encoding and supported output interpretation policy.</summary>
internal sealed record OutputDesc
{
    public bool FramebufferSrgb { get; init; }
    public bool Dither { get; init; }
    public bool LogicOperationEnabled => false;
}
