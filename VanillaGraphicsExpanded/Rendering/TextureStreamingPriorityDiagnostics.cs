namespace VanillaGraphicsExpanded.Rendering;

internal readonly record struct TextureStreamingPriorityDiagnostics(
    TextureStreamingPriorityCounter Low,
    TextureStreamingPriorityCounter Normal,
    TextureStreamingPriorityCounter High);
