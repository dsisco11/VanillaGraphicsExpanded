namespace VanillaGraphicsExpanded.Rendering.Pipeline.State;
/// <summary>Explicit draw values; absent values reject when required by the pipeline declaration.</summary>
internal sealed record GraphicsDynamicState
{
    /// <summary>Declared or observed DynamicDrawState? Viewport value.</summary>
    public DynamicDrawState? Viewport { get; init; }
    /// <summary>Declared Scissor value; null means the draw did not supply it.</summary>
    public (int X, int Y, int Width, int Height)? Scissor { get; init; }
    /// <summary>Declared StencilReference value; null means the draw did not supply it.</summary>
    public (int Front, int Back)? StencilReference { get; init; }
    /// <summary>Declared BlendConstant value; null means the draw did not supply it.</summary>
    public (float R, float G, float B, float A)? BlendConstant { get; init; }
}
