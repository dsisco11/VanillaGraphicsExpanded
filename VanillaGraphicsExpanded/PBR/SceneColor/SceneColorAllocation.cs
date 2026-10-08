using OpenTK.Graphics.OpenGL;
using Vintagestory.API.Client;

namespace VanillaGraphicsExpanded.PBR.SceneColor;

/// <summary>Selects floating-point storage for engine-owned scene color without changing data attachments.</summary>
internal struct SceneColorAllocation
{
    private EnumFrameBuffer framebuffer;
    private bool primaryColorSeen;

    #region Public API
    /// <summary>Starts the allocation sequence for a newly assigned engine framebuffer.</summary>
    internal void Begin(EnumFrameBuffer kind)
    {
        framebuffer = kind;
        primaryColorSeen = false;
    }

    /// <summary>Promotes scene color while retaining primary glow, OIT revealage, depth and SSAO formats.</summary>
    internal PixelInternalFormat Select(PixelInternalFormat format)
    {
        if (format != PixelInternalFormat.Rgba8) return format;
        // The engine allocates primary color before its RGBA8 glow attachment. SSAO position
        // and normal attachments already use their own formats and never consume this marker.
        if (framebuffer == EnumFrameBuffer.Primary)
        {
            bool color = !primaryColorSeen;
            primaryColorSeen = true;
            return color ? PixelInternalFormat.Rgba16f : format;
        }

        // Engine glare images remain menu-only; scene glare owns separate floating-point storage.
        return format;
    }
    #endregion
}
