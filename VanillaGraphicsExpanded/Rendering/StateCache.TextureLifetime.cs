using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Keeps texture binding snapshots consistent with resource deletion on the context thread.</summary>
internal sealed partial class StateCache
{
    #region Texture lifetime
    /// <summary>Deletes a texture and records the implicit unbinding performed by OpenGL.</summary>
    public void DeleteTexture(int textureId)
    {
        GL.DeleteTexture(textureId);
        CompleteResourceDeletion(GpuResourceKind.Texture, textureId);
    }
    #endregion
}
