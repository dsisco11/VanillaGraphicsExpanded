namespace VanillaGraphicsExpanded.Rendering.RHI;

/// <summary>Defines stencil update behavior independently of native token values.</summary>
public enum StencilOperation
{
    Keep,
    Zero,
    Replace,
    IncrementClamp,
    DecrementClamp,
    Invert,
    IncrementWrap,
    DecrementWrap
}
