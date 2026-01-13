using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

internal static class GlDebug
{
    public static void TryLabel(ObjectLabelIdentifier identifier, int id, string? name)
    {
#if DEBUG
        if (id == 0 || string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        try
        {
            GL.ObjectLabel(identifier, id, -1, name);
        }
        catch
        {
            // Best-effort only: labels depend on context + driver support.
        }
#endif
    }

    public static void TryLabelTexture2D(int textureId, string? name)
    {
        TryLabel(ObjectLabelIdentifier.Texture, textureId, name);
    }

    public static void TryLabelFramebuffer(int framebufferId, string? name)
    {
        TryLabel(ObjectLabelIdentifier.Framebuffer, framebufferId, name);
    }

    public static GroupScope Group(string name)
    {
        return new GroupScope(name);
    }

    public readonly struct GroupScope : IDisposable
    {
        private readonly bool active;

        public GroupScope(string name)
        {
#if DEBUG
            if (string.IsNullOrWhiteSpace(name))
            {
                active = false;
                return;
            }

            try
            {
                GL.PushDebugGroup(DebugSourceExternal.DebugSourceApplication, 0, -1, name);
                active = true;
            }
            catch
            {
                active = false;
            }
#else
            active = false;
#endif
        }

        public void Dispose()
        {
#if DEBUG
            if (!active)
            {
                return;
            }

            try
            {
                GL.PopDebugGroup();
            }
            catch
            {
            }
#endif
        }
    }
}
