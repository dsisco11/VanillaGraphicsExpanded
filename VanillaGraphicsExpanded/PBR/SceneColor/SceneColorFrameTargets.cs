using System.Collections.Generic;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using Vintagestory.API.Client;

namespace VanillaGraphicsExpanded.PBR.SceneColor;

/// <summary>Validates borrowed scene storage once per engine image publication, without owning its textures.</summary>
internal sealed class SceneColorFrameTargets
{
    private static readonly EnumFrameBuffer[] SceneTargets =
    [
        EnumFrameBuffer.Primary, EnumFrameBuffer.Luma, EnumFrameBuffer.FindBright,
        EnumFrameBuffer.BlurHorizontalMedRes, EnumFrameBuffer.BlurVerticalMedRes,
        EnumFrameBuffer.BlurHorizontalLowRes, EnumFrameBuffer.BlurVerticalLowRes, EnumFrameBuffer.GodRays
    ];
    private readonly Dictionary<int, (int Width, int Height, bool Linear)> images = new();

    #region Public API
    /// <summary>Rejects missing or integer scene color storage before selecting a linear frame.</summary>
    internal bool Prepare(IReadOnlyList<FrameBufferRef> buffers)
    {
        foreach (var kind in SceneTargets)
        {
            if (buffers.Count <= (int)kind || buffers[(int)kind] is not { } target
                || target.Width <= 0 || target.Height <= 0
                || target.ColorTextureIds is not { Length: > 0 } colors || colors[0] == 0) return false;
            int id = colors[0];
            if (!images.TryGetValue(id, out var cached)
                || cached.Width != target.Width || cached.Height != target.Height)
            {
                // The established attachment abstraction imports metadata at the
                // publication boundary; ordinary frames reuse the recorded result.
                using var image = GpuFramebufferAttachment.FromTextureId(id);
                cached = (image.Width, image.Height,
                    image.InternalFormat is PixelInternalFormat.Rgba16f or PixelInternalFormat.Rgba32f);
                images[id] = cached;
            }
            if (!cached.Linear || cached.Width != target.Width || cached.Height != target.Height) return false;
        }
        return true;
    }

    /// <summary>Forgets borrowed image metadata after engine rebuild or world retirement.</summary>
    internal void Invalidate() => images.Clear();
    #endregion
}
