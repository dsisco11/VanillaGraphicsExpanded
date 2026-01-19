namespace VanillaGraphicsExpanded.Rendering;

internal enum TextureStageFallbackReason
{
    None = 0,
    QueueFull = 1,
    RingFull = 2,
    NotInitialized = 3,
    NoPersistentSupport = 4,
    Oversize = 5,
    Disabled = 6
}
