using System.Collections.Immutable;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Executes private buffer-texture creation while retaining native-width association intent.</summary>
internal sealed partial class StateCache
{
    #region Internal API
    /// <summary>Associates a validated backing selection and returns established facts without result queries.</summary>
    internal GpuTextureMetadata? CreateBufferTexture(in GpuBufferTextureInitialization request, out nint id)
    {
        id = 0;
        int name = 0;
        bool named = GpuSupport.Graphics.SupportsArbDirectStateAccess && TryCreateTexture(TextureTarget.TextureBuffer, out name);
        if (!named) name = GenTexture();
        id = name;
        if (name == 0) return null;
        bool whole = request.Mode == GpuBufferTextureMode.Whole;
        if (named)
        {
            try
            {
                if (whole) GL.TextureBuffer(name, request.Format, request.Buffer);
                else GL.TextureBufferRange(name, request.Format, request.Buffer, request.Offset, request.Size);
            }
            catch { named = false; }
        }
        if (!named)
        {
            // Preserve the established native-entry-point fallback. Publication occurs
            // afterward, so a notification failure can never repeat the native command.
            using var binding = BindTextureScope(TextureTarget.TextureBuffer, 0, name);
            if (whole) GL.TexBuffer(TextureBufferTarget.TextureBuffer, request.Format, request.Buffer);
            else GL.TexBufferRange(TextureBufferTarget.TextureBuffer, request.Format, request.Buffer, request.Offset, request.Size);
        }
        return new GpuTextureMetadata(TextureTarget.TextureBuffer, ImmutableArray<GpuTextureLevelMetadata>.Empty,
            false, 0, 0, 0, new(request.Buffer, request.Format, whole ? 0 : request.Offset, whole ? 0 : request.Size, whole));
    }
    #endregion
}
