using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Keeps texture binding snapshots consistent with resource deletion on the context thread.</summary>
internal sealed partial class GlStateCache
{
    #region Texture lifetime
    /// <summary>Deletes a texture and records the implicit unbinding performed by OpenGL.</summary>
    public void DeleteTexture(int textureId)
    {
        GL.DeleteTexture(textureId);
        if (textureId == 0 || textureBindingsByUnit == null) return;

        // Upload scopes restore cached bindings. A deleted name must become zero before
        // a later scope captures it, while bindings of surviving textures remain intact.
        foreach (var bindings in textureBindingsByUnit)
        {
            if (bindings == null) continue;
            foreach (var binding in bindings)
                if (binding.Value == textureId) bindings[binding.Key] = 0;
        }
    }
    #endregion
}
