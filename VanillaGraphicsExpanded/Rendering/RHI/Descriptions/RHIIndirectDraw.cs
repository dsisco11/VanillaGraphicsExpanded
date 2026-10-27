namespace VanillaGraphicsExpanded.Rendering.RHI;

/// <summary>Selects GPU-produced draw arguments and an optional supported count-buffer interval without reading their contents.</summary>
public readonly record struct RHIIndirectDraw(RHIBufferRange Arguments, int Count = 1, int Stride = 0,
    RHIBufferRange? CountBuffer = null);
