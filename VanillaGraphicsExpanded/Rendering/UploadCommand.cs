namespace VanillaGraphicsExpanded.Rendering;

internal readonly record struct UploadCommand(
    long SequenceId,
    int Priority,
    TextureUploadRequest Request);
