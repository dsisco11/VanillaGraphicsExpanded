using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Executes native texture name creation before storage-specific metadata is acquired.</summary>
internal sealed partial class StateCache
{
    #region Internal API
    /// <summary>Creates a typed texture object without changing bindings; native dispatch failure permits bound initialization.</summary>
    internal bool TryCreateTexture(TextureTarget target, out int texture)
    {
        texture = 0;
        try { GL.CreateTextures(target, 1, out texture); }
        catch { return false; }
        return texture != 0;
    }

    /// <summary>Reserves a texture name whose type and state will be initialized by its first binding.</summary>
    internal int GenTexture() => GL.GenTexture();
    #endregion
}
