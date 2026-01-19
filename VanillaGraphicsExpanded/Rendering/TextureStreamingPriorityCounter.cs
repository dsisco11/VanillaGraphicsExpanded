namespace VanillaGraphicsExpanded.Rendering;

internal readonly record struct TextureStreamingPriorityCounter(
    long Enqueued,
    long Drained,
    long Fallback,
    long Failed);
