using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Checks pixel-transfer buffer changes before managed pointers are passed to native upload/readback.</summary>
internal sealed partial class StateCache
{
    #region Private
    /// <summary>Publishes the shared binding cache only after native success; failed unbinds cannot proceed to a pointer transfer.</summary>
    private void BindPixelTransferBuffer(BufferTarget target, int buffer)
    {
        if (TryGetCachedBoundBuffer(target, out int known) && known == buffer) return;
        bufferBindingByTarget.Remove(target);
        CheckBoundaryNativeError();
        GL.BindBuffer(target, buffer);
        CheckBoundaryNativeError();
        bufferBindingByTarget[target] = buffer;
    }
    #endregion
}
