namespace VanillaGraphicsExpanded.Rendering;

internal readonly record struct TextureStreamingFallbackDiagnostics(
    long QueueFull,
    long RingFull,
    long NotInitialized,
    long NoPersistentSupport,
    long Oversize,
    long Disabled);
