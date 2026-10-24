using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Projects a selected texture or renderbuffer image without creating another canonical registration.</summary>
public readonly record struct GpuImageMetadata(int Width, int Height, int Depth, PixelInternalFormat Format, int Samples);

/// <summary>Specifies a wrapper-local image selection for projection from canonical storage.</summary>
internal readonly record struct GpuImageSelection(int Level = 0, int Layer = 0, TextureTarget Face = 0,
    bool Layered = false, int BaseView = 0, int ViewCount = 0);
