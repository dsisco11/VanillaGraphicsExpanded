namespace VanillaGraphicsExpanded.Rendering.RHI;

/// <summary>Supplies two selected images and pixel rectangles for immediate copy/conversion or multisample resolve.</summary>
public readonly record struct RHIImageBlit(RHIAttachment Source, RenderArea SourceArea,
    RHIAttachment Destination, RenderArea DestinationArea, ImageAspects Aspects = ImageAspects.Color,
    SamplerFilter Filter = SamplerFilter.Nearest);
