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
    private readonly Dictionary<int, (int Width, int Height, PixelInternalFormat Format)> images = new();

    #region Public API
    /// <summary>Validates actual scene storage, accepting omitted postprocess dimensions in engine publications.</summary>
    internal bool Prepare(IReadOnlyList<FrameBufferRef> buffers, out string? failure)
    {
        failure = null;
        foreach (var kind in SceneTargets)
        {
            if (buffers.Count <= (int)kind || buffers[(int)kind] is not { } target
                || target.ColorTextureIds is not { Length: > 0 } colors || colors[0] == 0)
            {
                failure = $"{kind} has no published color texture.";
                return false;
            }
            if (kind == EnumFrameBuffer.Primary && (target.Width <= 0 || target.Height <= 0))
            {
                failure = $"Primary has invalid scene dimensions {target.Width}x{target.Height}.";
                return false;
            }
            int id = colors[0];
            if (!images.TryGetValue(id, out var cached)
                || (target.Width > 0 && cached.Width != target.Width)
                || (target.Height > 0 && cached.Height != target.Height))
            {
                // The engine omits dimensions on some postprocess publications (including
                // BlurVerticalLowRes). The allocated image is authoritative in that case.
                // Import once per publication; do not repeatedly query omitted metadata.
                using var image = GpuFramebufferAttachment.FromTextureId(id);
                cached = (image.Width, image.Height, image.InternalFormat);
                images[id] = cached;
            }
            if (cached.Format is not (PixelInternalFormat.Rgba16f or PixelInternalFormat.Rgba32f))
            {
                failure = $"{kind} color texture {id} uses {cached.Format}; floating-point RGBA storage is required.";
                return false;
            }
            if ((target.Width > 0 && cached.Width != target.Width)
                || (target.Height > 0 && cached.Height != target.Height))
            {
                failure = $"{kind} color texture {id} is {cached.Width}x{cached.Height}, "
                    + $"but its publication specifies {target.Width}x{target.Height}.";
                return false;
            }
        }
        return true;
    }

    /// <summary>Forgets borrowed image metadata after engine rebuild or world retirement.</summary>
    internal void Invalidate() => images.Clear();
    #endregion
}
