using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Executes raw framebuffer attachment commands through the shared native observation boundary.</summary>
internal sealed partial class StateCache
{
    #region Internal API
    /// <summary>Attaches or detaches a two-dimensional image and refreshes existing framebuffer aliases.</summary>
    internal void FramebufferTexture2D(FramebufferTarget target, FramebufferAttachment slot,
        TextureTarget textureTarget, int texture, int level)
    {
        var observation = BeginNativeFramebufferAttachment((int)target, false, (int)slot, texture, level, -1, 3);
        bool returned = false;
        try
        {
            GL.FramebufferTexture2D(target, slot, textureTarget, texture, level);
            returned = true;
        }
        finally { observation.Complete(returned); }
    }

    /// <summary>Attaches or detaches one array or volume layer without declaring wrapper ownership.</summary>
    internal void FramebufferTextureLayer(FramebufferTarget target, FramebufferAttachment slot,
        int texture, int level, int layer)
    {
        var observation = BeginNativeFramebufferAttachment((int)target, false, (int)slot, texture, level, layer, 1);
        bool returned = false;
        try
        {
            GL.FramebufferTextureLayer(target, slot, texture, level, layer);
            returned = true;
        }
        finally { observation.Complete(returned); }
    }
    #endregion
}
