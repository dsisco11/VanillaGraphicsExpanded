using OpenTK.Graphics.OpenGL;

using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.Cache.ArtifactSystem;

internal readonly record struct ArtifactStageCopyUploadPayload(
    int TextureId,
    TextureUploadTarget Target,
    TextureUploadRegion Region,
    PixelFormat PixelFormat,
    PixelType PixelType,
    TextureUploadData Data,
    TextureUploadPriority Priority = TextureUploadPriority.Normal,
    int UnpackAlignment = 1,
    int UnpackRowLength = 0,
    int UnpackImageHeight = 0);
